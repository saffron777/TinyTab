using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Regresión penalizada (lasso/ridge/red elástica) con validación cruzada.</summary>
internal static class RegularizedAnalyses
{
    private static readonly (string Key, string Label)[] Penalties =
    [
        ("lasso", "Lasso (L1): elimina predictores"),
        ("ridge", "Ridge (L2): reduce sin eliminar"),
        ("elastic", "Red elástica (L1 + L2)")
    ];

    public static readonly AnalysisDefinition Regularized = new(
        "regression-regularized",
        "Ridge, lasso y red elástica",
        "Regresión",
        "Regresión penalizada con validación cruzada: coeficientes, trayectoria y error según el parámetro λ.",
        [],
        [],
        [
            new(
                "penalty",
                "Penalización",
                AnalysisParamKinds.Select,
                "lasso",
                Penalties.Select(item => new AnalysisParamOption(item.Key, item.Label)).ToArray(),
                false),
            new(
                "rule",
                "Elección de λ",
                AnalysisParamKinds.Select,
                "min",
                [new("min", "El de menor error de validación"), new("one_se", "Regla de 1 error estándar (modelo más simple)")],
                false),
            new("folds", "Pliegues de validación cruzada", AnalysisParamKinds.Integer, 5.0, [], false, Minimum: 3, Maximum: 20),
            new("seed", "Semilla", AnalysisParamKinds.Integer, 123.0, [], false, Minimum: 0, Maximum: 2147483647)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Modelos aditivos y reducción",
        Roles:
        [
            new ColumnRole("response", "Respuesta (Y)", ColumnKinds.Numeric),
            new ColumnRole("predictors", "Predictores (números y categorías)", ColumnKinds.Any, Multiple: true)
        ],
        Outputs: [new AnalysisOutputSpec(ModelHelpers.PredictionsKey, ModelHelpers.PredictionsLabel)]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var response = ExtendedHelpers.FirstRole(input, "response", "Respuesta (Y)");
        var predictors = input.RoleNames("predictors", true);
        var penalty = input.Pstr("penalty", "lasso");
        var rule = input.Pstr("rule", "min");
        var folds = Math.Max(3, input.Pint("folds", 5));
        var seed = input.Pint("seed", 123);
        var designResult = DesignBuilder.Build(frame, response, predictors);
        if (designResult.IsError) return Result.Err<AnalysisResult>(designResult.Error.Code, designResult.Error.Message);
        var design = designResult.Value;
        var p = design.Matrix[0].Length;
        if (p < 2) return ExtendedHelpers.Invalid("Elija al menos un predictor.");
        var n = design.RowCount;
        if (n < 2 * folds) return ExtendedHelpers.Invalid("Se necesitan al menos 2 filas por pliegue.");

        var x = design.Matrix.Select(row => row.Skip(1).ToArray()).ToArray();
        var y = design.Response;
        var means = Enumerable.Range(0, p - 1).Select(column => x.Average(row => row[column])).ToArray();
        var sds = Enumerable.Range(0, p - 1).Select(column =>
        {
            var mean = means[column];
            var variance = x.Sum(row => Math.Pow(row[column] - mean, 2)) / (n - 1.0);
            return variance > 0 ? Math.Sqrt(variance) : 1.0;
        }).ToArray();
        var yMean = y.Average();
        var standardized = new double[n][];
        for (var row = 0; row < n; row++)
        {
            standardized[row] = new double[p - 1];
            for (var column = 0; column < p - 1; column++) standardized[row][column] = (x[row][column] - means[column]) / sds[column];
        }
        var centered = y.Select(value => value - yMean).ToArray();
        var lambdaMax = Enumerable.Range(0, p - 1)
            .Select(column => Math.Abs(Enumerable.Range(0, n).Sum(index => standardized[index][column] * centered[index])) / n)
            .Max();
        lambdaMax = Math.Max(lambdaMax, 1e-8);
        var lambdas = Enumerable.Range(0, 40)
            .Select(index => lambdaMax * Math.Pow(10, -3 + 3.0 * index / 39))
            .ToArray();
        var l1Ratio = penalty switch { "ridge" => 0.0, "elastic" => 0.5, _ => 1.0 };
        var order = Enumerable.Range(0, n).ToArray();
        var random = new System.Random(seed);
        for (var index = n - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (order[index], order[swap]) = (order[swap], order[index]);
        }
        var foldOf = new int[n];
        for (var index = 0; index < n; index++) foldOf[order[index]] = index % folds;

        var errors = new double[lambdas.Length][];
        for (var lambdaIndex = 0; lambdaIndex < lambdas.Length; lambdaIndex++) errors[lambdaIndex] = new double[folds];
        for (var fold = 0; fold < folds; fold++)
        {
            var train = Enumerable.Range(0, n).Where(row => foldOf[row] != fold).ToArray();
            var test = Enumerable.Range(0, n).Where(row => foldOf[row] == fold).ToArray();
            for (var lambdaIndex = 0; lambdaIndex < lambdas.Length; lambdaIndex++)
            {
                var beta = Solve(standardized, centered, train, lambdas[lambdaIndex], l1Ratio);
                var sse = test.Sum(row =>
                {
                    var prediction = beta.Select((value, column) => value * standardized[row][column]).Sum();
                    return Math.Pow(centered[row] - prediction, 2);
                });
                errors[lambdaIndex][fold] = sse / test.Length;
            }
        }
        var meansError = Enumerable.Range(0, lambdas.Length).Select(index => errors[index].Average()).ToArray();
        var best = Array.IndexOf(meansError, meansError.Min());
        if (rule == "one_se")
        {
            var threshold = meansError[best] + StandardError(errors[best]);
            var candidates = Enumerable.Range(0, lambdas.Length).Where(index => meansError[index] <= threshold).ToArray();
            best = candidates.Max();
        }
        var coefficients = Solve(standardized, centered, Enumerable.Range(0, n).ToArray(), lambdas[best], l1Ratio);
        var original = coefficients.Select((value, column) => value / sds[column]).ToArray();
        var intercept = yMean - original.Select((value, column) => value * means[column]).Sum();
        var fitted = new double[n];
        for (var row = 0; row < n; row++)
        {
            fitted[row] = intercept;
            for (var column = 0; column < p - 1; column++) fitted[row] += original[column] * x[row][column];
        }
        var sseFinal = y.Select((value, index) => Math.Pow(value - fitted[index], 2)).Sum();
        var sst = y.Sum(value => Math.Pow(value - yMean, 2));
        var r2 = sst > 0 ? 1 - sseFinal / sst : double.NaN;
        var nonzero = original.Count(value => Math.Abs(value) > 1e-10);
        var ols = LinearModel.Fit(design);

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("λ elegido", ModelHelpers.N(lambdas[best])),
                new TileItem("R² (entrenamiento)", ModelHelpers.N(r2)),
                new TileItem("RMSE de validación cruzada", ModelHelpers.N(Math.Sqrt(meansError[best]))),
                new TileItem("Coeficientes en cero", JsNumber.ToString(p - 1 - nonzero))
            ])
        };
        ModelHelpers.DroppedNote(sections, design);
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "Constante", ModelHelpers.N(intercept), ModelHelpers.N(ols.IsOk ? ols.Value.Coefficients[0] : double.NaN) }
        };
        for (var column = 0; column < p - 1; column++)
        {
            rows.Add([
                design.Pretty(design.ColumnNames[column + 1]),
                ModelHelpers.N(original[column]),
                ModelHelpers.N(ols.IsOk ? ols.Value.Coefficients[column + 1] : double.NaN)
            ]);
        }
        sections.Add(new TableSection(
            "Coeficientes (en la escala original)",
            ["Término", "Regularizado", "MCO (sin penalizar)"],
            rows,
            Note: "Las columnas se estandarizaron para penalizar por igual; los coeficientes se devuelven a su escala original."));

        var cvPoints = lambdas.Select((lambda, index) => new ScatterPoint(lambda, Math.Sqrt(meansError[index]))).ToList();
        var path = Enumerable.Range(0, p - 1)
            .Select(column => new LineSeries(
                design.Pretty(design.ColumnNames[column + 1]),
                lambdas.Select((lambda, index) => Solve(standardized, centered, Enumerable.Range(0, n).ToArray(), lambda, l1Ratio)[column] / sds[column]).ToArray()))
            .ToList();

        return Result.Ok(new AnalysisResult(
            "regression-regularized",
            $"Regresión regularizada ({Penalties.First(item => item.Key == penalty).Label.Split(" (")[0]}): {response}",
            sections,
            Subtitle: $"validación cruzada de {folds} pliegues · n = {n} · {p - 1} términos",
            Charts:
            [
                new ScatterChart("Error de validación cruzada frente a λ", cvPoints, "λ", "RMSE"),
                new LineChart("Trayectoria de los coeficientes", path, "λ")
            ],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
            {
                [ModelHelpers.PredictionsKey] = ModelHelpers.Predictions(
                    frame,
                    design,
                    ("ajustado", fitted),
                    ("residuo", y.Select((value, index) => value - fitted[index]).ToArray()))
            },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["lambda_"] = lambdas[best],
                ["l1_ratio"] = l1Ratio,
                ["r2"] = r2,
                ["nonzero"] = nonzero,
                ["n"] = n,
                ["coefficients"] = design.ColumnNames.Skip(1)
                    .Select((column, index) => (column, value: original[index]))
                    .ToDictionary(
                        pair => design.Pretty(pair.column),
                        pair => (object?)pair.value,
                        StringComparer.Ordinal)
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double StandardError(double[] values)
    {
        var mean = values.Average();
        var variance = values.Sum(value => Math.Pow(value - mean, 2)) / Math.Max(1, values.Length - 1);
        return Math.Sqrt(variance / values.Length);
    }

    private static double[] Solve(double[][] x, double[] y, int[] rows, double lambda, double l1Ratio)
    {
        var p = x[0].Length;
        var beta = new double[p];
        if (l1Ratio == 0)
        {
            var xtx = new double[p][];
            var xty = new double[p];
            for (var i = 0; i < p; i++)
            {
                xtx[i] = new double[p];
                for (var j = 0; j < p; j++)
                {
                    for (var row = 0; row < rows.Length; row++) xtx[i][j] += x[rows[row]][i] * x[rows[row]][j];
                }
                for (var row = 0; row < rows.Length; row++) xty[i] += x[rows[row]][i] * y[rows[row]];
                xtx[i][i] += lambda * rows.Length;
            }
            var solved = LinearAlgebra.SolveLinear(xtx, xty);
            return solved.IsOk ? solved.Value : beta;
        }
        var l2 = lambda * (1 - l1Ratio);
        for (var iteration = 0; iteration < 500; iteration++)
        {
            var change = 0.0;
            for (var column = 0; column < p; column++)
            {
                var rho = 0.0;
                var norm = 0.0;
                for (var row = 0; row < rows.Length; row++)
                {
                    var index = rows[row];
                    var partial = y[index];
                    for (var other = 0; other < p; other++)
                    {
                        if (other != column) partial -= x[index][other] * beta[other];
                    }
                    rho += x[index][column] * partial;
                    norm += x[index][column] * x[index][column];
                }
                var updated = Soft(rho, lambda * l1Ratio) / (norm + l2);
                change = Math.Max(change, Math.Abs(updated - beta[column]));
                beta[column] = updated;
            }
            if (change < 1e-8) break;
        }
        return beta;
    }

    private static double Soft(double value, double threshold) =>
        value > threshold ? value - threshold : value < -threshold ? value + threshold : 0;
}
