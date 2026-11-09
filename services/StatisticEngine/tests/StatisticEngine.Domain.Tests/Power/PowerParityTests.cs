using StatisticEngine.Domain.Power;
using StatisticEngine.Domain.Tests.Parity;
using Xunit;

namespace StatisticEngine.Domain.Tests.Power;

/// <summary>Paridad de potencia y tamaño de muestra con el motor TypeScript.</summary>
public class PowerParityTests
{
    private static readonly Dictionary<string, Func<GoldenCase, object?>> Registry = new(StringComparer.Ordinal)
    {
        ["powerOneSampleT"] = goldenCase => ResultDto.From(PowerAnalysis.PowerOneSampleT(
            GoldenReader.Number(goldenCase.Args[0]),
            GoldenReader.Number(goldenCase.Args[1]),
            OptionalNumber(goldenCase, 2) ?? 0.05)),
        ["sampleSizeOneSampleT"] = goldenCase => ResultDto.From(PowerAnalysis.SampleSizeOneSampleT(
            GoldenReader.Number(goldenCase.Args[0]),
            OptionalNumber(goldenCase, 1) ?? 0.8,
            OptionalNumber(goldenCase, 2) ?? 0.05)),
        ["powerTwoSampleT"] = goldenCase => ResultDto.From(PowerAnalysis.PowerTwoSampleT(
            GoldenReader.Number(goldenCase.Args[0]),
            GoldenReader.Number(goldenCase.Args[1]),
            OptionalNumber(goldenCase, 2) ?? 0.05)),
        ["sampleSizeTwoSampleT"] = goldenCase => ResultDto.From(PowerAnalysis.SampleSizeTwoSampleT(
            GoldenReader.Number(goldenCase.Args[0]),
            OptionalNumber(goldenCase, 1) ?? 0.8,
            OptionalNumber(goldenCase, 2) ?? 0.05)),
        ["powerOneProportion"] = goldenCase => ResultDto.From(PowerAnalysis.PowerOneProportion(
            GoldenReader.Number(goldenCase.Args[0]),
            GoldenReader.Number(goldenCase.Args[1]),
            GoldenReader.Number(goldenCase.Args[2]),
            OptionalNumber(goldenCase, 3) ?? 0.05)),
        ["sampleSizeOneProportion"] = goldenCase => ResultDto.From(PowerAnalysis.SampleSizeOneProportion(
            GoldenReader.Number(goldenCase.Args[0]),
            GoldenReader.Number(goldenCase.Args[1]),
            OptionalNumber(goldenCase, 2) ?? 0.8,
            OptionalNumber(goldenCase, 3) ?? 0.05)),
        ["powerOneWayAnova"] = goldenCase => ResultDto.From(PowerAnalysis.PowerOneWayAnova(
            GoldenReader.Number(goldenCase.Args[0]),
            GoldenReader.Number(goldenCase.Args[1]),
            GoldenReader.Number(goldenCase.Args[2]),
            OptionalNumber(goldenCase, 3) ?? 0.05)),
        ["ocCurve"] = goldenCase => PowerAnalysis.OcCurve(
            GoldenReader.Number(goldenCase.Args[0]),
            GoldenReader.Numbers(goldenCase.Args[1]),
            OptionalNumber(goldenCase, 2) ?? 0.05)
    };

    [Fact]
    public void CoincideConElMotorTypescript()
    {
        GoldenSuite.Run("power", Registry, 1e-12);
    }

    private static bool Present(GoldenCase goldenCase, int index) =>
        index < goldenCase.Count && !GoldenReader.IsUndefined(goldenCase.Args[index]);

    private static double? OptionalNumber(GoldenCase goldenCase, int index) =>
        Present(goldenCase, index) ? GoldenReader.Number(goldenCase.Args[index]) : null;
}
