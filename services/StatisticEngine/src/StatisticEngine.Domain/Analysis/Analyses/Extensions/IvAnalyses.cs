using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Variables instrumentales (2MCO) con primera etapa, Wu-Hausman y Sargan (port de <c>models_more._iv</c>).</summary>
internal static class IvAnalyses
{
    private static readonly AnalysisParamDefinition Covariance = new(
        "cov",
        "Errores estándar",
        AnalysisParamKinds.Select,
        "robust",
        [new("robust", "Robustos"), new("unadjusted", "Clásicos")],
        false);

    public static readonly AnalysisDefinition Iv = new(
        "regression-iv",
        "Variables instrumentales (2MCO)",
        "Regresión",
        "Corrige la endogeneidad con instrumentos: primera etapa, Wu-Hausman y Sargan.",
        [],
        [],
        [Covariance, ModelHelpers.Alpha()],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Endogeneidad, efectos mixtos y diagnóstico",
        Roles:
        [
            new ColumnRole("response", "Respuesta (Y)", ColumnKinds.Numeric),
            new ColumnRole("endogenous", "Variables endógenas", ColumnKinds.Numeric, Multiple: true),
            new ColumnRole("exogenous", "Controles exógenos (opcional)", ColumnKinds.Any, Multiple: true, Optional: true),
            new ColumnRole("instruments", "Instrumentos", ColumnKinds.Numeric, Multiple: true)
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var response = ExtendedHelpers.FirstRole(input, "response", "Respuesta (Y)");
        var endogenous = input.RoleNames("endogenous", true);
        var exogenous = input.RoleNames("exogenous", true);
        var instruments = input.RoleNames("instruments", true);
        var alpha = input.Pnum("alpha", 0.05);
        var covType = input.Pstr("cov", "robust");

        if (instruments.Count < endogenous.Count)
        {
            return ExtendedHelpers.Invalid(
                $"Se necesitan al menos tantos instrumentos ({instruments.Count}) como variables endógenas ({endogenous.Count}).");
        }
        if (endogenous.Intersect(instruments).Any() || endogenous.Intersect(exogenous).Any() || instruments.Intersect(exogenous).Any())
        {
            return ExtendedHelpers.Invalid("Una columna no puede tener dos funciones (endógena, exógena o instrumento).");
        }
        var all = new List<string> { response };
        all.AddRange(endogenous);
        all.AddRange(exogenous);
        all.AddRange(instruments);
        var designResult = DesignBuilder.Build(frame, response, [.. endogenous, .. exogenous, .. instruments]);
        if (designResult.IsError) return Result.Err<AnalysisResult>(designResult.Error.Code, designResult.Error.Message);
        var design = designResult.Value;
        if (design.FactorTokens.Count > 0)
        {
            return ExtendedHelpers.Error(
                DomainErrorCode.NotSupported,
                "La versión actual de variables instrumentales solo admite columnas numéricas.");
        }

        string Token(string name) => design.Names.First(pair => pair.Value == name).Key;
        var n = design.RowCount;
        var y = design.Response;
        var exogColumns = new List<double[]> { Enumerable.Repeat(1.0, n).ToArray() };
        exogColumns.AddRange(exogenous.Select(name => design.Data.Numeric(Token(name))));
        var endogColumns = endogenous.Select(name => design.Data.Numeric(Token(name))).ToArray();
        var instrumentColumns = instruments.Select(name => design.Data.Numeric(Token(name))).ToArray();
        var exog = ToMatrix(exogColumns, n);
        var endog = ToMatrix(endogColumns, n);
        var z = ToMatrix([.. exogColumns, .. instrumentColumns], n);
        var x = ToMatrix([.. exogColumns, .. endogColumns], n);

        var iv = FitTwoStage(x, z, y, covType == "robust");
        var ols = LinearModel.Fit(x, y);
        if (iv.IsError) return Result.Err<AnalysisResult>(iv.Error.Code, iv.Error.Message);
        if (ols.IsError) return Result.Err<AnalysisResult>(ols.Error.Code, ols.Error.Message);
        var fit = iv.Value;

        var firstStage = new List<(string Name, double PartialR2, double F, double P)>();
        for (var index = 0; index < endogColumns.Length; index++)
        {
            var target = endogColumns[index];
            var first = FirstStage(target, exog, instrumentColumns, n, covType == "robust");
            firstStage.Add((design.Pretty(Token(endogenous[index])), first.PartialR2, first.F, first.P));
        }
        var weakest = firstStage.Min(item => item.F);

        // Wu-Hausman de linearmodels: proyecciones de los residuos MCO y de 2MCO.
        var instrumentsMatrix = ToMatrix(instrumentColumns, n);
        var z1 = ToMatrix([.. exogColumns, .. instrumentColumns, .. endogColumns], n);
        var e1 = Project(ols.Value.Residuals, z1, n);
        var e2 = Project(fit.Residuals, instrumentsMatrix, n);
        var delta = e1.Sum(value => value * value) - e2.Sum(value => value * value);
        var q = endogenous.Count;
        var v = n - exog[0].Length - endogenous.Count - q;
        var wuStatistic = v > 0 ? (delta / q) / ((ols.Value.Residuals.Sum(value => value * value) - delta) / v) : double.NaN;
        var wuPValue = ExtendedStatistics.FSf(wuStatistic, q, Math.Max(1, v));

        double sarganStatistic = double.NaN;
        double sarganPValue = double.NaN;
        if (instruments.Count > endogenous.Count)
        {
            var residualFit = LinearModel.Fit(z, fit.Residuals);
            if (residualFit.IsOk)
            {
                sarganStatistic = n * residualFit.Value.R2;
                sarganPValue = ExtendedStatistics.ChiSquareSf(sarganStatistic, instruments.Count - endogenous.Count);
            }
        }

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("R²", ModelHelpers.N(fit.R2)),
                new TileItem("F de la primera etapa (mínimo)", ModelHelpers.N(weakest)),
                new TileItem("Sobreidentificación", instruments.Count > endogenous.Count ? "sí" : "no")
            ])
        };
        ModelHelpers.DroppedNote(sections, design);
        if (weakest < 10)
        {
            sections.Add(new AlertSection(
                "Aviso",
                "warning",
                $"Instrumentos débiles: el F de la primera etapa ({weakest.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}) es menor que 10. "
                + "Las estimaciones por variables instrumentales pueden estar muy sesgadas e imprecisas."));
        }

        var critical = covType == "robust" ? SpecialFunctions.NormalInv(1 - alpha / 2) : InferenceDistributions.TQuantile(1 - alpha / 2, fit.DegreesOfFreedom);
        var coefficientRows = new List<IReadOnlyList<string>>();
        var coefficientNames = new List<string>();
        for (var index = 0; index < exogColumns.Count; index++) coefficientNames.Add(index == 0 ? "Constante" : design.Pretty(Token(exogenous[index - 1])));
        coefficientNames.AddRange(endogenous.Select(name => design.Pretty(Token(name)) + " (endógena)"));
        for (var index = 0; index < fit.Coefficients.Length; index++)
        {
            var name = coefficientNames[index];
            coefficientRows.Add([
                name,
                ModelHelpers.N(fit.Coefficients[index]),
                ModelHelpers.N(fit.StandardErrors[index]),
                ModelHelpers.N(fit.TValues[index]),
                ModelHelpers.P(fit.PValues[index]),
                ModelHelpers.N(fit.Coefficients[index] - critical * fit.StandardErrors[index]),
                ModelHelpers.N(fit.Coefficients[index] + critical * fit.StandardErrors[index]),
                ModelHelpers.N(ols.Value.Coefficients[index])
            ]);
        }
        sections.Add(new TableSection(
            "Estimaciones de 2MCO",
            ["Término", "Coeficiente", "Error est.", "t", "Valor p", .. ModelHelpers.CiColumns(alpha), "Coef. MCO"],
            coefficientRows,
            Note: "La última columna es la estimación por MCO ignorando la endogeneidad: si difiere mucho, la corrección importa."));

        sections.Add(new TableSection(
            "Primera etapa por variable endógena",
            ["Variable", "R² parcial", "F", "Valor p"],
            firstStage.Select(item => (IReadOnlyList<string>)new[]
            {
                item.Name,
                ModelHelpers.N(item.PartialR2),
                ModelHelpers.N(item.F),
                ModelHelpers.P(item.P)
            }).ToArray()));

        var tests = new List<IReadOnlyList<string>>
        {
            new[] { "Wu-Hausman (¿son endógenas?)", ModelHelpers.N(wuStatistic), ModelHelpers.P(wuPValue) }
        };
        if (instruments.Count > endogenous.Count)
        {
            tests.Add(new[] { "Sargan (sobreidentificación: ¿instrumentos válidos?)", ModelHelpers.N(sarganStatistic), ModelHelpers.P(sarganPValue) });
        }
        sections.Add(new TableSection(
            "Pruebas de especificación",
            ["Prueba", "Estadístico", "Valor p"],
            tests,
            Note: "Wu-Hausman con valor p pequeño: la variable es endógena y MCO sería inconsistente. Sargan con valor p pequeño: algún instrumento no es válido."));

        var coefficients = new Dictionary<string, object?>(StringComparer.Ordinal);
        var keyNames = new List<string>();
        for (var index = 0; index < exogColumns.Count; index++) keyNames.Add(index == 0 ? "Intercept" : design.Pretty(Token(exogenous[index - 1])));
        keyNames.AddRange(endogenous.Select(name => design.Pretty(Token(name))));
        for (var index = 0; index < fit.Coefficients.Length; index++) coefficients[keyNames[index]] = fit.Coefficients[index];

        return Result.Ok(new AnalysisResult(
            "regression-iv",
            $"Variables instrumentales (2MCO): {response}",
            sections,
            Subtitle: $"endógenas: {string.Join(", ", endogenous)} · instrumentos: {string.Join(", ", instruments)} · n = {n}",
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["coefficients"] = coefficients,
                ["first_stage_f"] = weakest,
                ["wu_hausman_p"] = wuPValue
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private sealed record IvFit(
        double[] Coefficients,
        double[] StandardErrors,
        double[] TValues,
        double[] PValues,
        double[] Residuals,
        double R2,
        int DegreesOfFreedom);

    private static Result<IvFit> FitTwoStage(double[][] x, double[][] z, double[] y, bool robust)
    {
        var n = y.Length;
        var ztz = Multiply(Transpose(z), z);
        var ztzInverse = LinearAlgebra.Inverse(ztz);
        if (ztzInverse.IsError) return Result.Err<IvFit>(ztzInverse.Error.Code, ztzInverse.Error.Message);
        var xz = Multiply(Transpose(x), z);
        var right = Multiply(Multiply(xz, ztzInverse.Value), Transpose(z));
        var a = Multiply(right, x);
        var aInverse = LinearAlgebra.Inverse(a);
        if (aInverse.IsError) return Result.Err<IvFit>(aInverse.Error.Code, aInverse.Error.Message);
        var beta = Multiply(aInverse.Value, Multiply(right, y));
        var residuals = new double[n];
        var fitted = new double[n];
        var sse = 0.0;
        for (var row = 0; row < n; row++)
        {
            var prediction = 0.0;
            for (var column = 0; column < beta.Length; column++) prediction += x[row][column] * beta[column];
            fitted[row] = prediction;
            residuals[row] = y[row] - prediction;
            sse += residuals[row] * residuals[row];
        }
        var k = x[0].Length;
        var df = n - k;
        var sigma2 = sse / df;
        double[][] covariance;
        if (!robust)
        {
            covariance = Scale(aInverse.Value, sigma2);
        }
        else
        {
            var b = Multiply(Multiply(aInverse.Value, xz), ztzInverse.Value);
            var meat = new double[z[0].Length][];
            for (var i = 0; i < meat.Length; i++) meat[i] = new double[meat.Length];
            for (var row = 0; row < n; row++)
            {
                var factor = residuals[row] * residuals[row];
                for (var i = 0; i < z[row].Length; i++)
                {
                    for (var j = 0; j < z[row].Length; j++) meat[i][j] += factor * z[row][i] * z[row][j];
                }
            }
            covariance = Multiply(Multiply(b, meat), Transpose(b));
        }
        var mean = y.Average();
        var sst = y.Sum(value => Math.Pow(value - mean, 2));
        var standardErrors = new double[k];
        var tValues = new double[k];
        var pValues = new double[k];
        for (var index = 0; index < k; index++)
        {
            standardErrors[index] = Math.Sqrt(Math.Max(0, covariance[index][index]));
            tValues[index] = standardErrors[index] > 0 ? beta[index] / standardErrors[index] : double.NaN;
            pValues[index] = robust
                ? 2 * (1 - SpecialFunctions.NormalCdf(Math.Abs(tValues[index])))
                : 2 * (1 - InferenceDistributions.TCdf(Math.Abs(tValues[index]), df));
        }
        return Result.Ok(new IvFit(beta, standardErrors, tValues, pValues, residuals, sst > 0 ? 1 - sse / sst : 0, df));
    }

    private sealed record FirstStageResult(double PartialR2, double F, double P, double[] Residuals);

    private static FirstStageResult FirstStage(
        double[] target,
        double[][] exog,
        double[][] instruments,
        int n,
        bool robust)
    {
        var exogFit = LinearModel.Fit(exog, target);
        var residualTarget = new double[n];
        var fittedTarget = exogFit.IsOk ? exogFit.Value.Fitted : new double[n];
        for (var row = 0; row < n; row++) residualTarget[row] = target[row] - fittedTarget[row];

        var residualInstruments = new double[instruments.Length][];
        for (var index = 0; index < instruments.Length; index++)
        {
            var fit = LinearModel.Fit(exog, instruments[index][..n]);
            residualInstruments[index] = new double[n];
            for (var row = 0; row < n; row++) residualInstruments[index][row] = instruments[index][row] - (fit.IsOk ? fit.Value.Fitted[row] : 0);
        }
        var matrix = new double[n][];
        for (var row = 0; row < n; row++)
        {
            matrix[row] = new double[instruments.Length];
            for (var column = 0; column < instruments.Length; column++) matrix[row][column] = residualInstruments[column][row];
        }
        var regression = LinearModel.Fit(matrix, residualTarget);
        var r2 = regression.IsOk ? regression.Value.R2 : double.NaN;

        var full = new List<double[]>(exog.Length + instruments.Length);
        for (var row = 0; row < n; row++)
        {
            var columns = new List<double>(exog.Length + instruments.Length);
            for (var column = 0; column < exog[0].Length; column++) columns.Add(exog[row][column]);
            for (var column = 0; column < instruments.Length; column++) columns.Add(instruments[column][row]);
            full.Add([.. columns]);
        }
        var fullFit = LinearModel.Fit(full.ToArray(), target, covariance: robust ? "hc0" : "classical");
        var m = instruments.Length;
        var statistic = double.NaN;
        var p = double.NaN;
        if (fullFit.IsOk)
        {
            var parameters = fullFit.Value.Parameters;
            var beta = fullFit.Value.Coefficients[(parameters - m)..];
            var covariance = new double[m][];
            for (var i = 0; i < m; i++)
            {
                covariance[i] = new double[m];
                for (var j = 0; j < m; j++) covariance[i][j] = fullFit.Value.Covariance[parameters - m + i][parameters - m + j];
            }
            var inverse = LinearAlgebra.Inverse(covariance);
            if (inverse.IsOk)
            {
                statistic = 0.0;
                for (var i = 0; i < m; i++)
                {
                    for (var j = 0; j < m; j++) statistic += beta[i] * inverse.Value[i][j] * beta[j];
                }
                p = robust
                    ? ExtendedStatistics.ChiSquareSf(statistic, m)
                    : ExtendedStatistics.FSf(statistic / m, m, n - parameters);
                if (!robust) statistic /= m;
            }
        }
        return new FirstStageResult(r2, statistic, p, residualTarget);
    }

    private static double[] Project(double[] values, double[][] matrix, int n)
    {
        var fit = LinearModel.Fit(matrix, values);
        if (fit.IsError) return new double[n];
        return fit.Value.Fitted;
    }

    private static double[][] ToMatrix(IReadOnlyList<double[]> columns, int rows)
    {
        var matrix = new double[rows][];
        for (var row = 0; row < rows; row++)
        {
            matrix[row] = new double[columns.Count];
            for (var column = 0; column < columns.Count; column++) matrix[row][column] = columns[column][row];
        }
        return matrix;
    }

    private static double[][] Transpose(double[][] matrix)
    {
        var result = new double[matrix[0].Length][];
        for (var i = 0; i < result.Length; i++) result[i] = new double[matrix.Length];
        for (var i = 0; i < matrix.Length; i++)
        {
            for (var j = 0; j < matrix[0].Length; j++) result[j][i] = matrix[i][j];
        }
        return result;
    }

    private static double[][] Multiply(double[][] a, double[][] b)
    {
        var result = new double[a.Length][];
        for (var i = 0; i < a.Length; i++)
        {
            result[i] = new double[b[0].Length];
            for (var k = 0; k < b.Length; k++)
            {
                var left = a[i][k];
                if (left == 0) continue;
                for (var j = 0; j < b[0].Length; j++) result[i][j] += left * b[k][j];
            }
        }
        return result;
    }

    private static double[] Multiply(double[][] a, double[] v)
    {
        var result = new double[a.Length];
        for (var i = 0; i < a.Length; i++)
        {
            for (var j = 0; j < v.Length; j++) result[i] += a[i][j] * v[j];
        }
        return result;
    }

    private static double[][] Scale(double[][] matrix, double factor)
    {
        var result = new double[matrix.Length][];
        for (var i = 0; i < matrix.Length; i++)
        {
            result[i] = new double[matrix[i].Length];
            for (var j = 0; j < matrix[i].Length; j++) result[i][j] = matrix[i][j] * factor;
        }
        return result;
    }
}
