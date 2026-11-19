using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Mínimos cuadrados ponderados (port de <c>models._wls</c>).</summary>
internal static class WlsAnalyses
{
    public static readonly AnalysisDefinition Wls = new(
        "regression-wls",
        "Mínimos cuadrados ponderados",
        "Regresión",
        "Para varianza no constante: pesos propios o estimados, comparado con MCO y con Breusch-Pagan antes y después.",
        [],
        [],
        [ModelHelpers.Interactions(), ModelHelpers.Alpha()],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Lineales robustos y flexibles",
        Roles:
        [
            new ColumnRole("response", "Respuesta (Y)", ColumnKinds.Numeric),
            new ColumnRole("predictors", "Predictores (números y categorías)", ColumnKinds.Any, Multiple: true),
            new ColumnRole("weights", "Pesos (opcional; vacío = estimarlos)", ColumnKinds.Numeric, Optional: true)
        ],
        Outputs: [new AnalysisOutputSpec(ModelHelpers.PredictionsKey, ModelHelpers.PredictionsLabel)]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var response = ExtendedHelpers.FirstRole(input, "response", "Respuesta (Y)");
        var predictors = input.RoleNames("predictors", true);
        var alpha = input.Pnum("alpha", 0.05);
        var interactions = input.Pstr("interactions", "none");
        var weightColumn = ExtendedHelpers.FirstRole(input, "weights", "Pesos");
        var extra = string.IsNullOrEmpty(weightColumn)
            ? null
            : new Dictionary<string, string>(StringComparer.Ordinal) { ["weights"] = weightColumn };

        var designResult = DesignBuilder.Build(frame, response, predictors, interactions: interactions, extra: extra);
        if (designResult.IsError) return Result.Err<AnalysisResult>(designResult.Error.Code, designResult.Error.Message);
        var design = designResult.Value;

        var olsResult = LinearModel.Fit(design);
        if (olsResult.IsError) return Result.Err<AnalysisResult>(olsResult.Error.Code, olsResult.Error.Message);
        var ols = olsResult.Value;
        var enough = ModelHelpers.NeedEnough(design, ols.Parameters + 1);
        if (enough.IsError) return Result.Err<AnalysisResult>(enough.Error.Code, enough.Error.Message);

        double[] weights;
        string origin;
        if (!string.IsNullOrEmpty(weightColumn))
        {
            var token = design.Names.First(pair => pair.Value == weightColumn).Key;
            weights = design.Data.Numeric(token);
            if (weights.Any(value => value <= 0))
            {
                return ExtendedHelpers.Invalid("Los pesos deben ser estrictamente positivos.");
            }
            origin = $"pesos de la columna «{weightColumn}»";
        }
        else
        {
            var scaleMatrix = new double[ols.N][];
            for (var row = 0; row < ols.N; row++) scaleMatrix[row] = [1, ols.Fitted[row]];
            var scaleResult = LinearModel.Fit(scaleMatrix, ols.Residuals.Select(Math.Abs).ToArray());
            if (scaleResult.IsError) return Result.Err<AnalysisResult>(scaleResult.Error.Code, scaleResult.Error.Message);
            var spread = scaleResult.Value.Fitted;
            var floor = Math.Max(spread.Max() * 0.01, 1e-9);
            weights = spread.Select(value => 1 / Math.Pow(Math.Max(value, floor), 2)).ToArray();
            origin = "pesos estimados: 1 / (desviación predicha por |residuo| ~ ajustado)²";
        }

        var wlsResult = LinearModel.Fit(design, weights);
        if (wlsResult.IsError) return Result.Err<AnalysisResult>(wlsResult.Error.Code, wlsResult.Error.Message);
        var wls = wlsResult.Value;

        var bpBefore = LinearModel.BreuschPagan(ols.Residuals, design.Matrix);
        var weightedResiduals = wls.Residuals.Select((value, index) => value * Math.Sqrt(weights[index])).ToArray();
        var bpAfter = LinearModel.BreuschPagan(weightedResiduals, design.Matrix);
        var n = design.RowCount;

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("R² ponderado", ModelHelpers.N(wls.R2)),
                new TileItem("Breusch-Pagan antes", ModelHelpers.P(bpBefore.PValue)),
                new TileItem("Breusch-Pagan después", ModelHelpers.P(bpAfter.PValue))
            ])
        };
        ModelHelpers.DroppedNote(sections, design);

        var intervals = wls.ConfidenceIntervals(alpha);
        var rows = new List<IReadOnlyList<string>>();
        for (var index = 0; index < wls.Coefficients.Length; index++)
        {
            var name = design.ColumnNames[index] == "Intercept" ? "Constante" : design.Pretty(design.ColumnNames[index]);
            var olsIndex = Array.FindIndex(design.ColumnNames.ToArray(), column => column == design.ColumnNames[index]);
            rows.Add([
                name,
                ModelHelpers.N(ols.Coefficients[olsIndex]),
                ModelHelpers.N(ols.StandardErrors[olsIndex]),
                ModelHelpers.N(wls.Coefficients[index]),
                ModelHelpers.N(wls.StandardErrors[index]),
                ModelHelpers.N(wls.TValues[index]),
                ModelHelpers.P(wls.PValues[index]),
                ModelHelpers.N(intervals[index].Low),
                ModelHelpers.N(intervals[index].High)
            ]);
        }
        sections.Add(new TableSection(
            "MCO frente a mínimos cuadrados ponderados",
            ["Término", "Coef. MCO", "EE MCO", "Coef. MCP", "EE MCP", "t", "Valor p", .. ModelHelpers.CiColumns(alpha)],
            rows));

        var points = new List<ScatterPoint>(n);
        for (var row = 0; row < n; row++) points.Add(new ScatterPoint(wls.Fitted[row], wls.Residuals[row] * Math.Sqrt(weights[row])));

        var datasets = new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
        {
            [ModelHelpers.PredictionsKey] = ModelHelpers.Predictions(
                frame,
                design,
                ("ajustado", wls.Fitted),
                ("residuo", wls.Residuals),
                ("peso", weights))
        };

        var keyResults = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["n"] = n,
            ["r2"] = wls.R2,
            ["bp_before"] = bpBefore.PValue,
            ["bp_after"] = bpAfter.PValue,
            ["coefficients"] = ModelHelpers.Coefficients(design, wls)
        };

        return Result.Ok(new AnalysisResult(
            "regression-wls",
            $"Mínimos cuadrados ponderados: {response}",
            sections,
            Subtitle: $"{origin} · n = {n}",
            Charts:
            [
                new ScatterChart(
                    "Residuos estandarizados por el peso",
                    points,
                    "Valor ajustado",
                    "Residuo × √peso")
            ],
            Datasets: datasets,
            KeyResults: keyResults,
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
