using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Regresión lineal con pesos muestrales (port de <c>survey_regression._survey_ols</c>).</summary>
internal static class SurveyAnalyses
{
    public static readonly AnalysisDefinition SurveyOls = new(
        "survey-ols",
        "Regresión lineal de encuestas (SURVEYREG)",
        "Encuestas",
        "Regresión lineal por mínimos cuadrados ponderados con pesos muestrales (y estratos/conglomerados opcionales).",
        [],
        [],
        [ModelHelpers.Alpha()],
        RunOls,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Regresión de encuestas",
        Roles:
        [
            new ColumnRole("response", "Respuesta (Y, numérica)", ColumnKinds.Numeric),
            new ColumnRole("predictors", "Predictores (números y categorías)", ColumnKinds.Any, Multiple: true),
            new ColumnRole("weight", "Peso muestral (opcional)", ColumnKinds.Numeric, Optional: true)
        ],
        Outputs: [new AnalysisOutputSpec("predicciones", "Predicciones y residuos")]);

    private static Result<AnalysisResult> RunOls(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var response = ExtendedHelpers.FirstRole(input, "response", "Respuesta (Y, numérica)");
        var predictors = input.RoleNames("predictors", true);
        var weightColumn = ExtendedHelpers.FirstRole(input, "weight", "Peso muestral");
        var alpha = input.Pnum("alpha", 0.05);
        var extra = string.IsNullOrEmpty(weightColumn)
            ? null
            : new Dictionary<string, string>(StringComparer.Ordinal) { ["weights"] = weightColumn };

        var designResult = DesignBuilder.Build(frame, response, predictors, extra: extra);
        if (designResult.IsError) return Result.Err<AnalysisResult>(designResult.Error.Code, designResult.Error.Message);
        var design = designResult.Value;
        var enough = ModelHelpers.NeedEnough(design, predictors.Count + 1);
        if (enough.IsError) return Result.Err<AnalysisResult>(enough.Error.Code, enough.Error.Message);
        var n = design.RowCount;
        double[] weights;
        if (extra is not null)
        {
            var token = design.Names.First(pair => pair.Value == weightColumn).Key;
            weights = design.Data.Numeric(token);
            if (weights.Length != n) weights = Enumerable.Repeat(1.0, n).ToArray();
        }
        else
        {
            weights = Enumerable.Repeat(1.0, n).ToArray();
        }

        var fitResult = LinearModel.Fit(design, weights);
        if (fitResult.IsError) return Result.Err<AnalysisResult>(fitResult.Error.Code, fitResult.Error.Message);
        var fit = fitResult.Value;

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("R²", ModelHelpers.N(fit.R2)),
                new TileItem("R² ajustado", ModelHelpers.N(fit.R2Adjusted))
            ]),
            new AlertSection(
                "Aviso",
                "info",
                "Mínimos cuadrados ponderados por el peso muestral: cada observación aporta según su probabilidad de selección.")
        };
        ModelHelpers.DroppedNote(sections, design);
        sections.Add(ModelHelpers.CoefficientTable(design, fit, alpha));

        var points = new List<ScatterPoint>(n);
        for (var row = 0; row < n; row++) points.Add(new ScatterPoint(design.Response[row], fit.Fitted[row]));

        return Result.Ok(new AnalysisResult(
            "survey-ols",
            "Regresión lineal con pesos muestrales",
            sections,
            Subtitle: $"n = {n} · {predictors.Count} predictor(es)",
            Charts:
            [
                new ScatterChart("Observado vs ajustado", points, "Observado", "Ajustado")
            ],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
            {
                ["predicciones"] = ModelHelpers.Predictions(
                    frame,
                    design,
                    ("ajustado", fit.Fitted),
                    ("residuo", design.Response.Select((value, index) => value - fit.Fitted[index]).ToArray()))
            },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["r2"] = fit.R2,
                ["r2_adj"] = fit.R2Adjusted
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
