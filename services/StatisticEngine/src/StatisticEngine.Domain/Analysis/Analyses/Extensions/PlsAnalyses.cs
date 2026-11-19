using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Mínimos cuadrados parciales (port de <c>models_advanced._pls</c>).</summary>
internal static class PlsAnalyses
{
    public static readonly AnalysisDefinition Pls = new(
        "regression-pls",
        "Mínimos cuadrados parciales (PLS)",
        "Regresión",
        "Reduce predictores correlacionados a componentes latentes que explican la respuesta; útil con muchas columnas.",
        [],
        [],
        [
            new("components", "Número de componentes", AnalysisParamKinds.Integer, 2.0, [], false, Minimum: 1, Maximum: 20),
            new("scale", "Estandarizar las variables", AnalysisParamKinds.Boolean, true, [], false)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Modelos aditivos y reducción",
        Roles:
        [
            new ColumnRole("response", "Respuesta (Y)", ColumnKinds.Numeric),
            new ColumnRole("predictors", "Predictores numéricos", ColumnKinds.Numeric, Multiple: true, MinCount: 1, MaxCount: 30)
        ],
        Outputs: [new AnalysisOutputSpec("predicciones", "Predicciones y residuos")]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var response = ExtendedHelpers.FirstRole(input, "response", "Respuesta (Y)");
        var predictors = input.RoleNames("predictors", true);
        if (predictors.Count == 0) return ExtendedHelpers.Invalid("Elija al menos un predictor numérico.");
        var names = new List<string> { response };
        names.AddRange(predictors);
        var complete = frame.CompleteCases(names);
        if (complete.RowCount < 12)
        {
            return ExtendedHelpers.Invalid($"Se necesitan al menos 12 filas completas (hay {complete.RowCount}).");
        }
        var y = complete.Numeric(response);
        var x = new double[complete.RowCount][];
        for (var row = 0; row < complete.RowCount; row++)
        {
            x[row] = new double[predictors.Count];
            for (var column = 0; column < predictors.Count; column++) x[row][column] = complete.Numeric(predictors[column])[row];
        }
        var components = Math.Min(input.Pint("components", 2), predictors.Count);
        var fitResult = PlsRegression.Fit(x, y, components, input.Pbool("scale", true));
        if (fitResult.IsError) return Result.Err<AnalysisResult>(fitResult.Error.Code, fitResult.Error.Message);
        var fit = fitResult.Value;

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(complete.RowCount)),
                new TileItem("Componentes", JsNumber.ToString(fit.Components)),
                new TileItem("R²", ModelHelpers.N(fit.R2))
            ]),
            new AlertSection(
                "Aviso",
                "info",
                "PLS reduce predictores correlacionados a pocas componentes latentes que explican la respuesta: útil con más columnas que filas.")
        };

        var varianceRows = new List<IReadOnlyList<string>>();
        var cumulative = 0.0;
        for (var component = 0; component < fit.Components; component++)
        {
            cumulative += fit.YScoreVariances[component];
            varianceRows.Add([
                JsNumber.ToString(component + 1),
                ModelHelpers.N(fit.XScoreVariances[component]),
                ModelHelpers.N(fit.YScoreVariances[component]),
                ModelHelpers.N(cumulative)
            ]);
        }
        sections.Add(new TableSection(
            "Varianza explicada por componente",
            ["Componente", "Varianza de X explicada", "Varianza de Y explicada", "Acumulada (Y)"],
            varianceRows));

        var coefficientRows = new List<IReadOnlyList<string>>();
        for (var index = 0; index < predictors.Count; index++)
        {
            coefficientRows.Add([predictors[index], ModelHelpers.N(fit.Coefficients[index])]);
        }
        sections.Add(new TableSection("Coeficientes de regresión", ["Predictor", "Coeficiente"], coefficientRows));

        var points = new List<ScatterPoint>(y.Length);
        for (var row = 0; row < y.Length; row++) points.Add(new ScatterPoint(y[row], fit.Fitted[row]));

        var datasets = new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
        {
            ["predicciones"] = new DatasetResult(
                "Predicciones y residuos",
                [
                    DataColumn.Numeric("observado", y),
                    DataColumn.Numeric("ajustado", fit.Fitted),
                    DataColumn.Numeric("residuo", y.Select((value, index) => value - fit.Fitted[index]).ToArray())
                ])
        };

        return Result.Ok(new AnalysisResult(
            "regression-pls",
            "Mínimos cuadrados parciales (PLS)",
            sections,
            Subtitle: $"n = {complete.RowCount} · {predictors.Count} predictores · {fit.Components} componente(s)",
            Charts:
            [
                new ScatterChart("Observado vs ajustado", points, "Observado", "Ajustado")
            ],
            Datasets: datasets,
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = complete.RowCount,
                ["components"] = fit.Components,
                ["r2"] = fit.R2
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
