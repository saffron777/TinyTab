using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Matriz de dispersión y correlaciones.</summary>
internal static class SplomAnalyses
{
    public static readonly AnalysisDefinition Splom = new(
        "splom",
        "Matriz de dispersión",
        "Relaciones",
        "Todos los pares de variables a la vez, con la matriz de correlaciones.",
        [],
        [],
        [],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Relaciones",
        Roles:
        [
            new ColumnRole("vars", "Variables (2 a 8)", ColumnKinds.Numeric, Multiple: true, MinCount: 2, MaxCount: 8),
            new ColumnRole("group", "Grupos (opcional)", ColumnKinds.Categorical, Optional: true)
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var names = input.RoleNames("vars", true);
        if (names.Count < 2) return ExtendedHelpers.Invalid("Elija al menos dos variables.");
        var designResult = DesignBuilder.Build(frame, null, names, constant: false);
        if (designResult.IsError) return Result.Err<AnalysisResult>(designResult.Error.Code, designResult.Error.Message);
        var design = designResult.Value;
        var n = design.RowCount;
        var k = names.Count;
        var matrix = new double[k][];
        for (var i = 0; i < k; i++)
        {
            matrix[i] = new double[k];
            for (var j = 0; j < k; j++) matrix[i][j] = i == j ? 1 : Correlation(design.Matrix, i, j);
        }
        var rows = Enumerable.Range(0, k).Select(i =>
        {
            var values = new List<string> { names[i] };
            values.AddRange(Enumerable.Range(0, k).Select(j => ModelHelpers.N(matrix[i][j])));
            return (IReadOnlyList<string>)values;
        }).ToArray();
        var correlations = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < k; i++)
        {
            correlations[names[i]] = Enumerable.Range(0, k).ToDictionary(j => names[j], j => (object?)Math.Round(matrix[i][j], 4), StringComparer.Ordinal);
        }
        var groupName = ExtendedHelpers.FirstRole(input, "group", "Grupos");
        var points = Enumerable.Range(0, n).Select(row => new ScatterPoint(design.Matrix[row][0], design.Matrix[row][1])).ToList();
        return Result.Ok(new AnalysisResult(
            "splom",
            "Matriz de dispersión",
            [
                new TableSection(
                    "Correlaciones de Pearson",
                    ["Variable", .. names],
                    rows,
                    Note: "En negrita: |r| ≥ 0.7. El gráfico muestra hasta 2 000 filas.")
            ],
            Subtitle: string.IsNullOrEmpty(groupName) ? "todas las variables" : "por " + groupName,
            Charts:
            [
                new ScatterChart("Matriz de dispersión", points, names[0], names[1])
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal) { ["correlations"] = correlations },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double Correlation(double[][] matrix, int first, int second)
    {
        var n = matrix.Length;
        var meanFirst = matrix.Average(row => row[first]);
        var meanSecond = matrix.Average(row => row[second]);
        var numerator = matrix.Sum(row => (row[first] - meanFirst) * (row[second] - meanSecond));
        var denominator = Math.Sqrt(matrix.Sum(row => Math.Pow(row[first] - meanFirst, 2)) * matrix.Sum(row => Math.Pow(row[second] - meanSecond, 2)));
        return denominator > 0 ? numerator / denominator : double.NaN;
    }
}
