using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Regresión ordinal de probabilidades proporcionales (port de <c>models._ordinal</c>).</summary>
internal static class OrdinalAnalyses
{
    public static readonly AnalysisDefinition Ordinal = new(
        "regression-ordinal",
        "Regresión ordinal",
        "Regresión",
        "Respuesta con categorías ordenadas (satisfacción 1-5, grados): probabilidades proporcionales con logit o probit.",
        [],
        [],
        [
            new(
                "distr",
                "Distribución",
                AnalysisParamKinds.Select,
                "logit",
                [new("logit", "Logit"), new("probit", "Probit")],
                false),
            ModelHelpers.Interactions(),
            ModelHelpers.Alpha()
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Respuesta binaria",
        Roles:
        [
            new ColumnRole("response", "Respuesta ordinal (3 o más categorías)", ColumnKinds.Any),
            new ColumnRole("predictors", "Predictores (números y categorías)", ColumnKinds.Any, Multiple: true)
        ],
        Outputs: [new AnalysisOutputSpec(ModelHelpers.PredictionsKey, ModelHelpers.PredictionsLabel)]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var response = ExtendedHelpers.FirstRole(input, "response", "Respuesta ordinal");
        var predictors = input.RoleNames("predictors", true);
        var alpha = input.Pnum("alpha", 0.05);
        var distribution = input.Pstr("distr", "logit");
        var interactions = input.Pstr("interactions", "none");
        var levels = frame.Levels(response);
        if (levels.Length < 3)
        {
            return ExtendedHelpers.Invalid($"«{response}» debe tener al menos 3 categorías ordenadas (tiene {levels.Length}); con 2 use la regresión binaria.");
        }
        if (levels.Length > 15)
        {
            return ExtendedHelpers.Invalid($"«{response}» tiene {levels.Length} categorías; para una respuesta ordinal se admiten hasta 15.");
        }
        var labels = frame.Texts(response);
        var categories = labels.Select(label => Array.IndexOf(levels, label)).ToArray();
        var working = frame.With(DataColumn.Numeric(response, categories.Select(value => (double)value).ToArray()));
        var designResult = DesignBuilder.Build(working, response, predictors, interactions: interactions);
        if (designResult.IsError) return Result.Err<AnalysisResult>(designResult.Error.Code, designResult.Error.Message);
        var design = designResult.Value;
        var k = design.Matrix[0].Length - 1;
        if (k < 1) return ExtendedHelpers.Invalid("Elija al menos un predictor.");
        var n = design.RowCount;
        if (n < 3 * (k + levels.Length - 1))
        {
            return ExtendedHelpers.Invalid(
                $"Hay {n} filas completas para {k + levels.Length - 1} parámetros: se necesitan al menos 3 por parámetro.");
        }
        var x = design.Matrix.Select(row => row.Skip(1).ToArray()).ToArray();
        var codes = design.Response.Select(value => (int)Math.Round(value)).ToArray();
        var counts = new int[levels.Length];
        foreach (var code in codes) counts[code]++;

        var thresholds = new double[levels.Length - 1];
        var cumulative = 0.0;
        for (var index = 0; index < levels.Length - 1; index++)
        {
            cumulative += counts[index] / (double)n;
            var clipped = Math.Clamp(cumulative, 1e-12, 1 - 1e-12);
            thresholds[index] = distribution == "probit" ? SpecialFunctions.NormalInv(clipped) : Math.Log(clipped / (1 - clipped));
        }
        var fitResult = OrdinalRegression.Fit(x, codes, levels.Length, thresholds, distribution == "probit");
        if (fitResult.IsError) return Result.Err<AnalysisResult>(fitResult.Error.Code, fitResult.Error.Message);
        var fit = fitResult.Value;
        var lr = 2 * (fit.LogLikelihood - fit.NullLogLikelihood);
        var lrP = ExtendedStatistics.ChiSquareSf(lr, k);

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("Categorías", JsNumber.ToString(levels.Length)),
                new TileItem("χ² de razón de verosimilitud", ModelHelpers.N(lr)),
                new TileItem("Exactitud", ModelHelpers.N(Accuracy(fit.Probabilities, codes)))
            ])
        };
        ModelHelpers.DroppedNote(sections, design);

        var critical = SpecialFunctions.NormalInv(1 - alpha / 2);
        var exponentiate = distribution == "logit";
        var headers = new List<string> { "Término", "Coeficiente", "Error est.", "z", "Valor p" };
        headers.AddRange(ModelHelpers.CiColumns(alpha));
        if (exponentiate)
        {
            headers.Add("Odds ratio");
            headers.Add($"IC {SigmaText.General(100 * (1 - alpha))} % inf. (OR)");
            headers.Add("IC sup. (OR)");
        }
        var coefficientRows = new List<IReadOnlyList<string>>();
        for (var index = 0; index < k; index++)
        {
            var name = design.Pretty(design.ColumnNames[index + 1]);
            var low = fit.Coefficients[index] - critical * fit.StandardErrors[index];
            var high = fit.Coefficients[index] + critical * fit.StandardErrors[index];
            var row = new List<string>
            {
                name,
                ModelHelpers.N(fit.Coefficients[index]),
                ModelHelpers.N(fit.StandardErrors[index]),
                ModelHelpers.N(fit.ZValues[index]),
                ModelHelpers.P(fit.PValues[index]),
                ModelHelpers.N(low),
                ModelHelpers.N(high)
            };
            if (exponentiate)
            {
                row.Add(ModelHelpers.N(Math.Exp(fit.Coefficients[index])));
                row.Add(ModelHelpers.N(Math.Exp(low)));
                row.Add(ModelHelpers.N(Math.Exp(high)));
            }
            coefficientRows.Add(row);
        }
        sections.Add(new TableSection(
            "Coeficientes (modelo de probabilidades proporcionales)",
            headers,
            coefficientRows,
            Note: "Un coeficiente positivo desplaza la respuesta hacia las categorías más altas."));

        sections.Add(new TableSection(
            "Umbrales entre categorías",
            ["Entre", "Umbral"],
            Enumerable.Range(0, levels.Length - 1)
                .Select(index => (IReadOnlyList<string>)new[] { $"{levels[index]} | {levels[index + 1]}", ModelHelpers.N(fit.Thresholds[index]) })
                .ToArray()));

        sections.Add(new TableSection(
            "Ajuste del modelo",
            ["Medida", "Valor", "gl", "Valor p"],
            [
                new[] { "Razón de verosimilitud (frente a solo umbrales)", ModelHelpers.N(lr), JsNumber.ToString(k), ModelHelpers.P(lrP) },
                new[] { "R² de McFadden", ModelHelpers.N(1 - fit.LogLikelihood / fit.NullLogLikelihood), string.Empty, string.Empty },
                new[] { "Log-verosimilitud", ModelHelpers.N(fit.LogLikelihood), string.Empty, string.Empty },
                new[] { "AIC", ModelHelpers.N(-2 * fit.LogLikelihood + 2 * (k + levels.Length - 1)), string.Empty, string.Empty },
                new[] { "BIC", ModelHelpers.N(-2 * fit.LogLikelihood + (k + levels.Length - 1) * Math.Log(n)), string.Empty, string.Empty }
            ]));

        var predicted = fit.Probabilities.Select(row =>
        {
            var best = 0;
            for (var index = 1; index < row.Length; index++)
            {
                if (row[index] > row[best]) best = index;
            }
            return best;
        }).ToArray();
        var confusion = new int[levels.Length, levels.Length];
        for (var row = 0; row < codes.Length; row++) confusion[codes[row], predicted[row]]++;
        var confusionRows = new List<IReadOnlyList<string>>();
        for (var row = 0; row < levels.Length; row++)
        {
            var values = new List<string> { levels[row] };
            for (var column = 0; column < levels.Length; column++) values.Add(JsNumber.ToString(confusion[row, column]));
            confusionRows.Add(values);
        }
        sections.Add(new TableSection(
            $"Categoría real (filas) frente a predicha (columnas)",
            ["", .. levels],
            confusionRows));

        var predictionColumns = new List<(string, double[])>
        {
            ("categoria_predicha_codigo", predicted.Select(value => (double)value).ToArray())
        };
        for (var level = 0; level < levels.Length; level++)
        {
            predictionColumns.Add(($"prob_{levels[level]}", fit.Probabilities.Select(row => row[level]).ToArray()));
        }
        var predictions = ModelHelpers.Predictions(frame, design, [.. predictionColumns]);

        var coefficientKeys = design.ColumnNames.Skip(1)
            .Select((column, index) => (column, value: fit.Coefficients[index]))
            .ToDictionary(
                pair => design.Pretty(pair.column),
                pair => (object?)pair.value,
                StringComparer.Ordinal);

        return Result.Ok(new AnalysisResult(
            "regression-ordinal",
            $"Regresión ordinal ({(distribution == "probit" ? "probit" : "logit")}): {response}",
            sections,
            Subtitle: $"{levels.Length} categorías ordenadas · n = {n}",
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal) { [ModelHelpers.PredictionsKey] = predictions },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["accuracy"] = Accuracy(fit.Probabilities, codes),
                ["lr_p"] = lrP,
                ["coefficients"] = coefficientKeys
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double Accuracy(double[][] probabilities, int[] categories)
    {
        var correct = 0;
        for (var row = 0; row < categories.Length; row++)
        {
            var best = 0;
            for (var column = 1; column < probabilities[row].Length; column++)
            {
                if (probabilities[row][column] > probabilities[row][best]) best = column;
            }
            if (best == categories[row]) correct++;
        }
        return correct / (double)categories.Length;
    }
}
