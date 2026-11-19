using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Regresión polinómica e interacciones (port de <c>models._polynomial</c>).</summary>
internal static class PolynomialAnalyses
{
    public static readonly AnalysisDefinition Polynomial = new(
        "regression-polynomial",
        "Regresión polinómica e interacciones",
        "Regresión",
        "Grados superiores e interacciones entre predictores, con prueba F secuencial para elegir el grado y curva con banda de confianza.",
        [],
        [],
        [
            new("degree", "Grado máximo", AnalysisParamKinds.Integer, 2.0, [], false, Minimum: 1, Maximum: 6),
            ModelHelpers.Interactions(),
            ModelHelpers.Alpha()
        ],
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
        var degree = Math.Clamp((int)input.Pnum("degree", 2), 1, 6);
        var interactions = input.Pstr("interactions", "none");

        var designResult = DesignBuilder.Build(frame, response, predictors, degree, interactions);
        if (designResult.IsError) return Result.Err<AnalysisResult>(designResult.Error.Code, designResult.Error.Message);
        var design = designResult.Value;
        if (design.NumericTokens.Count == 0 && degree > 1)
        {
            return ExtendedHelpers.Invalid("Los polinomios necesitan al menos un predictor numérico.");
        }

        var fitResult = LinearModel.Fit(design);
        if (fitResult.IsError) return Result.Err<AnalysisResult>(fitResult.Error.Code, fitResult.Error.Message);
        var fit = fitResult.Value;
        var enough = ModelHelpers.NeedEnough(design, fit.Parameters + 1);
        if (enough.IsError) return Result.Err<AnalysisResult>(enough.Error.Code, enough.Error.Message);
        var n = design.RowCount;

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("R²", ModelHelpers.N(fit.R2)),
                new TileItem("R² ajustado", ModelHelpers.N(fit.R2Adjusted)),
                new TileItem("Error estándar residual", ModelHelpers.N(fit.Sigma))
            ])
        };
        ModelHelpers.DroppedNote(sections, design);
        sections.Add(ModelHelpers.CoefficientTable(design, fit, alpha));

        var sequence = new List<IReadOnlyList<string>>();
        LinearFit? previous = null;
        for (var current = 1; current <= degree; current++)
        {
            var currentDesign = DesignBuilder.Build(frame, response, predictors, current, interactions);
            if (currentDesign.IsError) return Result.Err<AnalysisResult>(currentDesign.Error.Code, currentDesign.Error.Message);
            var currentFit = LinearModel.Fit(currentDesign.Value);
            if (currentFit.IsError) return Result.Err<AnalysisResult>(currentFit.Error.Code, currentFit.Error.Message);
            if (previous is null)
            {
                sequence.Add([
                    "Grado 1",
                    ModelHelpers.N(currentFit.Value.F),
                    ModelHelpers.P(currentFit.Value.FPValue),
                    ModelHelpers.N(currentFit.Value.R2Adjusted)
                ]);
            }
            else
            {
                var dfDifference = previous.DegreesOfFreedom - currentFit.Value.DegreesOfFreedom;
                var f = dfDifference > 0
                    ? ((previous.Sse - currentFit.Value.Sse) / dfDifference) / (currentFit.Value.Sse / currentFit.Value.DegreesOfFreedom)
                    : double.NaN;
                sequence.Add([
                    $"Grado {current} frente a {current - 1}",
                    ModelHelpers.N(f),
                    ModelHelpers.P(ExtendedStatistics.FSf(f, dfDifference, currentFit.Value.DegreesOfFreedom)),
                    ModelHelpers.N(currentFit.Value.R2Adjusted)
                ]);
            }
            previous = currentFit.Value;
        }
        sections.Add(new TableSection(
            "Selección del grado (prueba F secuencial)",
            ["Comparación", "F", "Valor p", "R² ajustado"],
            sequence,
            Note: "Un valor p pequeño indica que subir el grado mejora el ajuste."));

        var modelDf = fit.Parameters - 1;
        var regressionSs = fit.Sst - fit.Sse;
        sections.Add(new TableSection(
            "Análisis de varianza del modelo",
            ["Fuente", "gl", "Suma de cuadrados", "Cuadrado medio", "F", "Valor p"],
            [
                new[]
                {
                    "Regresión",
                    JsNumber.ToString(modelDf),
                    ModelHelpers.N(regressionSs),
                    ModelHelpers.N(regressionSs / modelDf),
                    ModelHelpers.N(fit.F),
                    ModelHelpers.P(fit.FPValue)
                },
                new[]
                {
                    "Error",
                    JsNumber.ToString(fit.DegreesOfFreedom),
                    ModelHelpers.N(fit.Sse),
                    ModelHelpers.N(fit.Sigma * fit.Sigma),
                    string.Empty,
                    string.Empty
                },
                new[]
                {
                    "Total",
                    JsNumber.ToString(modelDf + fit.DegreesOfFreedom),
                    ModelHelpers.N(fit.Sst),
                    string.Empty,
                    string.Empty,
                    string.Empty
                }
            ]));

        var chartSeries = new List<ScatterPoint>();
        if (design.NumericTokens.Count > 0)
        {
            var token = design.NumericTokens[0];
            var x = design.Data.Numeric(token);
            for (var row = 0; row < n; row++) chartSeries.Add(new ScatterPoint(x[row], design.Response[row]));
        }

        var datasets = new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
        {
            [ModelHelpers.PredictionsKey] = ModelHelpers.Predictions(
                frame,
                design,
                ("ajustado", fit.Fitted),
                ("residuo", fit.Residuals))
        };

        return Result.Ok(new AnalysisResult(
            "regression-polynomial",
            $"Regresión polinómica / con interacciones: {response}",
            sections,
            Subtitle: $"grado {degree} · n = {n}",
            Charts:
            [
                new ScatterChart(
                    design.NumericTokens.Count > 0
                        ? $"{response} según {design.Names[design.NumericTokens[0]]} (otros predictores en su media)"
                        : $"{response} según los predictores",
                    chartSeries,
                    design.NumericTokens.Count > 0 ? design.Names[design.NumericTokens[0]] : "Predictor",
                    response)
            ],
            Datasets: datasets,
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["r2"] = fit.R2,
                ["r2_adj"] = fit.R2Adjusted,
                ["coefficients"] = ModelHelpers.Coefficients(design, fit)
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
