using StatisticEngine.Domain.Tests.Parity;
using StatisticEngine.Domain.TimeSeries;
using Xunit;

namespace StatisticEngine.Domain.Tests.TimeSeries;

/// <summary>Paridad de series temporales con el motor TypeScript.</summary>
public class TimeSeriesParityTests
{
    private static readonly Dictionary<string, Func<GoldenCase, object?>> Registry = new(StringComparer.Ordinal)
    {
        ["autocorrelation"] = goldenCase => ResultDto.From(Autocorrelations.Autocorrelation(
            GoldenReader.Numbers(goldenCase.Args[0]),
            OptionalInt(goldenCase, 1) ?? 20)),
        ["partialAutocorrelation"] = goldenCase => ResultDto.From(Autocorrelations.PartialAutocorrelation(
            GoldenReader.Numbers(goldenCase.Args[0]),
            OptionalInt(goldenCase, 1) ?? 20)),
        ["movingAverage"] = goldenCase => ResultDto.From(Smoothing.MovingAverage(
            GoldenReader.Numbers(goldenCase.Args[0]),
            GoldenReader.Int(goldenCase.Args[1]))),
        ["simpleExponentialSmoothing"] = goldenCase => ResultDto.From(Smoothing.SimpleExponentialSmoothing(
            GoldenReader.Numbers(goldenCase.Args[0]),
            OptionalNumber(goldenCase, 1) ?? 0.2)),
        ["doubleExponentialSmoothing"] = goldenCase => ResultDto.From(Smoothing.DoubleExponentialSmoothing(
            GoldenReader.Numbers(goldenCase.Args[0]),
            OptionalNumber(goldenCase, 1) ?? 0.2,
            OptionalNumber(goldenCase, 2) ?? 0.1)),
        ["wintersSmoothing"] = goldenCase => ResultDto.From(Smoothing.WintersSmoothing(
            GoldenReader.Numbers(goldenCase.Args[0]),
            GoldenReader.Int(goldenCase.Args[1]),
            OptionalNumber(goldenCase, 2) ?? 0.2,
            OptionalNumber(goldenCase, 3) ?? 0.1,
            OptionalNumber(goldenCase, 4) ?? 0.1)),
        ["difference"] = goldenCase => Decomposition.Difference(
            GoldenReader.Numbers(goldenCase.Args[0]),
            OptionalInt(goldenCase, 1) ?? 1),
        ["decompose"] = goldenCase => ResultDto.From(Decomposition.Decompose(
            GoldenReader.Numbers(goldenCase.Args[0]),
            GoldenReader.Int(goldenCase.Args[1]))),
        ["fitAr"] = goldenCase => ResultDto.From(ArModel.FitAr(
            GoldenReader.Numbers(goldenCase.Args[0]),
            OptionalInt(goldenCase, 1) ?? 5,
            OptionalInt(goldenCase, 2) ?? 5)),
        ["ljungBox"] = goldenCase => LjungBox.Compute(
            GoldenReader.Numbers(goldenCase.Args[0]),
            OptionalInt(goldenCase, 1) ?? 10)
    };

    [Fact]
    public void CoincideConElMotorTypescript()
    {
        GoldenSuite.Run("timeseries", Registry, 1e-12, "fitAr");
    }

    [Fact]
    public void ElAjusteArCoincideConElMotor()
    {
        GoldenSuite.RunSelected("timeseries", Registry, 1e-9, "fitAr");
    }

    private static bool Present(GoldenCase goldenCase, int index) =>
        index < goldenCase.Count && !GoldenReader.IsUndefined(goldenCase.Args[index]);

    private static double? OptionalNumber(GoldenCase goldenCase, int index) =>
        Present(goldenCase, index) ? GoldenReader.Number(goldenCase.Args[index]) : null;

    private static int? OptionalInt(GoldenCase goldenCase, int index) =>
        Present(goldenCase, index) ? GoldenReader.Int(goldenCase.Args[index]) : null;
}
