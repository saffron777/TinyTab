using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Regresión logística multinomial por máxima verosimilitud.</summary>
internal static class MultinomialAnalyses
{
    public static readonly AnalysisDefinition Multinomial = new(
        "regression-multinomial",
        "Regresión logística multinomial",
        "Respuestas categóricas",
        "Modela una respuesta con tres o más categorías frente a una categoría de referencia, con coeficientes, razones de riesgo relativo y matriz de clasificación.",
        [],
        [],
        [
            new("reference", "Categoría de referencia (vacío = la primera)", AnalysisParamKinds.Text, "", [], true),
            new("interactions", "Interacciones", AnalysisParamKinds.Select, "none", [new("none", "Ninguna"), new("two", "Entre pares de predictores"), new("full", "Todas")], false),
            new("alpha", "Nivel de significancia α", AnalysisParamKinds.Number, 0.05, [], false, Minimum: 0.001, Maximum: 0.5)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Respuestas categóricas",
        Roles:
        [
            new ColumnRole("response", "Respuesta nominal (3 a 12 categorías)", ColumnKinds.Any),
            new ColumnRole("predictors", "Predictores (números y categorías)", ColumnKinds.Any, Multiple: true)
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        if (input.Pstr("interactions", "none") is not ("none" or "two" or "full")) return ExtendedHelpers.Invalid("Valor no válido para «Interacciones».");
        var response = ExtendedHelpers.FirstRole(input, "response", "Respuesta nominal (3 a 12 categorías)");
        var predictors = input.RoleNames("predictors", true);
        var design = DesignBuilder.Build(frame, null, predictors);
        if (design.IsError) return Result.Err<AnalysisResult>(design.Error.Code, design.Error.Message);
        var usedRows = design.Value.UsedRows.Where(row => !string.IsNullOrEmpty(frame.Texts(response)[row])).ToArray();
        var y = usedRows.Select(row => frame.Texts(response)[row] ?? string.Empty).ToArray();
        var levels = y.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (levels.Length < 3) return ExtendedHelpers.Invalid("La respuesta debe tener al menos tres categorías.");
        var matrixRows = design.Value.UsedRows.Select((row, index) => (row, index)).Where(pair => !string.IsNullOrEmpty(frame.Texts(response)[pair.row])).Select(pair => pair.index).ToArray();
        var n = usedRows.Length;
        var x = matrixRows.Select(index => design.Value.Matrix[index]).ToArray();
        var k = x[0].Length;
        var coded = y.Select(value => Array.IndexOf(levels, value)).ToArray();
        var parameters = new double[k * (levels.Length - 1)];
        double Objective(double[] values)
        {
            var total = 0.0;
            for (var row = 0; row < n; row++)
            {
                var scores = new double[levels.Length];
                for (var category = 1; category < levels.Length; category++)
                {
                    for (var column = 0; column < k; column++) scores[category] += values[(category - 1) * k + column] * x[row][column];
                }
                var max = scores.Max();
                var sum = scores.Sum(score => Math.Exp(score - max));
                total += scores[coded[row]] - max - Math.Log(sum);
            }
            return -total;
        }
        var current = Objective(parameters);
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var gradient = new double[parameters.Length];
            for (var index = 0; index < parameters.Length; index++)
            {
                var step = 1e-5;
                var high = (double[])parameters.Clone();
                var low = (double[])parameters.Clone();
                high[index] += step;
                low[index] -= step;
                gradient[index] = (Objective(high) - Objective(low)) / (2 * step);
            }
            var hessian = new double[parameters.Length, parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                for (var j = 0; j < parameters.Length; j++)
                {
                    var step = 1e-4;
                    var hh = (double[])parameters.Clone();
                    var hl = (double[])parameters.Clone();
                    var lh = (double[])parameters.Clone();
                    var ll = (double[])parameters.Clone();
                    hh[i] += step; hh[j] += step;
                    hl[i] += step; hl[j] -= step;
                    lh[i] -= step; lh[j] += step;
                    ll[i] -= step; ll[j] -= step;
                    hessian[i, j] = (Objective(hh) - Objective(hl) - Objective(lh) + Objective(ll)) / (4 * step * step);
                }
            }
            var matrix = new double[parameters.Length][];
            for (var i = 0; i < parameters.Length; i++)
            {
                matrix[i] = new double[parameters.Length];
                for (var j = 0; j < parameters.Length; j++) matrix[i][j] = hessian[i, j];
                matrix[i][i] += 1e-8;
            }
            var inverse = LinearAlgebra.Inverse(matrix);
            if (inverse.IsError) break;
            var delta = new double[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                for (var j = 0; j < parameters.Length; j++) delta[i] += inverse.Value[i][j] * gradient[j];
            }
            var next = parameters.Select((value, index) => value - delta[index]).ToArray();
            if (Objective(next) > current) break;
            var change = next.Select((value, index) => Math.Abs(value - parameters[index])).Max();
            parameters = next;
            current = Objective(next);
            if (change < 1e-10) break;
        }
        var covariance = new double[parameters.Length][];
        {
            var information = new double[parameters.Length][];
            for (var i = 0; i < parameters.Length; i++) information[i] = new double[parameters.Length];
            for (var row = 0; row < n; row++)
            {
                var scores = new double[levels.Length];
                for (var category = 1; category < levels.Length; category++)
                {
                    for (var column = 0; column < k; column++) scores[category] += parameters[(category - 1) * k + column] * x[row][column];
                }
                var max = scores.Max();
                var exponential = scores.Select(score => Math.Exp(score - max)).ToArray();
                var sum = exponential.Sum();
                var probabilities = exponential.Select(value => value / sum).ToArray();
                for (var first = 1; first < levels.Length; first++)
                {
                    for (var second = 1; second < levels.Length; second++)
                    {
                        var factor = probabilities[first] * ((first == second ? 1 : 0) - probabilities[second]);
                        for (var column = 0; column < k; column++)
                        {
                            for (var other = 0; other < k; other++)
                            {
                                information[(first - 1) * k + column][(second - 1) * k + other] += factor * x[row][column] * x[row][other];
                            }
                        }
                    }
                }
            }
            var inverse = LinearAlgebra.Inverse(information);
            covariance = inverse.IsOk ? inverse.Value : information.Select(_ => new double[parameters.Length]).ToArray();
        }
        var logLikelihood = -current;
        var nullLogLikelihood = Enumerable.Range(0, levels.Length).Sum(category => coded.Count(value => value == category) * Math.Log((coded.Count(value => value == category) + 1e-300) / n));
        var llr = 2 * (logLikelihood - nullLogLikelihood);
        var df = (levels.Length - 1) * predictors.Count;
        var pValue = ExtendedStatistics.ChiSquareSf(llr, df);
        var rows = new List<IReadOnlyList<string>>();
        for (var category = 1; category < levels.Length; category++)
        {
            for (var column = 0; column < k; column++)
            {
                var index = (category - 1) * k + column;
                var se = Math.Sqrt(Math.Max(covariance[index][index], 0));
                var z = se > 0 ? parameters[index] / se : 0;
                var p = 2 * (1 - SpecialFunctions.NormalCdf(Math.Abs(z)));
                rows.Add([
                    levels[category],
                    column == 0 ? "Constante" : design.Value.Pretty(design.Value.ColumnNames[column]),
                    ModelHelpers.N(parameters[index]), ModelHelpers.N(se), ModelHelpers.N(z), ModelHelpers.P(p),
                    ModelHelpers.N(parameters[index] - 1.959963984540054 * se),
                    ModelHelpers.N(parameters[index] + 1.959963984540054 * se),
                    ModelHelpers.N(Math.Exp(parameters[index])),
                    ModelHelpers.N(Math.Exp(parameters[index] - 1.959963984540054 * se)),
                    ModelHelpers.N(Math.Exp(parameters[index] + 1.959963984540054 * se))
                ]);
            }
        }
        var predictions = Enumerable.Range(0, n).Select(row =>
        {
            var scores = new double[levels.Length];
            for (var category = 1; category < levels.Length; category++)
            {
                for (var column = 0; column < k; column++) scores[category] += parameters[(category - 1) * k + column] * x[row][column];
            }
            return Array.IndexOf(scores, scores.Max());
        }).ToArray();
        var predictedProbabilities = Enumerable.Range(0, n).Select(row =>
        {
            var scores = new double[levels.Length];
            for (var category = 1; category < levels.Length; category++)
            {
                for (var column = 0; column < k; column++) scores[category] += parameters[(category - 1) * k + column] * x[row][column];
            }
            var max = scores.Max();
            var exponential = scores.Select(score => Math.Exp(score - max)).ToArray();
            var sum = exponential.Sum();
            return exponential.Select(value => value / sum).ToArray();
        }).ToArray();
        (string Name, double[] Values)[] datasetColumns;
        try
        {
            datasetColumns = Enumerable.Range(0, levels.Length)
                .Select(category => (Name: "prob_" + levels[category], Values: predictedProbabilities.Select(row => row[category]).ToArray()))
                .ToArray();
        }
        catch (Exception exception)
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mnerr.txt"), exception + "\n");
            return ExtendedHelpers.Invalid(exception.ToString());
        }
        DatasetResult predictionData;
        try
        {
            predictionData = ModelHelpers.Predictions(
                frame,
                design.Value,
                [
                    ("categoria_predicha_codigo", predictions.Select(value => (double)value).ToArray()),
                    .. datasetColumns.Select(column => (column.Name, column.Values))
                ]);
        }
        catch (Exception exception)
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mnerr2.txt"), exception + "\n");
            return ExtendedHelpers.Invalid(exception.ToString());
        }
        predictionData = predictionData with { Label = "Predicciones y residuos" };
        var accuracy = predictions.Select((value, index) => value == coded[index]).Count(value => value) / (double)n;
        var confusion = new List<IReadOnlyList<string>>();
        for (var actual = 0; actual < levels.Length; actual++)
        {
            var values = new List<string> { levels[actual] };
            values.AddRange(Enumerable.Range(0, levels.Length).Select(predicted => JsNumber.ToString(coded.Select((value, index) => value == actual && predictions[index] == predicted).Count(value => value))));
            confusion.Add(values);
        }
        return Result.Ok(new AnalysisResult(
            "regression-multinomial",
            $"Regresión logística multinomial: {response}",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Observaciones", JsNumber.ToString(n)),
                    new TileItem("Categorías", JsNumber.ToString(levels.Length)),
                    new TileItem("χ² de razón de verosimilitud", ModelHelpers.N(llr)),
                    new TileItem("McFadden R²", ModelHelpers.N(1 - logLikelihood / nullLogLikelihood))
                ]),
                new TableSection("Coeficientes frente a «" + levels[0] + "»", ["Categoría", "Término", "Coeficiente", "Error est.", "z", "Valor p", "IC 95 % inf.", "IC sup.", "Razón de riesgos relativos", "RRR inf.", "RRR sup."], rows,
                    Note: "Razón de riesgos relativos = exp(coeficiente): cambio en las probabilidades relativas de la categoría frente a la de referencia."),
                new TableSection("Ajuste del modelo", ["Medida", "Valor", "gl", "Valor p"], [
                    new[] { "Razón de verosimilitud", ModelHelpers.N(llr), JsNumber.ToString(df), ModelHelpers.P(pValue) },
                    new[] { "R² de McFadden", ModelHelpers.N(1 - logLikelihood / nullLogLikelihood), "", "" },
                    new[] { "Log-verosimilitud", ModelHelpers.N(logLikelihood), "", "" },
                    new[] { "AIC", ModelHelpers.N(-2 * logLikelihood + 2 * parameters.Length), "", "" },
                    new[] { "BIC", ModelHelpers.N(-2 * logLikelihood + Math.Log(n) * parameters.Length), "", "" }
                ]),
                new TableSection($"Real (filas) frente a predicho (columnas) · exactitud {100 * accuracy:0.0}%", ["", .. levels], confusion)
            ],
            Subtitle: $"referencia = {levels[0]} · {levels.Length} categorías · n = {n}",
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal) { ["predicciones"] = predictionData },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["accuracy"] = accuracy,
                ["llr_p"] = pValue
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
