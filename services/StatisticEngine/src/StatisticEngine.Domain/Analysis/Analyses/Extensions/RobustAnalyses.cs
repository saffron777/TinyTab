using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Regresión robusta y errores HC/HAC (port de <c>models._robust</c>).</summary>
internal static class RobustAnalyses
{
    private static readonly AnalysisParamDefinition Method = new(
        "method",
        "Método",
        AnalysisParamKinds.Select,
        "hc3",
        [
            new("hc3", "MCO con errores robustos HC3 (heterocedasticidad)"),
            new("hc1", "MCO con errores robustos HC1"),
            new("hc0", "MCO con errores robustos HC0 (White)"),
            new("hac", "MCO con errores HAC de Newey-West (autocorrelación y heterocedasticidad)"),
            new("huber", "Regresión robusta M de Huber"),
            new("tukey", "Regresión robusta M de Tukey (bicuadrada)"),
            new("hampel", "Regresión robusta M de Hampel")
        ],
        false);

    private static readonly AnalysisParamDefinition Lags = new(
        "lags",
        "Rezagos de Newey-West",
        AnalysisParamKinds.Integer,
        4.0,
        [],
        false,
        Minimum: 0,
        Maximum: 100,
        VisibleIf: new("method", ["hac"]));

    public static readonly AnalysisDefinition Robust = new(
        "regression-robust",
        "Regresión robusta y errores robustos",
        "Regresión",
        "Errores HC/HAC para heterocedasticidad y autocorrelación, o estimación M (Huber, Tukey, Hampel) contra atípicos.",
        [],
        [],
        [Method, Lags, ModelHelpers.Interactions(), ModelHelpers.Alpha()],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Lineales robustos y flexibles",
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
        var alpha = input.Pnum("alpha", 0.05);
        var method = input.Pstr("method", "hc3");
        var interactions = input.Pstr("interactions", "none");

        var designResult = DesignBuilder.Build(frame, response, predictors, interactions: interactions);
        if (designResult.IsError) return Result.Err<AnalysisResult>(designResult.Error.Code, designResult.Error.Message);
        var design = designResult.Value;
        var olsResult = LinearModel.Fit(design);
        if (olsResult.IsError) return Result.Err<AnalysisResult>(olsResult.Error.Code, olsResult.Error.Message);
        var ols = olsResult.Value;
        var enough = ModelHelpers.NeedEnough(design, ols.Parameters + 1);
        if (enough.IsError) return Result.Err<AnalysisResult>(enough.Error.Code, enough.Error.Message);
        var n = design.RowCount;

        if (method is not ("hc0" or "hc1" or "hc3" or "hac"))
        {
            return ExtendedHelpers.Error(
                DomainErrorCode.NotSupported,
                "La estimación M robusta (Huber/Tukey/Hampel) aún no está disponible en este motor; use HC0, HC1, HC3 o HAC.");
        }

        var robustResult = LinearModel.Fit(design, covariance: method, lags: input.Pint("lags", 4));
        if (robustResult.IsError) return Result.Err<AnalysisResult>(robustResult.Error.Code, robustResult.Error.Message);
        var robust = robustResult.Value;
        var bp = LinearModel.BreuschPagan(ols.Residuals, design.Matrix);
        var dw = LinearModel.DurbinWatson(ols.Residuals);
        var label = method switch
        {
            "hc0" => "MCO con errores robustos HC0 (White)",
            "hc1" => "MCO con errores robustos HC1",
            "hac" => "MCO con errores HAC de Newey-West (autocorrelación y heterocedasticidad)",
            _ => "MCO con errores robustos HC3 (heterocedasticidad)"
        };

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("R²", ModelHelpers.N(ols.R2)),
                new TileItem("Breusch-Pagan p", ModelHelpers.P(bp.PValue)),
                new TileItem("Durbin-Watson", ModelHelpers.N(dw))
            ])
        };
        ModelHelpers.DroppedNote(sections, design);

        var intervals = robust.ConfidenceIntervals(alpha);
        // statsmodels usa la normal para los intervalos con covarianza robusta (use_t = False).
        var critical = SpecialFunctions.NormalInv(1 - alpha / 2);
        for (var index = 0; index < intervals.Length; index++)
        {
            intervals[index] = (
                robust.Coefficients[index] - critical * robust.StandardErrors[index],
                robust.Coefficients[index] + critical * robust.StandardErrors[index]);
        }
        var rows = new List<IReadOnlyList<string>>();
        for (var index = 0; index < robust.Coefficients.Length; index++)
        {
            var name = design.ColumnNames[index] == "Intercept" ? "Constante" : design.Pretty(design.ColumnNames[index]);
            rows.Add([
                name,
                ModelHelpers.N(ols.Coefficients[index]),
                ModelHelpers.N(ols.StandardErrors[index]),
                ModelHelpers.N(robust.StandardErrors[index]),
                ModelHelpers.N(robust.StandardErrors[index] / ols.StandardErrors[index]),
                ModelHelpers.N(robust.TValues[index]),
                ModelHelpers.P(robust.PValues[index]),
                ModelHelpers.N(intervals[index].Low),
                ModelHelpers.N(intervals[index].High)
            ]);
        }
        sections.Add(new TableSection(
            "Coeficientes con errores robustos",
            ["Término", "Coeficiente", "Error est. clásico", "Error est. robusto", "Robusto / clásico", "t", "Valor p", .. ModelHelpers.CiColumns(alpha)],
            rows,
            Note: "Los coeficientes son los de MCO; solo cambian los errores estándar, valores p e intervalos."));
        sections.Add(new AlertSection(
            "Conclusión",
            "info",
            bp.PValue < alpha
                ? "Breusch-Pagan sugiere heterocedasticidad: los errores robustos son preferibles."
                : "No hay evidencia clara de heterocedasticidad; los errores robustos y los clásicos deberían ser parecidos."));

        var points = new List<ScatterPoint>(n);
        for (var row = 0; row < n; row++) points.Add(new ScatterPoint(ols.Fitted[row], ols.Residuals[row]));

        return Result.Ok(new AnalysisResult(
            "regression-robust",
            $"Regresión robusta: {response}",
            sections,
            Subtitle: $"{label} · n = {n}",
            Charts:
            [
                new ScatterChart("Residuos frente a valores ajustados", points, "Valor ajustado", "Residuo")
            ],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
            {
                [ModelHelpers.PredictionsKey] = ModelHelpers.Predictions(
                    frame,
                    design,
                    ("ajustado", ols.Fitted),
                    ("residuo", ols.Residuals))
            },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["coefficients"] = ModelHelpers.Coefficients(design, robust),
                ["standard_errors"] = design.ColumnNames
                    .Select((column, index) => (column, value: robust.StandardErrors[index]))
                    .ToDictionary(
                        pair => pair.column == "Intercept" ? "Intercept" : design.Pretty(pair.column),
                        pair => (object?)pair.value,
                        StringComparer.Ordinal)
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
