using System.Text.Json;
using StatisticEngine.Domain.Descriptive;
using StatisticEngine.Domain.Tests.Parity;
using Xunit;

namespace StatisticEngine.Domain.Tests.Descriptive;

/// <summary>Paridad de la estadística descriptiva (describe, IC de la media, matrices).</summary>
public class DescriptiveParityTests
{
    [Fact]
    public void LaDescriptivaCoincideConElMotor()
    {
        var registry = new Dictionary<string, Func<GoldenCase, object?>>(StringComparer.Ordinal)
        {
            ["describe"] = goldenCase => DescriptiveStats.Describe(
                GoldenReader.Numbers(goldenCase.Args[0]),
                Optional(goldenCase, 1, 0)),

            ["meanConfidenceInterval"] = goldenCase => DescriptiveStats.MeanConfidenceInterval(
                GoldenReader.Numbers(goldenCase.Args[0]),
                Optional(goldenCase, 1, 0.95),
                goldenCase.Count > 2 ? GoldenReader.Number(goldenCase.Args[2]) : null),

            ["describeByGroup"] = goldenCase => DescriptiveStats.DescribeByGroup(Groups(goldenCase.Args[0])),

            ["correlationMatrix"] = goldenCase => DescriptiveStats.CorrelationMatrix(
                GoldenReader.Texts(goldenCase.Args[0]),
                Columns(goldenCase.Args[1])),

            ["covarianceMatrix"] = goldenCase => DescriptiveStats.CovarianceMatrix(Columns(goldenCase.Args[0]))
        };

        GoldenSuite.Run("descriptive", registry, 1e-12);
    }

    private static double Optional(GoldenCase goldenCase, int index, double fallback) =>
        goldenCase.Count > index ? GoldenReader.Number(goldenCase.Args[index]) : fallback;

    private static List<DescriptiveGroup> Groups(JsonElement element)
    {
        var groups = new List<DescriptiveGroup>();
        foreach (var pair in element.EnumerateArray())
        {
            var items = pair.EnumerateArray().ToArray();
            groups.Add(new DescriptiveGroup(GoldenReader.Text(items[0]), GoldenReader.Numbers(items[1])));
        }
        return groups;
    }

    private static IReadOnlyList<double>[] Columns(JsonElement element)
    {
        var rows = GoldenReader.Matrix(element);
        var output = new IReadOnlyList<double>[rows.Length];
        for (var i = 0; i < rows.Length; i++) output[i] = rows[i];
        return output;
    }
}
