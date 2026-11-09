using System.Text.Json;
using StatisticEngine.Domain.Quality;
using StatisticEngine.Domain.Tests.Parity;
using Xunit;

namespace StatisticEngine.Domain.Tests.Quality;

/// <summary>Paridad del control estadístico de procesos y la capacidad con el motor TypeScript.</summary>
public class QualityParityTests
{
    private static readonly Dictionary<string, Func<GoldenCase, object?>> Registry = new(StringComparer.Ordinal)
    {
        ["evaluateRules"] = goldenCase => NelsonRules.EvaluateRules(
            GoldenReader.Numbers(goldenCase.Args[0]),
            GoldenReader.Number(goldenCase.Args[1]),
            GoldenReader.Number(goldenCase.Args[2])),
        ["xbarRChart"] = goldenCase => ResultDto.From(ControlCharts.XbarRChart(Subgroups(goldenCase))),
        ["xbarSChart"] = goldenCase => ResultDto.From(ControlCharts.XbarSChart(Subgroups(goldenCase))),
        ["individualsChart"] = goldenCase => ResultDto.From(ControlCharts.IndividualsChart(GoldenReader.Numbers(goldenCase.Args[0]))),
        ["pChart"] = goldenCase => ResultDto.From(ControlCharts.PChart(
            GoldenReader.Numbers(goldenCase.Args[0]),
            GoldenReader.Numbers(goldenCase.Args[1]))),
        ["npChart"] = goldenCase => ResultDto.From(ControlCharts.NpChart(
            GoldenReader.Numbers(goldenCase.Args[0]),
            GoldenReader.Number(goldenCase.Args[1]))),
        ["cChart"] = goldenCase => ResultDto.From(ControlCharts.CChart(GoldenReader.Numbers(goldenCase.Args[0]))),
        ["uChart"] = goldenCase => ResultDto.From(ControlCharts.UChart(
            GoldenReader.Numbers(goldenCase.Args[0]),
            GoldenReader.Numbers(goldenCase.Args[1]))),
        ["ewmaChart"] = goldenCase => ResultDto.From(ControlCharts.EwmaChart(
            GoldenReader.Numbers(goldenCase.Args[0]),
            OptionalNumber(goldenCase, 1) ?? 0.2,
            OptionalNumber(goldenCase, 2))),
        ["cusumChart"] = goldenCase => ResultDto.From(ControlCharts.CusumChart(
            GoldenReader.Numbers(goldenCase.Args[0]),
            OptionalNumber(goldenCase, 1),
            OptionalNumber(goldenCase, 2) ?? 0.5)),
        ["processCapability"] = goldenCase => ResultDto.From(Capability.ProcessCapability(
            GoldenReader.Numbers(goldenCase.Args[0]),
            OptionalNumber(goldenCase, 1),
            OptionalNumber(goldenCase, 2),
            OptionalCapabilityOptions(goldenCase, 3))),
        ["nonnormalCapability"] = goldenCase => ResultDto.From(Capability.NonnormalCapability(
            GoldenReader.Numbers(goldenCase.Args[0]),
            OptionalNumber(goldenCase, 1),
            OptionalNumber(goldenCase, 2))),
        ["histogram"] = goldenCase => Histogram.Build(
            GoldenReader.Numbers(goldenCase.Args[0]),
            OptionalInt(goldenCase, 1))
    };

    [Fact]
    public void CoincideConElMotorTypescript()
    {
        GoldenSuite.Run("spc", Registry, 1e-12);
    }

    private static SubgroupPoint[] Subgroups(GoldenCase goldenCase) =>
        goldenCase.Args[0].EnumerateArray()
            .Select(item => new SubgroupPoint(GoldenReader.Numbers(item.GetProperty("values"))))
            .ToArray();

    private static bool Present(GoldenCase goldenCase, int index) =>
        index < goldenCase.Count && !GoldenReader.IsUndefined(goldenCase.Args[index]);

    private static double? OptionalNumber(GoldenCase goldenCase, int index) =>
        Present(goldenCase, index) ? GoldenReader.Number(goldenCase.Args[index]) : null;

    private static int? OptionalInt(GoldenCase goldenCase, int index) =>
        Present(goldenCase, index) ? GoldenReader.Int(goldenCase.Args[index]) : null;

    private static CapabilityOptions? OptionalCapabilityOptions(GoldenCase goldenCase, int index)
    {
        if (!Present(goldenCase, index)) return null;
        var element = goldenCase.Args[index];
        var method = element.ValueKind == JsonValueKind.Object && element.TryGetProperty("method", out var property)
            ? property.GetString()
            : null;
        return new CapabilityOptions(method);
    }
}
