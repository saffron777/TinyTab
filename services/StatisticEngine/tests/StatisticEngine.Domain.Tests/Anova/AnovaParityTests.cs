using System.Text.Json;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Tests.Parity;
using Xunit;
using AnovaModule = StatisticEngine.Domain.Inference.Anova;

namespace StatisticEngine.Domain.Tests.Anova;

/// <summary>Paridad del módulo ANOVA (<c>anova.ts</c>): tablas, varianzas, comparaciones e IC.</summary>
public class AnovaParityTests
{
    private static readonly Dictionary<string, Func<GoldenCase, object?>> Registry = new(StringComparer.Ordinal)
    {
        ["bartlettTest"] = goldenCase => ResultDto.From(VarianceEquality.BartlettTest(Groups(goldenCase.Args[0]))),
        ["fCdf"] = goldenCase => InferenceDistributions.FCdf(Argument(goldenCase, 0), Argument(goldenCase, 1), Argument(goldenCase, 2)),
        ["fCdfUpper"] = goldenCase => InferenceDistributions.FCdfUpper(Argument(goldenCase, 0), Argument(goldenCase, 1), Argument(goldenCase, 2)),
        ["groupConfidenceIntervals"] = goldenCase => MultipleComparisons.GroupConfidenceIntervals(
            Groups(goldenCase.Args[0]),
            GoldenReader.Texts(goldenCase.Args[1]),
            goldenCase.Count > 2 ? GoldenReader.Number(goldenCase.Args[2]) : 0.95),
        ["leveneTest"] = goldenCase => ResultDto.From(VarianceEquality.LeveneTest(Groups(goldenCase.Args[0]))),
        ["oneWayAnova"] = goldenCase => ResultDto.From(AnovaModule.OneWayAnova(
            Groups(goldenCase.Args[0]),
            GoldenReader.Texts(goldenCase.Args[1]))),
        ["pairwiseComparisons"] = goldenCase => ResultDto.From(MultipleComparisons.PairwiseComparisons(
            Groups(goldenCase.Args[0]),
            GoldenReader.Texts(goldenCase.Args[1]),
            goldenCase.Count > 2 ? GoldenReader.Number(goldenCase.Args[2]) : 0.05,
            goldenCase.Count > 3 ? GoldenReader.Text(goldenCase.Args[3]) : "holm")),
        ["twoWayAnova"] = goldenCase => ResultDto.From(AnovaModule.TwoWayAnova(
            GoldenReader.Numbers(goldenCase.Args[0]),
            Levels(goldenCase.Args[1]),
            Levels(goldenCase.Args[2])))
    };

    [Fact]
    public void CoincideConElMotorTypescript() => GoldenSuite.Run("anova", Registry, 1e-12);

    private static double Argument(GoldenCase goldenCase, int index) => GoldenReader.Number(goldenCase.Args[index]);

    private static double[][] Groups(JsonElement element) => GoldenReader.Matrix(element);

    /// <summary>Niveles de factor tal como los convierte <c>String(x)</c> en JS (texto o número).</summary>
    private static string[] Levels(JsonElement element) =>
        element.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.Number
                ? JsNumber.ToString(item.GetDouble())
                : item.GetString() ?? string.Empty)
            .ToArray();
}
