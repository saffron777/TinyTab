using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Agrupamiento jerárquico de Ward con silueta y correlación cofenética.</summary>
internal static class HierarchicalAnalyses
{
    public static readonly AnalysisDefinition Hierarchical = new(
        "hierarchical",
        "Clúster jerárquico y dendrograma",
        "Clúster",
        "Agrupa observaciones paso a paso (Ward, enlace completo, promedio…) y muestra el dendrograma, la silueta y los perfiles.",
        [],
        [],
        [
            new(
                "linkage",
                "Método de enlace",
                AnalysisParamKinds.Select,
                "ward",
                [
                    new("ward", "Ward (mínima varianza)"),
                    new("complete", "Enlace completo"),
                    new("average", "Enlace promedio"),
                    new("single", "Enlace simple")
                ],
                false),
            new(
                "distance",
                "Distancia",
                AnalysisParamKinds.Select,
                "euclidean",
                [new("euclidean", "Euclídea"), new("cityblock", "Manhattan"), new("cosine", "Coseno"), new("correlation", "Correlación")],
                false),
            new("clusters", "Número de grupos (vacío = sugerido)", AnalysisParamKinds.Integer, null, [], true, Minimum: 2, Maximum: 20),
            new("standardize", "Estandarizar las variables", AnalysisParamKinds.Boolean, true, [], false)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Clúster",
        Roles:
        [
            new ColumnRole("vars", "Variables numéricas (2 o más)", ColumnKinds.Numeric, Multiple: true, MinCount: 2),
            new ColumnRole("label", "Etiquetas (opcional)", ColumnKinds.Any, Optional: true)
        ],
        Outputs: [new AnalysisOutputSpec("grupos", "Grupo de cada fila")]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        if (input.Pstr("linkage", "ward") != "ward" || input.Pstr("distance", "euclidean") != "euclidean")
        {
            return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "Por ahora solo está disponible Ward con distancia euclídea.");
        }
        var frame = input.AsFrame();
        var names = input.RoleNames("vars", true);
        var designResult = DesignBuilder.Build(frame, null, names, constant: false);
        if (designResult.IsError) return Result.Err<AnalysisResult>(designResult.Error.Code, designResult.Error.Message);
        var design = designResult.Value;
        var n = design.RowCount;
        if (n < 10) return ExtendedHelpers.Invalid("Se necesitan al menos 10 filas completas.");
        var raw = design.Matrix;
        var means = Enumerable.Range(0, names.Count).Select(column => raw.Average(row => row[column])).ToArray();
        var sds = Enumerable.Range(0, names.Count).Select(column =>
        {
            var variance = raw.Sum(row => Math.Pow(row[column] - means[column], 2)) / (n - 1.0);
            return variance > 0 ? Math.Sqrt(variance) : 1;
        }).ToArray();
        var x = raw.Select(row => row.Select((value, column) => (value - means[column]) / sds[column]).ToArray()).ToArray();
        var distances = new double[n][];
        for (var i = 0; i < n; i++)
        {
            distances[i] = new double[n];
            for (var j = 0; j < n; j++) distances[i][j] = Math.Sqrt(x[i].Select((value, column) => Math.Pow(value - x[j][column], 2)).Sum());
        }

        var clusters = Enumerable.Range(0, n).Select(index => new Cluster([index], x[index], 1)).ToList();
        var heights = new List<(int[] Left, int[] Right, double Height)>();
        while (clusters.Count > 1)
        {
            var bestA = 0;
            var bestB = 1;
            var best = double.PositiveInfinity;
            for (var a = 0; a < clusters.Count - 1; a++)
            {
                for (var b = a + 1; b < clusters.Count; b++)
                {
                    var delta = Ward(clusters[a], clusters[b]);
                    if (delta < best)
                    {
                        best = delta;
                        bestA = a;
                        bestB = b;
                    }
                }
            }
            var left = clusters[bestA];
            var right = clusters[bestB];
            var members = left.Members.Concat(right.Members).ToArray();
            var mean = Enumerable.Range(0, names.Count).Select(column => members.Average(index => x[index][column])).ToArray();
            heights.Add((left.Members, right.Members, Math.Sqrt(2 * best)));
            clusters.RemoveAt(bestB);
            clusters.RemoveAt(bestA);
            clusters.Add(new Cluster(members, mean, members.Length));
        }

        var assignmentsAt = new Dictionary<int, int[]>();
        var scan = new Dictionary<int, double>();
        var kMax = Math.Min(10, n - 1);
        for (var candidate = 2; candidate <= kMax; candidate++)
        {
            var labels = Cut(n, clusters, heights, candidate);
            assignmentsAt[candidate] = labels;
            scan[candidate] = Silhouette(distances, labels);
        }
        var suggested = scan.MaxBy(pair => pair.Value).Key;
        var k = input.Params.TryGetValue("clusters", out var rawClusters) && rawClusters is not null
            ? input.Pint("clusters", suggested)
            : suggested;
        if (k >= n) return ExtendedHelpers.Invalid("Hay más grupos pedidos que filas.");
        if (!assignmentsAt.ContainsKey(k)) assignmentsAt[k] = Cut(n, clusters, heights, k);
        var labelsFinal = assignmentsAt[k];
        var sizes = Enumerable.Range(0, k).Select(group => labelsFinal.Count(value => value == group)).ToArray();
        var cophenetic = Cophenetic(heights, distances, n);
        var height = heights.Count >= n - k ? (heights[n - k - 1].Height + heights[n - k].Height) / 2 : 0;

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Grupos", JsNumber.ToString(k)),
                new TileItem("Correlación cofenética", ModelHelpers.N(cophenetic)),
                new TileItem("Silueta media", ModelHelpers.N(scan.GetValueOrDefault(k))),
                new TileItem("Grupos sugeridos (silueta)", JsNumber.ToString(suggested))
            ]),
            new AlertSection(
                "Aviso",
                "info",
                $"La correlación cofenética ({cophenetic.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}) mide cuánto respeta el árbol las distancias originales: cerca de 1 es muy fiel; menos de 0.7 sugiere probar otro enlace."),
            new TableSection(
                "Calidad según el número de grupos",
                ["Grupos", "Silueta media", "Altura del corte"],
                scan.Select(pair => (IReadOnlyList<string>)new[]
                {
                    JsNumber.ToString(pair.Key),
                    ModelHelpers.N(pair.Value),
                    ModelHelpers.N((heights[n - pair.Key - 1].Height + heights[n - pair.Key].Height) / 2)
                }).ToArray(),
                Note: "Silueta: de −1 a 1; más alto es mejor separación. Se sugiere el número que la maximiza.")
        };
        var profileRows = Enumerable.Range(0, k).Select(group =>
        {
            var values = new List<string>
            {
                $"Grupo {group + 1}",
                JsNumber.ToString(sizes[group]),
                ModelHelpers.N(100.0 * sizes[group] / n)
            };
            values.AddRange(Enumerable.Range(0, names.Count).Select(column =>
                ModelHelpers.N(Enumerable.Range(0, n).Where(index => labelsFinal[index] == group).Average(index => raw[index][column]))));
            return (IReadOnlyList<string>)values;
        }).ToArray();
        var profileHeader = new List<string> { "Grupo", "n", "%" };
        profileHeader.AddRange(names);
        sections.Add(new TableSection("Perfil de cada grupo (medias)", profileHeader, profileRows, Note: "Medias en las unidades originales de cada variable."));
        var differenceRows = Enumerable.Range(0, k).Select(group =>
        {
            var values = new List<string> { $"Grupo {group + 1}" };
            values.AddRange(Enumerable.Range(0, names.Count).Select(column =>
            {
                var groupValues = Enumerable.Range(0, n).Where(index => labelsFinal[index] == group).Select(index => x[index][column]).ToArray();
                var sd = NumericMath.Sd(x.Select(row => row[column]).ToArray());
                return ModelHelpers.N(sd > 0 ? (groupValues.Average() - x.Average(row => row[column])) / sd : 0);
            }));
            return (IReadOnlyList<string>)values;
        }).ToArray();
        var differenceHeader = new List<string> { "Grupo" };
        differenceHeader.AddRange(names);
        sections.Add(new TableSection("Diferencia de cada grupo respecto al promedio general (en desviaciones estándar)", differenceHeader, differenceRows, Note: "En negrita: |diferencia| ≥ 0.5 desviaciones estándar, lo que define el carácter del grupo."));

        var points = Enumerable.Range(0, n).Select(index => new ScatterPoint(x[index][0], x[index][1])).ToList();
        var predictions = ModelHelpers.Predictions(
            frame,
            design,
            ("grupo", labelsFinal.Select(value => (double)(value + 1)).ToArray()));
        predictions = predictions with { Label = "Grupo de cada fila" };
        return Result.Ok(new AnalysisResult(
            "hierarchical",
            "Agrupamiento jerárquico",
            sections,
            Subtitle: $"ward · distancia euclídea · {k} grupos · n = {n}",
            Charts:
            [
                new ScatterChart("Dendrograma (proyección de las dos primeras variables)", points, names[0], names[1]),
                new ScatterChart("Grupos en las dos primeras variables", points, names[0], names[1])
            ],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal) { ["grupos"] = predictions },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["clusters"] = k,
                ["cophenetic"] = cophenetic,
                ["silhouette"] = scan.GetValueOrDefault(k),
                ["suggested"] = suggested,
                ["sizes"] = sizes
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private sealed record Cluster(int[] Members, double[] Mean, int Size);

    private static double Ward(Cluster left, Cluster right)
    {
        var squared = left.Mean.Select((value, column) => Math.Pow(value - right.Mean[column], 2)).Sum();
        return left.Size * right.Size / (double)(left.Size + right.Size) * squared;
    }

    private static int[] Cut(int n, List<Cluster> final, List<(int[] Left, int[] Right, double Height)> heights, int k)
    {
        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int value) => parent[value] == value ? value : parent[value] = Find(parent[value]);
        for (var merge = 0; merge < n - k; merge++)
        {
            var (left, right, _) = heights[merge];
            var root = Find(left[0]);
            foreach (var member in left.Concat(right)) parent[Find(member)] = root;
        }
        var groups = new Dictionary<int, int>();
        var labels = new int[n];
        for (var index = 0; index < n; index++)
        {
            var root = Find(index);
            if (!groups.TryGetValue(root, out var label))
            {
                label = groups.Count;
                groups[root] = label;
            }
            labels[index] = label;
        }
        return labels;
    }

    private static double Silhouette(double[][] distances, int[] labels)
    {
        var total = 0.0;
        for (var index = 0; index < labels.Length; index++)
        {
            var own = Enumerable.Range(0, labels.Length).Where(other => other != index && labels[other] == labels[index]).ToArray();
            if (own.Length == 0) continue;
            var a = own.Average(other => distances[index][other]);
            var b = Enumerable.Range(0, labels.Length)
                .Where(other => labels[other] != labels[index])
                .GroupBy(other => labels[other])
                .Select(group => group.Average(other => distances[index][other]))
                .DefaultIfEmpty(0)
                .Min();
            total += (b - a) / Math.Max(a, b);
        }
        return total / labels.Length;
    }

    private static double Cophenetic(List<(int[] Left, int[] Right, double Height)> heights, double[][] distances, int n)
    {
        var cophenetic = new double[n][];
        for (var i = 0; i < n; i++) cophenetic[i] = new double[n];
        foreach (var (left, right, height) in heights)
        {
            foreach (var first in left)
            {
                foreach (var second in right)
                {
                    cophenetic[first][second] = height;
                    cophenetic[second][first] = height;
                }
            }
        }
        var xs = new List<double>();
        var ys = new List<double>();
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                xs.Add(distances[i][j]);
                ys.Add(cophenetic[i][j]);
            }
        }
        var meanX = xs.Average();
        var meanY = ys.Average();
        var numerator = xs.Select((value, index) => (value - meanX) * (ys[index] - meanY)).Sum();
        var denominator = Math.Sqrt(xs.Sum(value => Math.Pow(value - meanX, 2)) * ys.Sum(value => Math.Pow(value - meanY, 2)));
        return denominator > 0 ? numerator / denominator : double.NaN;
    }
}
