using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>
/// ARDL y prueba de límites de Pesaran-Shin-Smith. Port de <c>econ_models._ardl</c> con la
/// selección de orden de <c>statsmodels.tsa.ardl.ardl_select_order</c>, la estimación por MCO del
/// ARDL y del UECM, la prueba de límites (tablas PSS) y los multiplicadores de largo plazo por el
/// método delta.
/// </summary>
internal static class ArdlAnalyses
{
    private static readonly (string Value, string Label)[] InformationCriteria =
    [
        ("aic", "AIC"),
        ("bic", "BIC"),
        ("hqic", "HQIC")
    ];

    private static readonly (string Value, string Label)[] Cases =
    [
        ("1", "Caso 1: sin constante ni tendencia"),
        ("3", "Caso 3: constante sin restringir"),
        ("5", "Caso 5: constante y tendencia sin restringir")
    ];

    public static readonly AnalysisDefinition Ardl = new(
        "ardl",
        "ARDL y prueba de límites (bounds test)",
        "Econometría",
        "Relación dinámica entre una variable y sus determinantes aunque mezclen órdenes de integración I(0) e I(1): multiplicadores de largo plazo y prueba de cointegración.",
        [],
        [],
        [
            new(
                "max_lag_y",
                "Rezagos máximos de Y",
                AnalysisParamKinds.Integer,
                4.0,
                [],
                false,
                Minimum: 1,
                Maximum: 12),
            new(
                "max_lag_x",
                "Rezagos máximos de cada X",
                AnalysisParamKinds.Integer,
                4.0,
                [],
                false,
                Minimum: 0,
                Maximum: 12),
            new(
                "ic",
                "Criterio de selección",
                AnalysisParamKinds.Select,
                "aic",
                [.. InformationCriteria.Select(item => new AnalysisParamOption(item.Value, item.Label))],
                false),
            new(
                "case",
                "Términos deterministas (casos de Pesaran)",
                AnalysisParamKinds.Select,
                "3",
                [.. Cases.Select(item => new AnalysisParamOption(item.Value, item.Label))],
                false),
            ExtendedHelpers.Alpha()
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Modelos dinámicos",
        Roles:
        [
            new ColumnRole("y", "Variable dependiente", ColumnKinds.Numeric),
            new ColumnRole(
                "x",
                "Variables explicativas (1 a 4)",
                ColumnKinds.Numeric,
                Multiple: true,
                MinCount: 1,
                MaxCount: 4),
            new ColumnRole(
                "time",
                "Columna de tiempo (opcional)",
                ColumnKinds.DateTime,
                Optional: true,
                Help: "Sin ella se usa el orden de las filas.")
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var responseName = ExtendedHelpers.FirstRole(input, "y", "Variable dependiente");
        var predictorNames = input.RoleNames("x", true).ToArray();
        var timeColumn = ExtendedHelpers.FirstRole(input, "time", "Columna de tiempo");
        var alpha = input.Pnum("alpha", 0.05);
        var maxLagY = input.Pint("max_lag_y", 4);
        var maxLagX = input.Pint("max_lag_x", 4);
        var criterion = input.Pstr("ic", "aic");
        var code = input.Pstr("case", "3");
        var trend = code switch
        {
            "1" => "n",
            "5" => "ct",
            _ => "c"
        };

        var names = new List<string> { responseName };
        names.AddRange(predictorNames);
        var loaded = MultiSeriesLoader.Load(frame, names, timeColumn, 40);
        if (loaded.IsError) return Result.Err<AnalysisResult>(loaded.Error.Code, loaded.Error.Message);
        var mts = loaded.Value;
        var n = mts.N;
        var response = mts.Values[0];
        var predictors = new double[predictorNames.Length][];
        for (var index = 0; index < predictorNames.Length; index++) predictors[index] = mts.Values[index + 1];

        var holdBack = Math.Max(maxLagY, maxLagX);
        if (n - holdBack <= 0) return ExtendedHelpers.Invalid("La serie es demasiado corta para esos rezagos.");

        var (lagsY, lagsX) = SelectOrder(response, predictors, trend, maxLagY, maxLagX, criterion, holdBack);
        if (lagsX < 0)
        {
            return ExtendedHelpers.Invalid(
                "El criterio de información no incluyó ninguna variable explicativa: no hay relación dinámica entre las series.");
        }

        var orderHoldBack = Math.Max(lagsY, lagsX);
        var fit = EstimateArdl(response, predictors, responseName, predictorNames, trend, lagsY, lagsX, orderHoldBack);
        if (fit.IsError) return Result.Err<AnalysisResult>(fit.Error.Code, fit.Error.Message);
        var ardl = fit.Value;

        var uecm = EstimateUecm(response, predictors, trend, lagsY, lagsX, orderHoldBack);
        if (uecm.IsError) return Result.Err<AnalysisResult>(uecm.Error.Code, uecm.Error.Message);
        var bounds = BoundsTest(uecm.Value, predictorNames.Length, int.Parse(code, System.Globalization.CultureInfo.InvariantCulture));
        if (bounds is null) return ExtendedHelpers.Invalid("No se pudo calcular la prueba de límites.");

        var order = $"ARDL({lagsY}, {lagsX})";
        var caseLabel = Cases.First(item => item.Value == code).Label.ToLowerInvariant();
        var sections = new List<AnalysisSection>
        {
            new TilesSection(
                "Resumen",
                [
                    new TileItem("Modelo", order),
                    new TileItem("Observaciones efectivas", JsNumber.ToString(ardl.Nobs)),
                    new TileItem("R²", JsNumber.ToString(ardl.R2)),
                    new TileItem("Estadístico F de límites", JsNumber.ToString(bounds.Statistic))
                ])
        };

        var critical = InferenceDistributions.TQuantile(1 - alpha / 2, ardl.DfResid);
        var coefficientRows = new List<IReadOnlyList<string>>();
        for (var index = 0; index < ardl.TermNames.Count; index++)
        {
            coefficientRows.Add([
                ardl.TermNames[index],
                ExtendedHelpers.N(ardl.Coefficients[index]),
                ExtendedHelpers.N(ardl.StandardErrors[index]),
                ExtendedHelpers.N(ardl.TValues[index]),
                SigmaText.PValue(ardl.PValues[index]),
                ExtendedHelpers.N(ardl.Coefficients[index] - critical * ardl.StandardErrors[index]),
                ExtendedHelpers.N(ardl.Coefficients[index] + critical * ardl.StandardErrors[index])
            ]);
        }
        sections.Add(new TableSection(
            "Coeficientes del ARDL",
            [
                "Término",
                "Coeficiente",
                "Error est.",
                "t",
                "Valor p",
                $"IC {SigmaText.General(100 * (1 - alpha))} % inf.",
                "IC sup."
            ],
            coefficientRows,
            Note: "Los nombres indican la variable y su rezago (x.L1 = un periodo atrás)."));

        var boundsRows = new List<IReadOnlyList<string>>();
        for (var percentile = 0; percentile < PssCriticalValues.Percentiles.Length; percentile++)
        {
            boundsRows.Add([
                $"{SigmaText.General(PssCriticalValues.Percentiles[percentile])} %",
                ExtendedHelpers.N(bounds.Lower[percentile]),
                ExtendedHelpers.N(bounds.Upper[percentile])
            ]);
        }
        sections.Add(new TableSection(
            "Prueba de límites de Pesaran-Shin-Smith (H₀: no hay relación de largo plazo)",
            ["Nivel de significancia", "Límite inferior I(0)", "Límite superior I(1)"],
            boundsRows,
            Note: $"Estadístico F = {Fixed(bounds.Statistic, 3)}. Por encima del límite superior se rechaza H₀ (hay cointegración); por debajo del inferior no; entre ambos la prueba no concluye."));

        var fiveLower = bounds.Lower[1];
        var fiveUpper = bounds.Upper[1];
        var five = Fixed(fiveUpper, 2);
        if (bounds.Statistic > fiveUpper)
        {
            sections.Add(new AlertSection(
                "Conclusión",
                "success",
                $"F = {Fixed(bounds.Statistic, 2)} supera el límite superior al 5 % ({five}): hay una relación de largo plazo entre las series."));
        }
        else if (bounds.Statistic < fiveLower)
        {
            sections.Add(new AlertSection(
                "Conclusión",
                "warning",
                $"F = {Fixed(bounds.Statistic, 2)} es menor que el límite inferior al 5 % ({Fixed(fiveLower, 2)}): no hay evidencia de relación de largo plazo."));
        }
        else
        {
            sections.Add(new AlertSection(
                "Conclusión",
                "info",
                $"F = {Fixed(bounds.Statistic, 2)} cae entre los límites al 5 % ({Fixed(fiveLower, 2)} y {five}): la prueba no concluye; determine antes el orden de integración."));
        }

        var arPositions = Enumerable.Range(0, lagsY)
            .Select(lag => ardl.DeterministicCount + lag)
            .ToArray();
        var arSum = arPositions.Sum(position => ardl.Coefficients[position]);
        var longRunRows = new List<IReadOnlyList<string>>();
        var longRunValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var predictor = 0; predictor < predictorNames.Length; predictor++)
        {
            var positions = Enumerable.Range(0, lagsX + 1)
                .Select(lag => ardl.DeterministicCount + lagsY + predictor * (lagsX + 1) + lag)
                .ToArray();
            var betaSum = positions.Sum(position => ardl.Coefficients[position]);
            var denominator = 1 - arSum;
            if (Math.Abs(denominator) < 1e-12) continue;
            var gradient = new double[ardl.Coefficients.Length];
            foreach (var position in positions) gradient[position] = 1 / denominator;
            foreach (var position in arPositions) gradient[position] = betaSum / (denominator * denominator);
            var theta = betaSum / denominator;
            var variance = 0.0;
            for (var i = 0; i < gradient.Length; i++)
            {
                for (var j = 0; j < gradient.Length; j++) variance += gradient[i] * ardl.Covariance[i][j] * gradient[j];
            }
            var error = Math.Sqrt(Math.Max(0, variance));
            var zValue = error > 0 ? theta / error : double.NaN;
            longRunRows.Add([
                predictorNames[predictor],
                ExtendedHelpers.N(theta),
                ExtendedHelpers.N(error),
                ExtendedHelpers.N(zValue),
                SigmaText.PValue(ExtendedHelpers.NormalTwoSided(zValue))
            ]);
            longRunValues[predictorNames[predictor]] = theta;
        }
        sections.Add(new TableSection(
            "Multiplicadores de largo plazo (método delta)",
            ["Variable", "Efecto de largo plazo", "Error est.", "z", "Valor p"],
            longRunRows,
            Note: "Efecto acumulado de X sobre Y cuando el sistema vuelve al equilibrio: Σβ / (1 − Σφ)."));

        var speed = uecm.Value.Coefficients[uecm.Value.SpeedIndex];
        var speedError = uecm.Value.StandardErrors[uecm.Value.SpeedIndex];
        var speedPValue = uecm.Value.PValues[uecm.Value.SpeedIndex];
        sections.Add(new TableSection(
            "Corrección de errores (de la forma UECM)",
            ["Coeficiente de ajuste", "Error est.", "Valor p"],
            [
                new[]
                {
                    ExtendedHelpers.N(speed),
                    ExtendedHelpers.N(speedError),
                    SigmaText.PValue(speedPValue)
                }
            ],
            Note: speed < 0
                ? "Debe ser negativo y significativo: la fracción de la desviación del equilibrio que se corrige cada periodo."
                : "Un coeficiente de ajuste no negativo contradice la corrección hacia el equilibrio."));

        var keep = ardl.Fitted.Length;
        var axis = new double[keep];
        var observed = new double[keep];
        for (var index = 0; index < keep; index++)
        {
            axis[index] = index;
            observed[index] = response[n - keep + index];
        }
        var charts = new List<ChartSpec>
        {
            new SeriesChart(
                "Serie y valores ajustados",
                [
                    new XYSeries("Datos", axis, observed),
                    new XYSeries("Ajustado", axis, ardl.Fitted, "dash")
                ],
                mts.Dated ? "Tiempo" : "Observación",
                responseName)
        };

        return Result.Ok(new AnalysisResult(
            "ardl",
            $"ARDL y prueba de límites: {responseName}",
            sections,
            Subtitle: $"{order} elegido por {criterion.ToUpperInvariant()} · {caseLabel} · n = {ardl.Nobs}",
            Charts: charts,
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["order"] = new[] { lagsY, lagsX },
                ["f_bounds"] = bounds.Statistic,
                ["bounds_p_upper"] = bounds.PValueUpper,
                ["long_run"] = longRunValues,
                ["speed_of_adjustment"] = speed,
                ["ar_sum"] = arSum
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static string Fixed(double value, int digits) =>
        value.ToString("F" + digits.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ selección de orden

    private static int DeterministicCount(string trend) => trend switch
    {
        "n" => 0,
        "ct" => 2,
        _ => 1
    };

    /// <summary>
    /// Rejilla de órdenes de <c>ardl_select_order</c>: todos los modelos usan la misma muestra
    /// (recortada por el rezago máximo pedido) y el criterio se calcula sobre la verosimilitud MLE.
    /// </summary>
    private static (int LagsY, int LagsX) SelectOrder(
        double[] response,
        double[][] predictors,
        string trend,
        int maxLagY,
        int maxLagX,
        string criterion,
        int holdBack)
    {
        var rows = response.Length - holdBack;
        var deterministic = DeterministicCount(trend);
        var bestLagsY = 0;
        var bestLagsX = -1;
        var bestValue = double.PositiveInfinity;
        for (var lagsY = 0; lagsY <= maxLagY; lagsY++)
        {
            for (var orderX = -1; orderX <= maxLagX; orderX++)
            {
                var columns = lagsY + (orderX < 0 ? 0 : predictors.Length * (orderX + 1));
                if (deterministic + columns >= rows) continue;
                var design = BuildDesign(response, predictors, trend, lagsY, orderX, holdBack, rows);
                var target = new double[rows];
                for (var row = 0; row < rows; row++) target[row] = response[holdBack + row];
                var fit = LinearModel.Fit(design, target);
                if (fit.IsError) continue;
                var sse = 0.0;
                foreach (var residual in fit.Value.Residuals) sse += residual * residual;
                var sigma2 = sse / rows;
                var logLikelihood = -rows * (Math.Log(2 * Math.PI * sigma2) + 1) / 2;
                var parameters = deterministic + columns + 1;
                var value = criterion switch
                {
                    "bic" => -2 * logLikelihood + Math.Log(rows) * parameters,
                    "hqic" => -2 * logLikelihood + 2 * Math.Log(Math.Log(rows)) * parameters,
                    _ => -2 * logLikelihood + 2 * parameters
                };
                if (value < bestValue)
                {
                    bestValue = value;
                    bestLagsY = lagsY;
                    bestLagsX = orderX;
                }
            }
        }
        return (bestLagsY, bestLagsX);
    }

    /// <summary>Diseño del ARDL: [deterministas, y.L1…y.Lp, x.L0…x.Lq] sobre las filas indicadas.</summary>
    private static double[][] BuildDesign(
        double[] response,
        double[][] predictors,
        string trend,
        int lagsY,
        int orderX,
        int holdBack,
        int rows)
    {
        var deterministic = DeterministicCount(trend);
        var columns = deterministic + lagsY + (orderX < 0 ? 0 : predictors.Length * (orderX + 1));
        var design = new double[rows][];
        for (var row = 0; row < rows; row++)
        {
            var time = holdBack + row;
            var values = new double[columns];
            var column = 0;
            if (deterministic > 0) values[column++] = 1;
            if (deterministic > 1) values[column++] = time + 1.0;
            for (var lag = 1; lag <= lagsY; lag++) values[column++] = response[time - lag];
            if (orderX >= 0)
            {
                foreach (var predictor in predictors)
                {
                    for (var lag = 0; lag <= orderX; lag++) values[column++] = predictor[time - lag];
                }
            }
            design[row] = values;
        }
        return design;
    }

    // ------------------------------------------------------------------ estimación

    private sealed record ArdlFit(
        int Nobs,
        int DfResid,
        int DeterministicCount,
        double[] Coefficients,
        double[] StandardErrors,
        double[] TValues,
        double[] PValues,
        double[][] Covariance,
        double[] Fitted,
        double R2,
        IReadOnlyList<string> TermNames);

    private static Result<ArdlFit> EstimateArdl(
        double[] response,
        double[][] predictors,
        string responseName,
        string[] predictorNames,
        string trend,
        int lagsY,
        int lagsX,
        int holdBack)
    {
        var rows = response.Length - holdBack;
        if (rows <= 0) return Result.Err<ArdlFit>(DomainErrorCode.InsufficientData, "No hay observaciones suficientes.");
        var design = BuildDesign(response, predictors, trend, lagsY, lagsX, holdBack, rows);
        var target = new double[rows];
        for (var row = 0; row < rows; row++) target[row] = response[holdBack + row];

        var fit = LinearModel.Fit(design, target);
        if (fit.IsError) return Result.Err<ArdlFit>(fit.Error.Code, fit.Error.Message);
        var value = fit.Value;

        var deterministic = DeterministicCount(trend);
        var terms = new List<string>();
        if (deterministic > 0) terms.Add("const");
        if (deterministic > 1) terms.Add("trend");
        for (var lag = 1; lag <= lagsY; lag++) terms.Add($"{responseName}.L{lag}");
        foreach (var name in predictorNames)
        {
            for (var lag = 0; lag <= lagsX; lag++) terms.Add($"{name}.L{lag}");
        }

        // statsmodels calcula el R² del ARDL sin centrar (el modelo no declara constante propia:
        // los términos deterministas vienen del proceso determinista).
        var sse = 0.0;
        var total = 0.0;
        foreach (var residual in value.Residuals) sse += residual * residual;
        foreach (var observation in target) total += observation * observation;
        var uncenteredR2 = total > 0 ? 1 - sse / total : double.NaN;

        return Result.Ok(new ArdlFit(
            rows,
            (int)Math.Round(value.DegreesOfFreedom),
            deterministic,
            value.Coefficients,
            value.StandardErrors,
            value.TValues,
            value.PValues,
            value.Covariance,
            value.Fitted,
            uncenteredR2,
            terms));
    }

    private sealed record UecmFit(
        int SpeedIndex,
        double[] Coefficients,
        double[] StandardErrors,
        double[] PValues,
        double[][] Covariance)
    {
        public double CovarianceAt(int row, int column) => Covariance[row][column];
    }

    /// <summary>
    /// UECM asociado al ARDL: Δy sobre [deterministas, y.L1, x.L1…, Δy rezagos, Δx rezagos].
    /// Es una reparametrización lineal del ARDL, así que comparte residuos y grados de libertad.
    /// </summary>
    private static Result<UecmFit> EstimateUecm(
        double[] response,
        double[][] predictors,
        string trend,
        int lagsY,
        int lagsX,
        int holdBack)
    {
        var differences = new double[response.Length];
        for (var index = 1; index < response.Length; index++) differences[index] = response[index] - response[index - 1];
        var rows = response.Length - holdBack;
        if (rows <= 0) return Result.Err<UecmFit>(DomainErrorCode.InsufficientData, "No hay observaciones suficientes.");

        var deterministic = DeterministicCount(trend);
        var laggedY = Math.Max(0, lagsY - 1);
        var laggedX = Math.Max(0, lagsX);
        var columns = deterministic + 1 + predictors.Length + laggedY + predictors.Length * laggedX;
        var design = new double[rows][];
        var target = new double[rows];
        for (var row = 0; row < rows; row++)
        {
            var time = holdBack + row;
            var values = new double[columns];
            var column = 0;
            if (deterministic > 0) values[column++] = 1;
            if (deterministic > 1) values[column++] = time + 1.0;
            values[column++] = response[time - 1];
            foreach (var predictor in predictors) values[column++] = predictor[time - 1];
            for (var lag = 1; lag <= laggedY; lag++) values[column++] = differences[time - lag];
            foreach (var predictor in predictors)
            {
                for (var lag = 0; lag < laggedX; lag++) values[column++] = predictor[time - lag] - predictor[time - lag - 1];
            }
            design[row] = values;
            target[row] = differences[time];
        }

        var fit = LinearModel.Fit(design, target);
        if (fit.IsError) return Result.Err<UecmFit>(fit.Error.Code, fit.Error.Message);
        var value = fit.Value;
        return Result.Ok(new UecmFit(deterministic, value.Coefficients, value.StandardErrors, value.PValues, value.Covariance));
    }

    private sealed record BoundsResult(double Statistic, double[] Lower, double[] Upper, double PValueUpper);

    private static BoundsResult? BoundsTest(UecmFit uecm, int predictors, int @case)
    {
        var variables = 1 + predictors;
        var start = @case switch
        {
            1 => 0,
            5 => 2,
            _ => 1
        };
        var restrictions = Enumerable.Range(start, variables).ToArray();
        var covariance = new double[restrictions.Length][];
        var coefficients = new double[restrictions.Length];
        for (var i = 0; i < restrictions.Length; i++)
        {
            coefficients[i] = uecm.Coefficients[restrictions[i]];
            covariance[i] = new double[restrictions.Length];
            for (var j = 0; j < restrictions.Length; j++) covariance[i][j] = uecm.CovarianceAt(restrictions[i], restrictions[j]);
        }
        var solved = LinearAlgebra.SolveLinear(covariance, coefficients);
        if (solved.IsError) return null;
        var quadratic = 0.0;
        for (var index = 0; index < coefficients.Length; index++) quadratic += coefficients[index] * solved.Value[index];
        var statistic = quadratic / restrictions.Length;

        var k = Math.Clamp(variables, 1, PssCriticalValues.Critical.Length);
        var caseIndex = Math.Clamp(@case, 1, PssCriticalValues.Critical[0].Length) - 1;
        var lower = PssCriticalValues.Critical[k - 1][caseIndex][0];
        var upper = PssCriticalValues.Critical[k - 1][caseIndex][1];
        var pUpper = PssPValue(statistic, k, caseIndex, upper: true);
        return new BoundsResult(statistic, lower, upper, pUpper);
    }

    private static double PssPValue(double statistic, int k, int caseIndex, bool upper)
    {
        var side = upper ? 1 : 0;
        var threshold = PssCriticalValues.StatStar[k - 1][caseIndex][side];
        var coefficients = statistic > threshold
            ? PssCriticalValues.SmallP[k - 1][caseIndex][side]
            : PssCriticalValues.LargeP[k - 1][caseIndex][side];
        var log = Math.Log(statistic);
        var total = 0.0;
        var power = 1.0;
        foreach (var coefficient in coefficients)
        {
            total += coefficient * power;
            power *= log;
        }
        return 1 - SpecialFunctions.NormalCdf(total);
    }
}
