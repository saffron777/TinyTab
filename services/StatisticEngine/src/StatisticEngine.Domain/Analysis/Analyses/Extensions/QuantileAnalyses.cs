using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Regresión cuantílica (port de <c>models._quantile</c>).</summary>
internal static class QuantileAnalyses
{
    private static readonly AnalysisParamDefinition Quantiles = new(
        "quantiles",
        "Cuantiles",
        AnalysisParamKinds.Multiselect,
        new[] { "0.1", "0.5", "0.9" },
        new[] { 0.05, 0.1, 0.25, 0.5, 0.75, 0.9, 0.95 }
            .Select(q => new AnalysisParamOption($"{q:g}", $"{q:g}"))
            .ToArray(),
        false);

    public static readonly AnalysisDefinition Quantile = new(
        "regression-quantile",
        "Regresión cuantílica",
        "Regresión",
        "Cómo cambia el efecto de los predictores en distintos puntos de la distribución de Y (mediana, colas).",
        [],
        [],
        [Quantiles, ModelHelpers.Interactions(), ModelHelpers.Alpha()],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Lineales robustos y flexibles",
        Roles:
        [
            new ColumnRole("response", "Respuesta (Y)", ColumnKinds.Numeric),
            new ColumnRole("predictors", "Predictores (números y categorías)", ColumnKinds.Any, Multiple: true)
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var response = ExtendedHelpers.FirstRole(input, "response", "Respuesta (Y)");
        var predictors = input.RoleNames("predictors", true);
        var alpha = input.Pnum("alpha", 0.05);
        var interactions = input.Pstr("interactions", "none");
        var requestedQuantiles = input.Plist("quantiles");
        if (requestedQuantiles.Count == 0) requestedQuantiles = ["0.1", "0.5", "0.9"];
        var quantiles = requestedQuantiles
            .Select(value => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : double.NaN)
            .Where(value => double.IsFinite(value) && value > 0 && value < 1)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        if (quantiles.Length == 0) return ExtendedHelpers.Invalid("Elija al menos un cuantil.");

        var designResult = DesignBuilder.Build(frame, response, predictors, interactions: interactions);
        if (designResult.IsError) return Result.Err<AnalysisResult>(designResult.Error.Code, designResult.Error.Message);
        var design = designResult.Value;
        var olsResult = LinearModel.Fit(design);
        if (olsResult.IsError) return Result.Err<AnalysisResult>(olsResult.Error.Code, olsResult.Error.Message);
        var ols = olsResult.Value;
        var enough = ModelHelpers.NeedEnough(design, ols.Parameters + 1);
        if (enough.IsError) return Result.Err<AnalysisResult>(enough.Error.Code, enough.Error.Message);
        var n = design.RowCount;

        var fits = quantiles.ToDictionary(q => q, q => QuantileRegression.Fit(design.Matrix, design.Response, q));
        var quantileLabels = quantiles.Select(q => $"q = {SigmaText.General(q)}").ToArray();

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("Cuantiles", JsNumber.ToString(quantiles.Length)),
                new TileItem("R² de MCO", ModelHelpers.N(ols.R2))
            ])
        };
        ModelHelpers.DroppedNote(sections, design);

        var coefficientRows = new List<IReadOnlyList<string>>();
        var pRows = new List<IReadOnlyList<string>>();
        for (var index = 0; index < ols.Coefficients.Length; index++)
        {
            var name = design.ColumnNames[index] == "Intercept" ? "Constante" : design.Pretty(design.ColumnNames[index]);
            var coefficientRow = new string[1 + 1 + quantiles.Length];
            coefficientRow[0] = name;
            coefficientRow[1] = ModelHelpers.N(ols.Coefficients[index]);
            var pRow = new string[1 + quantiles.Length];
            pRow[0] = name;
            for (var q = 0; q < quantiles.Length; q++)
            {
                coefficientRow[q + 2] = ModelHelpers.N(fits[quantiles[q]].Coefficients[index]);
                pRow[q + 1] = ModelHelpers.P(fits[quantiles[q]].PValues[index]);
            }
            coefficientRows.Add(coefficientRow);
            pRows.Add(pRow);
        }
        sections.Add(new TableSection(
            "Coeficientes por cuantil",
            ["Término", "MCO", .. quantileLabels],
            coefficientRows));
        sections.Add(new TableSection(
            "Valores p por cuantil",
            ["Término", .. quantileLabels],
            pRows));
        sections.Add(new TableSection(
            "Pseudo-R² por cuantil",
            ["Cuantil", "Pseudo-R²"],
            quantiles.Select(q => (IReadOnlyList<string>)new[]
            {
                SigmaText.General(q),
                ModelHelpers.N(fits[q].PseudoR2)
            }).ToArray()));

        var chartPoints = quantiles
            .Select((q, index) => new ScatterPoint(q, fits[q].Coefficients[0]))
            .ToList();
        var chartTitle = design.NumericTokens.Count > 0
            ? $"Efecto de {design.Names[design.NumericTokens[0]]} según el cuantil"
            : "Coeficientes según el cuantil";

        var coefficients = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var q in quantiles)
        {
            var perTerm = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var index = 0; index < fits[q].Coefficients.Length; index++)
            {
                var name = design.ColumnNames[index] == "Intercept" ? "Intercept" : design.Pretty(design.ColumnNames[index]);
                perTerm[name] = fits[q].Coefficients[index];
            }
            coefficients[SigmaText.General(q)] = perTerm;
        }

        return Result.Ok(new AnalysisResult(
            "regression-quantile",
            $"Regresión cuantílica: {response}",
            sections,
            Subtitle: $"cuantiles {string.Join(", ", quantiles.Select(SigmaText.General))} · n = {n}",
            Charts:
            [
                new ScatterChart(chartTitle, chartPoints, "Cuantil", "Coeficiente")
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["coefficients"] = coefficients
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
