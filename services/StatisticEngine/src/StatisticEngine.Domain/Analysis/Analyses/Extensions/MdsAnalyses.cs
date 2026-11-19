using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Escalamiento multidimensional clásico (Torgerson).</summary>
internal static class MdsAnalyses
{
    public static readonly AnalysisDefinition Mds = new(
        "mds",
        "Escalamiento multidimensional",
        "Mapas y atípicos",
        "Mapa de las observaciones que conserva sus distancias: método clásico (Torgerson) o no métrico (SMACOF) con estrés de Kruskal.",
        [],
        [],
        [
            new(
                "method",
                "Método",
                AnalysisParamKinds.Select,
                "classical",
                [new("classical", "Clásico (métrico)"), new("nonmetric", "No métrico (SMACOF)")],
                false),
            new(
                "metric",
                "Distancia",
                AnalysisParamKinds.Select,
                "euclidean",
                [
                    new("euclidean", "Euclídea"),
                    new("cityblock", "Manhattan"),
                    new("cosine", "Coseno"),
                    new("correlation", "Correlación")
                ],
                false),
            new("dimensions", "Dimensiones", AnalysisParamKinds.Integer, 2.0, [], false, Minimum: 2, Maximum: 5),
            new("standardize", "Estandarizar las variables", AnalysisParamKinds.Boolean, true, [], false),
            new("seed", "Semilla (método no métrico)", AnalysisParamKinds.Integer, 123.0, [], false, Minimum: 0, Maximum: 2147483647)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Mapas y atípicos",
        Roles:
        [
            new ColumnRole("vars", "Variables numéricas (2 o más)", ColumnKinds.Numeric, Multiple: true, MinCount: 2),
            new ColumnRole("group", "Colorear por (opcional)", ColumnKinds.Categorical, Optional: true),
            new ColumnRole("label", "Etiquetas (opcional)", ColumnKinds.Any, Optional: true)
        ],
        Outputs: [new AnalysisOutputSpec("coordenadas", "Coordenadas del mapa")]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        if (input.Pstr("method", "classical") != "classical" || input.Pstr("metric", "euclidean") != "euclidean")
        {
            return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "Por ahora solo está disponible el MDS clásico con distancia euclídea.");
        }
        var frame = input.AsFrame();
        var names = input.RoleNames("vars", true);
        var designResult = DesignBuilder.Build(frame, null, names, constant: false);
        if (designResult.IsError) return Result.Err<AnalysisResult>(designResult.Error.Code, designResult.Error.Message);
        var design = designResult.Value;
        var n = design.RowCount;
        var k = names.Count;
        if (n < 5) return ExtendedHelpers.Invalid("Se necesitan al menos 5 filas completas.");
        var x = design.Matrix;
        var means = Enumerable.Range(0, k).Select(column => x.Average(row => row[column])).ToArray();
        var sds = Enumerable.Range(0, k).Select(column =>
        {
            var variance = x.Sum(row => Math.Pow(row[column] - means[column], 2)) / (n - 1.0);
            return variance > 0 ? Math.Sqrt(variance) : 1;
        }).ToArray();
        var standardized = x.Select(row => row.Select((value, column) => (value - means[column]) / sds[column]).ToArray()).ToArray();
        var distances = new double[n][];
        for (var i = 0; i < n; i++)
        {
            distances[i] = new double[n];
            for (var j = 0; j < n; j++)
            {
                distances[i][j] = Math.Sqrt(standardized[i].Select((value, column) => Math.Pow(value - standardized[j][column], 2)).Sum());
            }
        }
        var b = new double[n][];
        var totalSquares = distances.Sum(row => row.Sum(value => value * value));
        var rowSquares = distances.Select(row => row.Sum(value => value * value)).ToArray();
        var columnSquares = Enumerable.Range(0, n).Select(column => distances.Sum(row => row[column] * row[column])).ToArray();
        for (var i = 0; i < n; i++)
        {
            b[i] = new double[n];
            for (var j = 0; j < n; j++)
            {
                b[i][j] = -0.5 * (distances[i][j] * distances[i][j] - rowSquares[i] / n - columnSquares[j] / n + totalSquares / ((double)n * n));
            }
        }
        var eigen = LinearAlgebra.EigenSymmetric(b);
        var order = Enumerable.Range(0, n).OrderByDescending(index => eigen.Values[index]).ToArray();
        var values = order.Select(index => eigen.Values[index]).ToArray();
        var dims = input.Pint("dimensions", 2);
        var coordinates = new double[n][];
        for (var row = 0; row < n; row++)
        {
            coordinates[row] = new double[dims];
            for (var dimension = 0; dimension < dims; dimension++)
            {
                coordinates[row][dimension] = eigen.Vectors[row][order[dimension]] * Math.Sqrt(Math.Max(values[dimension], 0));
                // Convención de signo de la primera dimensión (el signo de un vector propio es arbitrario).
                if (dimension == 0) coordinates[row][dimension] = -coordinates[row][dimension];
            }
        }
        var positive = values.Where(value => value > 0).Sum();
        var share = 100 * values.Take(dims).Sum() / positive;
        var fittedDistances = new double[n][];
        for (var i = 0; i < n; i++)
        {
            fittedDistances[i] = new double[n];
            for (var j = 0; j < n; j++)
            {
                fittedDistances[i][j] = Math.Sqrt(coordinates[i].Select((value, dimension) => Math.Pow(value - coordinates[j][dimension], 2)).Sum());
            }
        }
        var numerator = 0.0;
        var denominator = 0.0;
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                numerator += Math.Pow(fittedDistances[i][j] - distances[i][j], 2);
                denominator += distances[i][j] * distances[i][j];
            }
        }
        var stress = Math.Sqrt(numerator / denominator);

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("Varianza reproducida", ModelHelpers.N(share)),
                new TileItem("Dimensiones", JsNumber.ToString(dims))
            ]),
            new TableSection(
                "Valores propios",
                ["Dimensión", "Valor propio", "% de la varianza"],
                Enumerable.Range(0, Math.Min(8, n)).Select(index => (IReadOnlyList<string>)new[]
                {
                    JsNumber.ToString(index + 1),
                    ModelHelpers.N(values[index]),
                    ModelHelpers.N(100 * Math.Max(values[index], 0) / positive)
                }).ToArray(),
                Note: "Si hay valores propios negativos grandes, la matriz de distancias no es euclídea.")
        };
        var groupName = ExtendedHelpers.FirstRole(input, "group", "Colorear por");
        var series = new List<XYSeries>();
        if (!string.IsNullOrEmpty(groupName))
        {
            var labels = design.UsedRows.Select(row => frame.Texts(groupName)[row] ?? string.Empty).ToArray();
            foreach (var level in labels.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
            {
                var selected = Enumerable.Range(0, n).Where(index => labels[index] == level).ToArray();
                series.Add(new XYSeries(level, selected.Select(index => coordinates[index][0]).ToArray(), selected.Select(index => coordinates[index][1]).ToArray()));
            }
        }
        else
        {
            series.Add(new XYSeries("Observaciones", coordinates.Select(point => point[0]).ToArray(), coordinates.Select(point => point[1]).ToArray()));
        }
        sections.Add(new TableSection(
            "Ajuste",
            ["Medida", "Valor"],
            [["Estrés (diferencia relativa entre distancias reproducidas y originales)", ModelHelpers.N(stress)]]));

        var predictions = ModelHelpers.Predictions(
            frame,
            design,
            Enumerable.Range(0, dims).Select(dimension => ($"dim_{dimension + 1}", coordinates.Select(point => point[dimension]).ToArray())).ToArray());
        predictions = predictions with { Label = "Coordenadas del mapa" };

        return Result.Ok(new AnalysisResult(
            "mds",
            "Escalamiento multidimensional",
            sections,
            Subtitle: $"clásico (métrico) · distancia euclidean · {dims} dimensión(es) · n = {n}",
            Charts:
            [
                new SeriesChart("Configuración en las dos primeras dimensiones", series, "Dimensión 1", "Dimensión 2")
            ],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal) { ["coordenadas"] = predictions },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["stress"] = stress,
                ["n"] = n
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
