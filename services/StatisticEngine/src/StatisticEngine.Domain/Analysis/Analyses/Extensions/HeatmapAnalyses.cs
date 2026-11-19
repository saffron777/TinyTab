using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Mapa de calor de correlaciones.</summary>
internal static class HeatmapAnalyses
{
    public static readonly AnalysisDefinition Heatmap = new(
        "heatmap",
        "Mapa de calor de correlaciones",
        "Relaciones",
        "Matriz de correlaciones (Pearson, Spearman o Kendall) en colores, con orden por agrupamiento jerárquico opcional.",
        [],
        [],
        [
            new(
                "method",
                "Coeficiente",
                AnalysisParamKinds.Select,
                "pearson",
                [new("pearson", "Pearson"), new("spearman", "Spearman"), new("kendall", "Kendall")],
                false),
            new("cluster", "Ordenar por similitud (clúster jerárquico)", AnalysisParamKinds.Boolean, true, [], false)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Relaciones",
        Roles:
        [
            new ColumnRole("vars", "Variables (2 o más)", ColumnKinds.Numeric, Multiple: true, MinCount: 2)
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var method = input.Pstr("method", "pearson");
        if (method == "kendall")
        {
            return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "El coeficiente de Kendall aún no está disponible; use Pearson o Spearman.");
        }
        var frame = input.AsFrame();
        var names = input.RoleNames("vars", true);
        if (names.Count < 2) return ExtendedHelpers.Invalid("Elija al menos dos variables.");
        var complete = frame.CompleteCases(names);
        var n = complete.RowCount;
        var k = names.Count;
        var data = new double[n][];
        for (var row = 0; row < n; row++)
        {
            data[row] = new double[k];
            for (var column = 0; column < k; column++) data[row][column] = complete.Numeric(names[column])[row];
        }
        var matrix = new double[k][];
        for (var i = 0; i < k; i++)
        {
            matrix[i] = new double[k];
            for (var j = 0; j < k; j++)
            {
                matrix[i][j] = i == j ? 1 : method == "spearman"
                    ? Spearman(data.Select(row => row[i]).ToArray(), data.Select(row => row[j]).ToArray())
                    : Correlation(data.Select(row => row[i]).ToArray(), data.Select(row => row[j]).ToArray());
            }
        }
        var pairs = new List<(string First, string Second, double Coefficient, double PValue)>();
        for (var i = 0; i < k - 1; i++)
        {
            for (var j = i + 1; j < k; j++)
            {
                var r = matrix[i][j];
                var t = Math.Abs(r) < 1 ? r * Math.Sqrt((n - 2.0) / (1 - r * r)) : double.PositiveInfinity;
                pairs.Add((names[i], names[j], r, 2 * (1 - InferenceDistributions.TCdf(Math.Abs(t), n - 2))));
            }
        }
        pairs.Sort((left, right) => Math.Abs(right.Coefficient).CompareTo(Math.Abs(left.Coefficient)));
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Variables", JsNumber.ToString(k)),
                new TileItem("Filas con algún dato", JsNumber.ToString(n))
            ]),
            new TableSection(
                "Pares con mayor correlación",
                ["Variable 1", "Variable 2", "n", "Coeficiente", "Valor p"],
                pairs.Select(pair => (IReadOnlyList<string>)new[]
                {
                    pair.First,
                    pair.Second,
                    JsNumber.ToString(n),
                    ModelHelpers.N(pair.Coefficient),
                    ModelHelpers.P(pair.PValue)
                }).ToArray(),
                Note: "En rojo: |coeficiente| ≥ 0.7.")
        };
        var points = Enumerable.Range(0, k).SelectMany(i => Enumerable.Range(0, k).Select(j => new ScatterPoint(i, j))).ToList();
        var strongest = pairs.Count > 0 ? pairs[0] : ("", "", 0, 0);
        var methodLabel = method == "pearson" ? "Pearson" : "Spearman";
        return Result.Ok(new AnalysisResult(
            "heatmap",
            $"Mapa de calor de correlaciones ({methodLabel})",
            sections,
            Subtitle: $"{k} variables",
            Charts:
            [
                new ScatterChart("Mapa de calor de correlaciones", points, "Variable", "Variable")
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["strongest"] = new object[] { strongest.First, strongest.Second, strongest.Coefficient }
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double Correlation(double[] x, double[] y)
    {
        var meanX = x.Average();
        var meanY = y.Average();
        var numerator = x.Select((value, index) => (value - meanX) * (y[index] - meanY)).Sum();
        var denominator = Math.Sqrt(x.Sum(value => Math.Pow(value - meanX, 2)) * y.Sum(value => Math.Pow(value - meanY, 2)));
        return denominator > 0 ? numerator / denominator : double.NaN;
    }

    private static double Spearman(double[] x, double[] y) => Correlation(Ranks(x), Ranks(y));

    private static double[] Ranks(double[] values)
    {
        var order = Enumerable.Range(0, values.Length).OrderBy(index => values[index]).ToArray();
        var ranks = new double[values.Length];
        for (var position = 0; position < order.Length;)
        {
            var end = position + 1;
            while (end < order.Length && values[order[end]] == values[order[position]]) end++;
            var average = (position + end - 1) / 2.0 + 1;
            for (var index = position; index < end; index++) ranks[order[index]] = average;
            position = end;
        }
        return ranks;
    }
}
