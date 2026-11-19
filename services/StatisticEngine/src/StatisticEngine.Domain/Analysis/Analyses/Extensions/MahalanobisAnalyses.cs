using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Distancia de Mahalanobis y atípicos multivariados (port de <c>multivariate._mahalanobis</c>).</summary>
internal static class MahalanobisAnalyses
{
    public static readonly AnalysisDefinition Mahalanobis = new(
        "mahalanobis",
        "Distancia de Mahalanobis y atípicos multivariados",
        "Mapas y atípicos",
        "Observaciones que son raras en conjunto aunque cada variable parezca normal; métodos clásico y robusto (MCD).",
        [],
        [],
        [
            new(
                "method",
                "Estimación",
                AnalysisParamKinds.Select,
                "robust",
                [new("classical", "Media y covarianza clásicas"), new("robust", "Robusta (determinante mínimo de covarianza)")],
                false),
            new("seed", "Semilla (robusta)", AnalysisParamKinds.Integer, 123.0, [], false, Minimum: 0, Maximum: 2147483647),
            ModelHelpers.Alpha()
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Mapas y atípicos",
        Roles:
        [
            new ColumnRole("vars", "Variables numéricas (2 o más)", ColumnKinds.Numeric, Multiple: true, MinCount: 2)
        ],
        Outputs: [new AnalysisOutputSpec("distancias", "Distancia de Mahalanobis", Required: false)]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        if (input.Pstr("method", "robust") != "classical")
        {
            return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "La estimación robusta (MCD) aún no está disponible; use la media y covarianza clásicas.");
        }
        var frame = input.AsFrame();
        var names = input.RoleNames("vars", true);
        var designResult = DesignBuilder.Build(frame, null, names, constant: false);
        if (designResult.IsError) return Result.Err<AnalysisResult>(designResult.Error.Code, designResult.Error.Message);
        var design = designResult.Value;
        var n = design.RowCount;
        var k = names.Count;
        if (n <= k + 1) return ExtendedHelpers.Invalid($"Se necesitan más de {k + 1} filas completas.");
        var x = design.Matrix;
        var means = Enumerable.Range(0, k).Select(column => x.Average(row => row[column])).ToArray();
        var covariance = new double[k][];
        for (var i = 0; i < k; i++)
        {
            covariance[i] = new double[k];
            for (var j = 0; j < k; j++)
            {
                covariance[i][j] = x.Sum(row => (row[i] - means[i]) * (row[j] - means[j])) / (n - 1.0);
            }
        }
        var inverse = LinearAlgebra.Inverse(covariance);
        if (inverse.IsError) return Result.Err<AnalysisResult>(inverse.Error.Code, inverse.Error.Message);
        var distances = new double[n];
        for (var row = 0; row < n; row++)
        {
            var sum = 0.0;
            for (var i = 0; i < k; i++)
            {
                for (var j = 0; j < k; j++) sum += (x[row][i] - means[i]) * inverse.Value[i][j] * (x[row][j] - means[j]);
            }
            distances[row] = Math.Max(0, sum);
        }
        var alpha = input.Pnum("alpha", 0.05);
        var cutoff = ExtendedStatistics.ChiSquareInv(1 - alpha, k);
        var pValues = distances.Select(value => ExtendedStatistics.ChiSquareSf(value, k)).ToArray();
        var flagged = Enumerable.Range(0, n).Where(index => distances[index] > cutoff).ToArray();
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("Atípicas", JsNumber.ToString(flagged.Length)),
                new TileItem("Corte (χ²)", ModelHelpers.N(cutoff)),
                new TileItem("Mayor distancia²", ModelHelpers.N(distances.Max()))
            ]),
            new AlertSection(
                "Conclusión",
                flagged.Length > 0 ? "warning" : "success",
                flagged.Length > 0
                    ? $"{flagged.Length} observación(es) con D² > {cutoff.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} (χ² con {k} gl, α = {SigmaText.General(alpha)})."
                    : "Ninguna observación supera el corte.")
        };
        var order = Enumerable.Range(0, n).OrderByDescending(index => distances[index]).Take(25).ToArray();
        var tableRows = order.Select(index =>
        {
            var values = new List<string>
            {
                JsNumber.ToString(index + 1),
                ModelHelpers.N(distances[index]),
                ModelHelpers.P(pValues[index]),
                distances[index] > cutoff ? "sí" : "no"
            };
            for (var column = 0; column < Math.Min(k, 6); column++) values.Add(ModelHelpers.N(x[index][column]));
            return (IReadOnlyList<string>)values;
        }).ToArray();
        sections.Add(new TableSection(
            "Observaciones más alejadas",
            ["Fila", "D²", "Valor p", "¿Atípica?", .. names.Take(6)],
            tableRows,
            Note: "Los números de fila empiezan en 1. Con la media y la covarianza clásicas los atípicos pueden «esconderse» a sí mismos (enmascaramiento): pruebe el método robusto."));

        var distancePoints = Enumerable.Range(0, n).Select(index => new ScatterPoint(index + 1, distances[index])).ToList();
        var sorted = distances.OrderBy(value => value).ToArray();
        var theoretical = Enumerable.Range(1, n).Select(index => ExtendedStatistics.ChiSquareInv((index - 0.5) / n, k)).ToArray();
        var qqPoints = theoretical.Select((value, index) => new ScatterPoint(value, sorted[index])).ToList();

        var predictions = ModelHelpers.Predictions(
            frame,
            design,
            ("mahalanobis_d2", distances),
            ("valor_p", pValues),
            ("atipica", flagged.Length > 0 ? distances.Select(value => value > cutoff ? 1.0 : 0.0).ToArray() : new double[n]));
        predictions = predictions with { Label = "Distancia de Mahalanobis" };

        return Result.Ok(new AnalysisResult(
            "mahalanobis",
            "Distancia de Mahalanobis y atípicos multivariados",
            sections,
            Subtitle: $"media y covarianza clásicas · {k} variables · n = {n}",
            Charts:
            [
                new ScatterChart("Distancia de Mahalanobis² por observación", distancePoints, "Fila", "D²"),
                new ScatterChart("D² frente a los cuantiles de χ² (si los datos son normales, sigue la recta)", qqPoints, "Cuantil teórico de χ²", "D² ordenada")
            ],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal) { ["distancias"] = predictions },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["flagged"] = flagged.Length,
                ["cutoff"] = cutoff,
                ["rows"] = flagged.Select(index => index + 1).ToArray()
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
