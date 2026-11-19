using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Regresión no lineal con modelos incorporados (port de <c>models._nonlinear</c>).</summary>
internal static class NonlinearAnalyses
{
    private static readonly (string Key, string Expression, string[] Names, string Label)[] Models =
    [
        ("exponential", "a * exp(b * x)", ["a", "b"], "Crecimiento exponencial"),
        ("decay", "a * exp(-b * x) + c", ["a", "b", "c"], "Decaimiento exponencial con asíntota"),
        ("power", "a * x ** b", ["a", "b"], "Potencia"),
        ("logistic", "a / (1 + exp(-b * (x - c)))", ["a", "b", "c"], "Crecimiento logístico"),
        ("michaelis", "a * x / (b + x)", ["a", "b"], "Michaelis-Menten (saturación)"),
        ("gompertz", "a * exp(-b * exp(-c * x))", ["a", "b", "c"], "Gompertz"),
        ("quadratic_peak", "a + b * x + c * x ** 2", ["a", "b", "c"], "Parábola"),
        ("custom", "", [], "Fórmula propia")
    ];

    public static readonly AnalysisDefinition Nonlinear = new(
        "regression-nonlinear",
        "Regresión no lineal",
        "Regresión",
        "Ajusta curvas de crecimiento, saturación o la fórmula que usted escriba, con errores estándar y banda de confianza.",
        [],
        [],
        [
            new(
                "model",
                "Modelo",
                AnalysisParamKinds.Select,
                "michaelis",
                Models.Select(item => new AnalysisParamOption(item.Key, item.Label)).ToArray(),
                false),
            new(
                "expression",
                "Fórmula con x y los parámetros",
                AnalysisParamKinds.Text,
                null,
                [],
                true,
                Help: "Ejemplo: a * x ** b + c",
                VisibleIf: new("model", ["custom"])),
            new(
                "parameters",
                "Nombres de los parámetros",
                AnalysisParamKinds.Text,
                null,
                [],
                true,
                Help: "Separados por comas: a, b, c",
                VisibleIf: new("model", ["custom"])),
            new(
                "start",
                "Valores iniciales (opcional)",
                AnalysisParamKinds.Text,
                null,
                [],
                true,
                Help: "Uno por parámetro, separados por comas; vacío = automáticos."),
            new AnalysisParamDefinition("alpha", "Nivel de significancia α", AnalysisParamKinds.Number, 0.05, [], false, Minimum: 0.001, Maximum: 0.5)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Lineales robustos y flexibles",
        Roles:
        [
            new ColumnRole("x", "Variable X", ColumnKinds.Numeric),
            new ColumnRole("y", "Respuesta Y", ColumnKinds.Numeric)
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var xName = ExtendedHelpers.FirstRole(input, "x", "Variable X");
        var yName = ExtendedHelpers.FirstRole(input, "y", "Respuesta Y");
        var kind = input.Pstr("model", "michaelis");
        if (kind == "custom")
        {
            return ExtendedHelpers.Error(
                DomainErrorCode.NotSupported,
                "La fórmula personalizada aún no está disponible en este motor; elija uno de los modelos incorporados.");
        }
        var definition = Models.FirstOrDefault(item => item.Key == kind);
        if (definition.Key is null) return ExtendedHelpers.Invalid("Modelo no reconocido.");
        var complete = frame.CompleteCases([xName, yName]);
        if (complete.RowCount < 6) return ExtendedHelpers.Invalid("Se necesitan al menos 6 pares de datos.");
        var x = complete.Numeric(xName);
        var y = complete.Numeric(yName);
        var n = x.Length;
        var names = definition.Names;
        var k = names.Length;
        var function = Function(kind);
        double[] start;
        var startText = input.Pstr("start", string.Empty).Trim();
        if (startText.Length > 0)
        {
            var parsed = startText.Replace(';', ',')
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : double.NaN)
                .ToArray();
            if (parsed.Length != k || parsed.Any(value => !double.IsFinite(value)))
            {
                return ExtendedHelpers.Invalid($"Indique {k} valores iniciales (uno por parámetro: {string.Join(", ", names)}).");
            }
            start = parsed;
        }
        else
        {
            start = StartingValues(kind, x, y);
        }

        var fitResult = Fit(function, x, y, start);
        if (fitResult.IsError) return Result.Err<AnalysisResult>(fitResult.Error.Code, fitResult.Error.Message);
        var fit = fitResult.Value;
        var fitted = x.Select(value => function(value, fit.Coefficients)).ToArray();
        var residuals = y.Select((value, index) => value - fitted[index]).ToArray();
        var sse = residuals.Sum(value => value * value);
        var mean = y.Average();
        var sst = y.Sum(value => Math.Pow(value - mean, 2));
        var r2 = sst > 0 ? 1 - sse / sst : double.NaN;
        var df = n - k;
        var sigma = Math.Sqrt(sse / df);
        var critical = InferenceDistributions.TQuantile(1 - input.Pnum("alpha", 0.05) / 2, df);
        var alpha = input.Pnum("alpha", 0.05);

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("R²", ModelHelpers.N(r2)),
                new TileItem("Error estándar residual", ModelHelpers.N(sigma)),
                new TileItem("AIC", ModelHelpers.N(n * Math.Log(sse / n) + 2 * (k + 1)))
            ])
        };
        var rows = new List<IReadOnlyList<string>>();
        for (var index = 0; index < k; index++)
        {
            var estimate = fit.Coefficients[index];
            var error = Math.Sqrt(Math.Max(0, fit.Covariance[index][index]));
            var t = estimate / error;
            rows.Add([
                names[index],
                ModelHelpers.N(estimate),
                ModelHelpers.N(error),
                ModelHelpers.N(t),
                ModelHelpers.P(2 * (1 - InferenceDistributions.TCdf(Math.Abs(t), df))),
                ModelHelpers.N(estimate - critical * error),
                ModelHelpers.N(estimate + critical * error)
            ]);
        }
        sections.Add(new TableSection(
            "Parámetros estimados",
            ["Parámetro", "Estimación", "Error est.", "t", "Valor p", .. ModelHelpers.CiColumns(alpha)],
            rows,
            Note: "Errores estándar por aproximación lineal (matriz de covarianza del ajuste)."));

        var grid = Enumerable.Range(0, 150).Select(index => x.Min() + (x.Max() - x.Min()) * index / 149.0).ToArray();
        var curve = grid.Select(value => function(value, fit.Coefficients)).ToArray();
        var curvePoints = new List<ScatterPoint>(n);
        for (var index = 0; index < n; index++) curvePoints.Add(new ScatterPoint(x[index], y[index]));
        var residualPoints = new List<ScatterPoint>(n);
        for (var index = 0; index < n; index++) residualPoints.Add(new ScatterPoint(fitted[index], residuals[index]));

        var coefficients = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 0; index < k; index++) coefficients[names[index]] = fit.Coefficients[index];

        return Result.Ok(new AnalysisResult(
            "regression-nonlinear",
            $"Regresión no lineal: {definition.Label}",
            sections,
            Subtitle: $"y = {definition.Expression} · n = {n}",
            Charts:
            [
                new SeriesChart(
                    $"{yName} según {xName}",
                    [
                        new XYSeries("Datos", x, y),
                        new XYSeries("Ajuste", grid, curve)
                    ],
                    xName,
                    yName),
                new ScatterChart("Residuos frente a valores ajustados", residualPoints, "Valor ajustado", "Residuo")
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["r2"] = r2,
                ["parameters"] = coefficients
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Func<double, double[], double> Function(string kind) => kind switch
    {
        "exponential" => (x, p) => p[0] * Math.Exp(p[1] * x),
        "decay" => (x, p) => p[0] * Math.Exp(-p[1] * x) + p[2],
        "power" => (x, p) => p[0] * Math.Pow(x, p[1]),
        "logistic" => (x, p) => p[0] / (1 + Math.Exp(-p[1] * (x - p[2]))),
        "michaelis" => (x, p) => p[0] * x / (p[1] + x),
        "gompertz" => (x, p) => p[0] * Math.Exp(-p[1] * Math.Exp(-p[2] * x)),
        _ => (x, p) => p[0] + p[1] * x + p[2] * x * x
    };

    private static double[] StartingValues(string kind, double[] x, double[] y)
    {
        var spanY = y.Max() - y.Min();
        if (spanY == 0) spanY = 1;
        var spanX = x.Max() - x.Min();
        if (spanX == 0) spanX = 1;
        var top = y.Max();
        return kind switch
        {
            "exponential" => [Math.Max(y.Min(), 1e-3), 1 / spanX],
            "decay" => [spanY, 3 / spanX, y.Min()],
            "power" => [Math.Max(top / Math.Max(Math.Pow(x.Max(value => Math.Abs(value)), 1), 1e-9), 1e-3), 1],
            "logistic" => [top * 1.1, 4 / spanX, Median(x)],
            "michaelis" => [top * 1.2, Median(x)],
            "gompertz" => [top * 1.1, 2, 3 / spanX],
            _ => [y.Average(), 0, 0]
        };
    }

    private static double Median(double[] values)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
    }

    private sealed record NonlinearFit(double[] Coefficients, double[][] Covariance);

    private static Result<NonlinearFit> Fit(Func<double, double[], double> function, double[] x, double[] y, double[] start)
    {
        var k = start.Length;
        // La parábola es lineal: mínimos cuadrados exactos.
        if (k == 3 && Math.Abs(start[0] - y.Average()) < 1e-12 && start[1] == 0 && start[2] == 0)
        {
            var design = x.Select(value => new[] { 1.0, value, value * value }).ToArray();
            var linear = LinearModel.Fit(design, y);
            if (linear.IsError) return Result.Err<NonlinearFit>(linear.Error.Code, linear.Error.Message);
            var covariance = new double[k][];
            for (var i = 0; i < k; i++)
            {
                covariance[i] = new double[k];
                for (var j = 0; j < k; j++) covariance[i][j] = linear.Value.Covariance[i][j];
            }
            return Result.Ok(new NonlinearFit(linear.Value.Coefficients, covariance));
        }

        var beta = (double[])start.Clone();
        for (var iteration = 0; iteration < 300; iteration++)
        {
            var jacobian = new double[x.Length][];
            var residuals = new double[x.Length];
            for (var row = 0; row < x.Length; row++)
            {
                var value = function(x[row], beta);
                residuals[row] = y[row] - value;
                jacobian[row] = new double[k];
                for (var column = 0; column < k; column++)
                {
                    var step = 1e-7 * Math.Max(1, Math.Abs(beta[column]));
                    var shifted = (double[])beta.Clone();
                    shifted[column] += step;
                    jacobian[row][column] = (function(x[row], shifted) - value) / step;
                }
            }
            var jtj = new double[k][];
            var jtr = new double[k];
            for (var i = 0; i < k; i++)
            {
                jtj[i] = new double[k];
                for (var j = 0; j < k; j++)
                {
                    for (var row = 0; row < x.Length; row++) jtj[i][j] += jacobian[row][i] * jacobian[row][j];
                }
                for (var row = 0; row < x.Length; row++) jtr[i] += jacobian[row][i] * residuals[row];
            }
            for (var i = 0; i < k; i++) jtj[i][i] += 1e-12;
            var solved = LinearAlgebra.SolveLinear(jtj, jtr);
            if (solved.IsError) return Result.Err<NonlinearFit>(solved.Error.Code, solved.Error.Message);
            var stepValue = solved.Value;
            var previous = beta;
            beta = beta.Select((value, index) => value + stepValue[index]).ToArray();
            var improvement = stepValue.Sum(value => Math.Abs(value));
            if (improvement < 1e-10) break;
            if (iteration == 299 || !double.IsFinite(improvement))
            {
                return Result.Err<NonlinearFit>(DomainErrorCode.NonConvergence, "El ajuste no convergió; pruebe otros valores iniciales.");
            }
        }

        var finalJacobian = new double[x.Length][];
        for (var row = 0; row < x.Length; row++)
        {
            var value = function(x[row], beta);
            finalJacobian[row] = new double[k];
            for (var column = 0; column < k; column++)
            {
                var step = 1e-7 * Math.Max(1, Math.Abs(beta[column]));
                var shifted = (double[])beta.Clone();
                shifted[column] += step;
                finalJacobian[row][column] = (function(x[row], shifted) - value) / step;
            }
        }
        var xtx = new double[k][];
        for (var i = 0; i < k; i++)
        {
            xtx[i] = new double[k];
            for (var j = 0; j < k; j++)
            {
                for (var row = 0; row < x.Length; row++) xtx[i][j] += finalJacobian[row][i] * finalJacobian[row][j];
            }
        }
        var inverse = LinearAlgebra.Inverse(xtx);
        if (inverse.IsError) return Result.Err<NonlinearFit>(inverse.Error.Code, inverse.Error.Message);
        var sse = y.Select((value, row) => Math.Pow(value - function(x[row], beta), 2)).Sum();
        var sigma2 = sse / (x.Length - k);
        var covarianceFinal = inverse.Value.Select(row => row.Select(value => value * sigma2).ToArray()).ToArray();
        return Result.Ok(new NonlinearFit(beta, covarianceFinal));
    }

    private static (double[] Low, double[] High) CurveBand(
        Func<double, double[], double> function,
        double[] grid,
        double[] beta,
        double[][] covariance,
        double critical)
    {
        var low = new double[grid.Length];
        var high = new double[grid.Length];
        var k = beta.Length;
        for (var row = 0; row < grid.Length; row++)
        {
            var value = function(grid[row], beta);
            var jacobian = new double[k];
            for (var column = 0; column < k; column++)
            {
                var step = 1e-6 * Math.Max(1, Math.Abs(beta[column]));
                var shifted = (double[])beta.Clone();
                shifted[column] += step;
                jacobian[column] = (function(grid[row], shifted) - value) / step;
            }
            var variance = 0.0;
            for (var i = 0; i < k; i++)
            {
                for (var j = 0; j < k; j++) variance += jacobian[i] * covariance[i][j] * jacobian[j];
            }
            var half = critical * Math.Sqrt(Math.Max(0, variance));
            low[row] = value - half;
            high[row] = value + half;
        }
        return (low, high);
    }
}
