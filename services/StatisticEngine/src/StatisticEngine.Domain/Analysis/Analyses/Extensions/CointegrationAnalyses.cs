using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>
/// Cointegración por Engle-Granger y Johansen. Port de <c>econ_multi._cointegration</c>: la prueba de
/// Johansen reproduce <c>statsmodels.tsa.vector_ar.vecm.coint_johansen</c> (incluidas sus tablas de
/// valores críticos) y la de Engle-Granger, <c>statsmodels.tsa.stattools.coint</c>.
/// </summary>
internal static class CointegrationAnalyses
{
    private static readonly (string Value, string Label)[] Deterministic =
    [
        ("-1", "Sin constante ni tendencia"),
        ("0", "Constante en la relación y en los datos"),
        ("1", "Constante y tendencia lineal")
    ];

    private static readonly (double Level, string Label)[] Levels =
    [
        (0.10, "10 %"),
        (0.05, "5 %"),
        (0.01, "1 %")
    ];

    public static readonly AnalysisDefinition Cointegration = new(
        "cointegration",
        "Cointegración (Engle-Granger y Johansen)",
        "Econometría",
        "¿Comparten las series una relación de equilibrio de largo plazo? Traza y máximo autovalor, vectores β y prueba de Engle-Granger.",
        [],
        [],
        [
            new(
                "det_order",
                "Términos deterministas",
                AnalysisParamKinds.Select,
                "0",
                [.. Deterministic.Select(item => new AnalysisParamOption(item.Value, item.Label))],
                false),
            new(
                "lags",
                "Rezagos en diferencias (k − 1)",
                AnalysisParamKinds.Integer,
                2.0,
                [],
                false,
                Minimum: 0,
                Maximum: 12),
            new(
                "alpha",
                "Nivel de significancia",
                AnalysisParamKinds.Select,
                "0.05",
                [.. Levels.Select(item => new AnalysisParamOption(SigmaText.General(item.Level), item.Label))],
                false)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Cointegración",
        Roles:
        [
            new ColumnRole("vars", "Series (2 a 6, en niveles)", ColumnKinds.Numeric, Multiple: true, MinCount: 2, MaxCount: 6),
            new ColumnRole(
                "time",
                "Columna de tiempo (opcional)",
                ColumnKinds.DateTime,
                Optional: true,
                Help: "Sin ella se usa el orden de las filas.")
        ]);

    /// <summary>Resultado de la prueba de Johansen (mismos campos que <c>JohansenTestResult</c>).</summary>
    internal sealed record JohansenResult(
        double[][] Rkt,
        double[][] R0t,
        double[] Eigenvalues,
        double[][] Eigenvectors,
        double[] Trace,
        double[] MaxEigen,
        double[][] TraceCritical,
        double[][] MaxCritical);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var names = input.RoleNames("vars", true).ToArray();
        var timeColumn = ExtendedHelpers.FirstRole(input, "time", "Columna de tiempo");
        var detOrder = (int)Math.Round(input.Pnum("det_order", 0));
        var lags = Math.Max(0, input.Pint("lags", 2));
        var alpha = input.Pnum("alpha", 0.05);
        if (names.Length > 6) return ExtendedHelpers.Invalid("La prueba de Johansen admite hasta 6 series.");

        var loaded = MultiSeriesLoader.Load(frame, names, timeColumn, 40);
        if (loaded.IsError) return Result.Err<AnalysisResult>(loaded.Error.Code, loaded.Error.Message);
        var mts = loaded.Value;
        var k = mts.K;
        var n = mts.N;

        var data = MatrixBuilder.Rows(mts);
        var johansenResult = Johansen(data, detOrder, lags);
        if (johansenResult is null)
        {
            return ExtendedHelpers.Invalid("La prueba de Johansen no pudo estimarse con estos datos.");
        }
        var johansen = johansenResult;

        var level = Levels.MinBy(item => Math.Abs(item.Level - alpha)).Level;
        var column = Array.FindIndex(Levels, item => item.Level == level);
        var traceCritical = Enumerable.Range(0, k).Select(index => johansen.TraceCritical[index][column]).ToArray();
        var maxCritical = Enumerable.Range(0, k).Select(index => johansen.MaxCritical[index][column]).ToArray();

        var rank = 0;
        while (rank < k && johansen.Trace[rank] > traceCritical[rank]) rank++;

        var sections = new List<AnalysisSection>
        {
            new TilesSection(
                "Resumen",
                [
                    new TileItem("Series", JsNumber.ToString(k)),
                    new TileItem("Rango de cointegración (traza)", JsNumber.ToString(rank)),
                    new TileItem("Observaciones", JsNumber.ToString(n)),
                    new TileItem("Nivel", $"{SigmaText.General(100 * (1 - level))} %")
                ])
        };

        if (rank == 0)
        {
            sections.Add(new AlertSection(
                "Conclusión",
                "warning",
                "La prueba de la traza no detecta cointegración: las series no comparten una relación de equilibrio de largo plazo; modele sus diferencias con un VAR."));
        }
        else if (rank >= k)
        {
            sections.Add(new AlertSection(
                "Conclusión",
                "info",
                "El rango es completo: las series ya son estacionarias; use un VAR en niveles."));
        }
        else
        {
            sections.Add(new AlertSection(
                "Conclusión",
                "success",
                $"Hay {rank} relación(es) de cointegración: use un modelo de corrección de errores (VECM) con ese rango."));
        }

        sections.Add(new TableSection(
            "Prueba de la traza",
            ["Hipótesis nula (rango ≤ r)", "Estadístico", "Valor crítico", "¿Se rechaza?"],
            Enumerable.Range(0, k).Select(r => (IReadOnlyList<string>)new[]
            {
                $"r ≤ {r}",
                ExtendedHelpers.N(johansen.Trace[r]),
                ExtendedHelpers.N(traceCritical[r]),
                johansen.Trace[r] > traceCritical[r] ? "sí" : "no"
            }).ToArray(),
            Note: "Se rechazan hipótesis de menor a mayor rango hasta que una no se rechace: ese es el rango estimado."));

        sections.Add(new TableSection(
            "Prueba del máximo autovalor",
            ["Hipótesis nula", "Estadístico", "Valor crítico", "¿Se rechaza?"],
            Enumerable.Range(0, k).Select(r => (IReadOnlyList<string>)new[]
            {
                $"r = {r}",
                ExtendedHelpers.N(johansen.MaxEigen[r]),
                ExtendedHelpers.N(maxCritical[r]),
                johansen.MaxEigen[r] > maxCritical[r] ? "sí" : "no"
            }).ToArray()));

        var relations = Math.Max(rank, 1);
        var beta = new double[k][];
        for (var row = 0; row < k; row++)
        {
            beta[row] = new double[relations];
            for (var relation = 0; relation < relations; relation++) beta[row][relation] = johansen.Eigenvectors[row][relation];
        }
        var normalised = new double[k][];
        for (var row = 0; row < k; row++)
        {
            normalised[row] = new double[relations];
            for (var relation = 0; relation < relations; relation++)
            {
                normalised[row][relation] = beta[row][relation] / beta[0][relation];
            }
        }
        sections.Add(new TableSection(
            "Vectores de cointegración (normalizados con la primera serie)",
            ["Serie", .. Enumerable.Range(0, relations).Select(index => $"β{index + 1}")],
            Enumerable.Range(0, k).Select(row => (IReadOnlyList<string>)[
                names[row],
                .. normalised[row].Select(value => ExtendedHelpers.N(value))
            ]).ToArray(),
            Note: "Cada columna define una combinación estacionaria: β′·y(t) fluctúa alrededor de un nivel estable."
                  + (rank > 0 ? string.Empty : " (Se muestra el primero aunque no se rechace la no cointegración.)")));

        sections.Add(new TableSection(
            "Autovalores",
            ["#", "Autovalor"],
            Enumerable.Range(0, k).Select(index => (IReadOnlyList<string>)new[]
            {
                JsNumber.ToString(index + 1),
                ExtendedHelpers.N(johansen.Eigenvalues[index])
            }).ToArray()));

        var y0 = Enumerable.Range(0, n).Select(row => data[row][0]).ToArray();
        var others = new double[n][];
        for (var row = 0; row < n; row++)
        {
            others[row] = new double[k - 1];
            for (var series = 1; series < k; series++) others[row][series - 1] = data[row][series];
        }
        var engleGranger = EngleGranger(y0, others);
        sections.Add(new TableSection(
            $"Engle-Granger: {names[0]} frente a {string.Join(", ", names.Skip(1))}",
            ["Estadístico ADF de los residuos", "Valor p", "Crítico 1 %", "Crítico 5 %", "Crítico 10 %"],
            [
                new[]
                {
                    ExtendedHelpers.N(engleGranger.Statistic),
                    SigmaText.PValue(engleGranger.PValue),
                    ExtendedHelpers.N(engleGranger.Critical[0]),
                    ExtendedHelpers.N(engleGranger.Critical[1]),
                    ExtendedHelpers.N(engleGranger.Critical[2])
                }
            ],
            Note: "H₀: no hay cointegración (los residuos de la regresión de nivel tienen raíz unitaria). El valor p usa las tablas de MacKinnon."));

        var spread = new double[n];
        for (var row = 0; row < n; row++)
        {
            var total = 0.0;
            for (var series = 0; series < k; series++) total += normalised[series][0] * data[row][series];
            spread[row] = total;
        }
        var chartX = new double[n];
        for (var row = 0; row < n; row++) chartX[row] = row;
        var charts = new List<ChartSpec>
        {
            new SeriesChart(
                "Combinación de largo plazo β′y (debe ser estacionaria si hay cointegración)",
                [new XYSeries("β₁′y", chartX, spread)],
                mts.Dated ? "Tiempo" : "Observación",
                "β′y")
        };

        return Result.Ok(new AnalysisResult(
            "cointegration",
            "Cointegración (Engle-Granger y Johansen)",
            sections,
            Subtitle: $"{k} series · n = {n} · {lags} rezago(s) en diferencias · "
                      + Deterministic.First(item => item.Value == detOrder.ToString(System.Globalization.CultureInfo.InvariantCulture))
                          .Label.ToLowerInvariant(),
            Charts: charts,
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["rank"] = rank,
                ["trace"] = johansen.Trace,
                ["trace_critical"] = traceCritical,
                ["eigenvalues"] = johansen.Eigenvalues,
                ["engle_granger_p"] = engleGranger.PValue,
                ["beta"] = normalised
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    // ------------------------------------------------------------------ Johansen

    /// <summary>Prueba de Johansen compartida con el VECM (misma ruta numérica que <c>coint_johansen</c>).</summary>
    internal static JohansenResult? Johansen(double[][] data, int detOrder, int lags)
    {
        var n = data.Length;
        var k = data[0].Length;
        var filtered = Detrend(data, detOrder);
        var differences = MatrixBuilder.Differences(filtered);
        var rows = differences.Length;
        if (rows - lags <= 0 || n - lags - 1 <= 0) return null;

        // z: rezagos 1..lags de las diferencias, con el mismo recorte que `lagmat(trim="both")`.
        var z = new double[rows - lags][];
        for (var row = 0; row < rows - lags; row++)
        {
            var values = new double[k * lags];
            for (var lag = 1; lag <= lags; lag++)
            {
                for (var variable = 0; variable < k; variable++)
                {
                    values[(lag - 1) * k + variable] = differences[lags + row - lag][variable];
                }
            }
            z[row] = values;
        }
        var f = detOrder > -1 ? 0 : detOrder;
        z = Detrend(z, f);

        var dx = new double[rows - lags][];
        for (var row = 0; row < rows - lags; row++) dx[row] = differences[lags + row];
        dx = Detrend(dx, f);
        var r0t = Residuals(dx, z);

        var levels = new double[n - lags - 1][];
        for (var row = 0; row < n - lags - 1; row++) levels[row] = filtered[row + 1];
        levels = Detrend(levels, f);
        var rkt = Residuals(levels, z);

        var count = rkt.Length;
        var skk = MatrixBuilder.Scale(MatrixBuilder.Cross(rkt, rkt), 1.0 / count);
        var sk0 = MatrixBuilder.Scale(MatrixBuilder.Cross(rkt, r0t), 1.0 / count);
        var s00 = MatrixBuilder.Scale(MatrixBuilder.Cross(r0t, r0t), 1.0 / count);
        var inverseS00 = LinearAlgebra.Inverse(s00);
        if (inverseS00.IsError) return null;
        var sigma = LinearAlgebra.Multiply(LinearAlgebra.Multiply(sk0, inverseS00.Value), LinearAlgebra.Transpose(sk0));

        var lower = CholeskyDecomposition.Lower(skk);
        if (lower.IsError) return null;
        var lowerInverse = CholeskyDecomposition.InverseLower(lower.Value);
        if (lowerInverse.IsError) return null;

        // S = L⁻¹·Σ·L⁻ᵀ es simétrica y tiene los mismos autovalores que skk⁻¹·Σ.
        var left = LinearAlgebra.Multiply(lowerInverse.Value, sigma);
        var symmetric = LinearAlgebra.Multiply(left, LinearAlgebra.Transpose(lowerInverse.Value));
        var eigen = LinearAlgebra.EigenSymmetric(MatrixBuilder.Symmetrize(symmetric));

        // Vectores propios de skk⁻¹·Σ a partir de los de S: u = L⁻ᵀ·v.
        var order = Enumerable.Range(0, k).OrderByDescending(index => eigen.Values[index]).ToArray();
        var eigenvalues = order.Select(index => eigen.Values[index]).ToArray();
        var vectors = new double[k][];
        for (var row = 0; row < k; row++)
        {
            vectors[row] = new double[k];
            for (var relation = 0; relation < k; relation++)
            {
                var column = order[relation];
                var total = 0.0;
                for (var index = 0; index < k; index++) total += lowerInverse.Value[index][row] * eigen.Vectors[index][column];
                vectors[row][relation] = total;
            }
        }

        var normalization = LinearAlgebra.Multiply(LinearAlgebra.Multiply(LinearAlgebra.Transpose(vectors), skk), vectors);
        var normalizationCholesky = CholeskyDecomposition.Lower(MatrixBuilder.Symmetrize(normalization));
        if (normalizationCholesky.IsError) return null;
        var normalizationInverse = CholeskyDecomposition.InverseLower(normalizationCholesky.Value);
        if (normalizationInverse.IsError) return null;
        var scaled = LinearAlgebra.Multiply(vectors, normalizationInverse.Value);

        var firstNonZero = 0.0;
        var found = false;
        for (var row = 0; row < k && !found; row++)
        {
            for (var relation = 0; relation < k && !found; relation++)
            {
                if (scaled[row][relation] != 0)
                {
                    firstNonZero = Math.Sign(scaled[row][relation]);
                    found = true;
                }
            }
        }
        if (found)
        {
            for (var row = 0; row < k; row++)
            {
                for (var relation = 0; relation < k; relation++) scaled[row][relation] *= firstNonZero;
            }
        }

        var trace = new double[k];
        var maxEigen = new double[k];
        for (var index = 0; index < k; index++)
        {
            var sum = 0.0;
            for (var j = index; j < k; j++) sum += Math.Log(1 - eigenvalues[j]);
            trace[index] = -count * sum;
            maxEigen[index] = -count * Math.Log(1 - eigenvalues[index]);
        }

        var traceCritical = new double[k][];
        var maxCritical = new double[k][];
        for (var index = 0; index < k; index++)
        {
            traceCritical[index] = JohansenCritical(k - index, detOrder, traceTable: true);
            maxCritical[index] = JohansenCritical(k - index, detOrder, traceTable: false);
        }

        return new JohansenResult(rkt, r0t, eigenvalues, scaled, trace, maxEigen, traceCritical, maxCritical);
    }

    private static double[] JohansenCritical(int n, int detOrder, bool traceTable)
    {
        var table = (traceTable, detOrder) switch
        {
            (true, -1) => CointegrationTables.TraceP0,
            (true, 0) => CointegrationTables.TraceP1,
            (true, _) => CointegrationTables.TraceP2,
            (false, -1) => CointegrationTables.MaxP0,
            (false, 0) => CointegrationTables.MaxP1,
            _ => CointegrationTables.MaxP2
        };
        var row = Math.Clamp(n, 1, table.Length) - 1;
        return table[row];
    }

    /// <summary>Residuos de la regresión de <paramref name="y"/> sobre <paramref name="x"/> sin constante.</summary>
    private static double[][] Residuals(double[][] y, double[][] x)
    {
        if (x.Length == 0 || x[0].Length == 0) return y;
        var columns = x[0].Length;
        var xtx = MatrixBuilder.Cross(x, x);
        var inverse = LinearAlgebra.Inverse(xtx);
        if (inverse.IsError) return y;
        var xty = MatrixBuilder.Cross(x, y);
        var coefficients = LinearAlgebra.Multiply(inverse.Value, xty);
        var fitted = LinearAlgebra.Multiply(x, coefficients);
        var output = new double[y.Length][];
        for (var row = 0; row < y.Length; row++)
        {
            output[row] = new double[y[row].Length];
            for (var column = 0; column < y[row].Length; column++)
            {
                output[row][column] = y[row][column] - fitted[row][column];
            }
        }
        return output;
    }

    /// <summary>
    /// Elimina la tendencia polinómica de orden <paramref name="order"/> (paridad con <c>detrend</c> de
    /// <c>coint_johansen</c>, que regresa sobre <c>vander(linspace(-1, 1, n), order + 1)</c>).
    /// </summary>
    private static double[][] Detrend(double[][] data, int order)
    {
        if (order < 0) return data;
        var n = data.Length;
        if (n == 0) return data;
        var columns = data[0].Length;
        var design = new double[n][];
        for (var row = 0; row < n; row++)
        {
            var value = n == 1 ? 0 : -1 + 2.0 * row / (n - 1);
            var powers = new double[order + 1];
            var current = 1.0;
            for (var power = 0; power <= order; power++)
            {
                powers[power] = current;
                current *= value;
            }
            design[row] = powers;
        }

        var xtx = MatrixBuilder.Cross(design, design);
        var inverse = LinearAlgebra.Inverse(xtx);
        if (inverse.IsError) return data;
        var xty = MatrixBuilder.Cross(design, data);
        var coefficients = LinearAlgebra.Multiply(inverse.Value, xty);
        var fitted = LinearAlgebra.Multiply(design, coefficients);
        var output = new double[n][];
        for (var row = 0; row < n; row++)
        {
            output[row] = new double[columns];
            for (var column = 0; column < columns; column++) output[row][column] = data[row][column] - fitted[row][column];
        }
        return output;
    }

    // ------------------------------------------------------------------ Engle-Granger

    private sealed record EngleGrangerResult(double Statistic, double PValue, double[] Critical);

    private static EngleGrangerResult EngleGranger(double[] y0, double[][] y1)
    {
        var n = y0.Length;
        var k = y1[0].Length + 1;
        var design = new double[n][];
        for (var row = 0; row < n; row++)
        {
            design[row] = new double[k];
            for (var column = 0; column < y1[row].Length; column++) design[row][column] = y1[row][column];
            design[row][k - 1] = 1;
        }
        var fit = LinearModel.Fit(design, y0);
        var residuals = fit.IsError ? y0 : fit.Value.Residuals;
        var statistic = AdfNoConstant(residuals);
        var pValue = MacKinnonP(statistic, "c", k);
        var critical = MacKinnonCritical(k, "c", n - 1);
        return new EngleGrangerResult(statistic, pValue, critical);
    }

    /// <summary>ADF sin deterministas con selección de rezagos por AIC (paridad con <c>adfuller(regression="n")</c>).</summary>
    private static double AdfNoConstant(double[] y)
    {
        var n = y.Length;
        var differences = new double[n - 1];
        for (var index = 0; index < differences.Length; index++) differences[index] = y[index + 1] - y[index];
        var maxLag = (int)Math.Ceiling(12.0 * Math.Pow(n / 100.0, 1.0 / 4.0));
        maxLag = Math.Min(n / 2 - 1, maxLag);
        maxLag = Math.Max(0, maxLag);

        var rows = differences.Length - maxLag;
        var response = new double[rows];
        for (var row = 0; row < rows; row++) response[row] = differences[maxLag + row];

        var bestLag = 0;
        var bestAic = double.PositiveInfinity;
        for (var lag = 0; lag <= maxLag; lag++)
        {
            var design = AdfDesign(y, differences, maxLag, lag, rows);
            var candidate = LinearModel.Fit(design, response);
            var aic = candidate.IsError ? double.PositiveInfinity : candidate.Value.Aic;
            if (double.IsNaN(aic)) aic = double.PositiveInfinity;
            if (aic < bestAic)
            {
                bestAic = aic;
                bestLag = lag;
            }
        }

        var finalRows = differences.Length - bestLag;
        var finalResponse = new double[finalRows];
        for (var row = 0; row < finalRows; row++) finalResponse[row] = differences[bestLag + row];
        var finalDesign = AdfDesign(y, differences, bestLag, bestLag, finalRows);
        var final = LinearModel.Fit(finalDesign, finalResponse);
        return final.IsError ? double.NaN : final.Value.TValues[0];
    }

    private static double[][] AdfDesign(double[] y, double[] differences, int maxLag, int lags, int rows)
    {
        var design = new double[rows][];
        for (var row = 0; row < rows; row++)
        {
            var values = new double[1 + lags];
            values[0] = y[maxLag + row];
            for (var lag = 1; lag <= lags; lag++) values[lag] = differences[maxLag + row - lag];
            design[row] = values;
        }
        return design;
    }

    /// <summary>p-valor de MacKinnon (1994) para <c>N</c> variables I(1).</summary>
    private static double MacKinnonP(double statistic, string regression, int n)
    {
        var index = Math.Clamp(n, 1, 6) - 1;
        var (max, min, star, small, large) = regression switch
        {
            "n" => (
                CointegrationTables.TauMaxN[index],
                CointegrationTables.TauMinN[index],
                CointegrationTables.TauStarN[index],
                CointegrationTables.TauSmallN[index],
                CointegrationTables.TauLargeN[index]),
            "ct" => (
                CointegrationTables.TauMaxCt[index],
                CointegrationTables.TauMinCt[index],
                CointegrationTables.TauStarCt[index],
                CointegrationTables.TauSmallCt[index],
                CointegrationTables.TauLargeCt[index]),
            "ctt" => (
                CointegrationTables.TauMaxCtt[index],
                CointegrationTables.TauMinCtt[index],
                CointegrationTables.TauStarCtt[index],
                CointegrationTables.TauSmallCtt[index],
                CointegrationTables.TauLargeCtt[index]),
            _ => (
                CointegrationTables.TauMaxC[index],
                CointegrationTables.TauMinC[index],
                CointegrationTables.TauStarC[index],
                CointegrationTables.TauSmallC[index],
                CointegrationTables.TauLargeC[index])
        };
        if (statistic > max) return 1.0;
        if (statistic < min) return 0.0;
        var coefficients = statistic <= star ? small : large;
        return SpecialFunctions.NormalCdf(Polynomial(coefficients, statistic));
    }

    /// <summary>Valores críticos de MacKinnon (2010) para <c>N</c> variables I(1) y <paramref name="nobs"/> observaciones.</summary>
    private static double[] MacKinnonCritical(int n, string regression, int nobs)
    {
        var table = regression switch
        {
            "n" => CointegrationTables.TauN,
            "ct" => CointegrationTables.TauCt,
            "ctt" => CointegrationTables.TauCtt,
            _ => CointegrationTables.TauC
        };
        var row = table[Math.Clamp(n, 1, table.Length) - 1];
        var inverse = 1.0 / nobs;
        var output = new double[row.Length];
        for (var percentile = 0; percentile < row.Length; percentile++)
        {
            var total = 0.0;
            var power = 1.0;
            foreach (var coefficient in row[percentile])
            {
                total += coefficient * power;
                power *= inverse;
            }
            output[percentile] = total;
        }
        return output;
    }

    private static double Polynomial(double[] coefficients, double value)
    {
        var total = 0.0;
        var power = 1.0;
        foreach (var coefficient in coefficients)
        {
            total += coefficient * power;
            power *= value;
        }
        return total;
    }
}

/// <summary>Operaciones de matrices fila a fila usadas por los análisis de sistemas de series.</summary>
internal static class MatrixBuilder
{
    public static double[][] Rows(MultiSeries series)
    {
        var n = series.N;
        var k = series.K;
        var output = new double[n][];
        for (var row = 0; row < n; row++)
        {
            output[row] = new double[k];
            for (var column = 0; column < k; column++) output[row][column] = series.Values[column][row];
        }
        return output;
    }

    public static double[][] Differences(double[][] data)
    {
        var rows = data.Length - 1;
        var output = new double[rows][];
        for (var row = 0; row < rows; row++)
        {
            output[row] = new double[data[row].Length];
            for (var column = 0; column < data[row].Length; column++) output[row][column] = data[row + 1][column] - data[row][column];
        }
        return output;
    }

    /// <summary><c>Aᵀ·B</c>.</summary>
    public static double[][] Cross(double[][] a, double[][] b) =>
        LinearAlgebra.Multiply(LinearAlgebra.Transpose(a), b);

    public static double[][] Scale(double[][] matrix, double factor)
    {
        var output = new double[matrix.Length][];
        for (var row = 0; row < matrix.Length; row++)
        {
            output[row] = new double[matrix[row].Length];
            for (var column = 0; column < matrix[row].Length; column++) output[row][column] = matrix[row][column] * factor;
        }
        return output;
    }

    public static double[][] Symmetrize(double[][] matrix)
    {
        var n = matrix.Length;
        var output = new double[n][];
        for (var row = 0; row < n; row++)
        {
            output[row] = new double[n];
            for (var column = 0; column < n; column++)
            {
                output[row][column] = 0.5 * (matrix[row][column] + matrix[column][row]);
            }
        }
        return output;
    }
}
