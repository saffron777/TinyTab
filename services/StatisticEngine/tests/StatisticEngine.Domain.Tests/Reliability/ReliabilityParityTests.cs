using System.Text.Json;
using StatisticEngine.Domain.Reliability;
using StatisticEngine.Domain.Tests.Parity;
using Xunit;

namespace StatisticEngine.Domain.Tests.Reliability;

/// <summary>Paridad de fiabilidad y supervivencia con el motor TypeScript.</summary>
public class ReliabilityParityTests
{
    private static readonly Dictionary<string, Func<GoldenCase, object?>> Registry = new(StringComparer.Ordinal)
    {
        ["weibullFit"] = goldenCase => ResultDto.From(ReliabilityAnalysis.WeibullFit(GoldenReader.Numbers(goldenCase.Args[0]))),
        ["lognormalFit"] = goldenCase => ResultDto.From(ReliabilityAnalysis.LognormalFit(GoldenReader.Numbers(goldenCase.Args[0]))),
        ["kaplanMeier"] = goldenCase => ResultDto.From(ReliabilityAnalysis.KaplanMeier(
            GoldenReader.Numbers(goldenCase.Args[0]),
            Flags(goldenCase.Args[1]))),
        ["probabilityPlotPoints"] = goldenCase => ReliabilityAnalysis.ProbabilityPlotPoints(GoldenReader.Numbers(goldenCase.Args[0])),
        ["weibullQuantile"] = goldenCase => ReliabilityAnalysis.WeibullQuantile(
            GoldenReader.Number(goldenCase.Args[0]),
            GoldenReader.Number(goldenCase.Args[1]),
            GoldenReader.Number(goldenCase.Args[2])),
        ["reliabilityAt"] = goldenCase => ReliabilityAnalysis.ReliabilityAt(
            GoldenReader.Number(goldenCase.Args[0]),
            GoldenReader.Number(goldenCase.Args[1]),
            GoldenReader.Number(goldenCase.Args[2]))
    };

    [Fact]
    public void CoincideConElMotorTypescript()
    {
        GoldenSuite.Run("reliability", Registry, 1e-12, "weibullFit");
    }

    [Fact]
    public void ElAjusteWeibullCoincideConElMotor()
    {
        GoldenSuite.RunSelected("reliability", Registry, 1e-9, "weibullFit");
    }

    private static bool[] Flags(JsonElement element) =>
        element.EnumerateArray().Select(item => item.GetBoolean()).ToArray();
}
