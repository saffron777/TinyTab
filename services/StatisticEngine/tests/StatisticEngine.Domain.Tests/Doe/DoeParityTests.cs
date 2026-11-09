using System.Text.Json;
using StatisticEngine.Domain.Doe;
using StatisticEngine.Domain.Tests.Parity;
using Xunit;

namespace StatisticEngine.Domain.Tests.Doe;

/// <summary>Paridad del módulo de diseño de experimentos (<c>doe.ts</c>).</summary>
public class DoeParityTests
{
    private static readonly Dictionary<string, Func<GoldenCase, object?>> Registry = new(StringComparer.Ordinal)
    {
        ["analyzeFactorial"] = goldenCase => ResultDto.From(FactorialAnalysis.AnalyzeFactorial(
            Design(goldenCase.Args[0]),
            GoldenReader.Numbers(goldenCase.Args[1]))),
        ["createCcd"] = goldenCase =>
        {
            var (centerPoints, seed) = goldenCase.Count > 1
                ? CcdOptions(goldenCase.Args[1])
                : (CenterPoints: 3, Seed: 20261023);
            return ResultDto.From(Designs.CreateCcd(Factors(goldenCase.Args[0]), centerPoints, seed));
        },
        ["createFactorialDesign"] = goldenCase =>
        {
            var (replicates, centerPoints, seed, randomize) = goldenCase.Count > 1
                ? FactorialOptions(goldenCase.Args[1])
                : (Replicates: 1, CenterPoints: 0, Seed: 20261023, Randomize: true);
            return ResultDto.From(Designs.CreateFactorialDesign(
                Factors(goldenCase.Args[0]),
                replicates,
                centerPoints,
                seed,
                randomize));
        },
        ["createPlackettBurman"] = goldenCase => ResultDto.From(Designs.CreatePlackettBurman(
            Factors(goldenCase.Args[0]),
            goldenCase.Count > 1 ? GoldenReader.Int(goldenCase.Args[1]) : 20261023)),
        ["designSummary"] = goldenCase => Designs.DesignSummary(Design(goldenCase.Args[0]))
    };

    [Fact]
    public void CoincideConElMotorTypescript() => GoldenSuite.Run("doe", Registry, 1e-12);

    private static (int CenterPoints, int Seed) CcdOptions(JsonElement element) => (
        element.TryGetProperty("centerPoints", out var centerPoints) ? GoldenReader.Int(centerPoints) : 3,
        element.TryGetProperty("seed", out var seed) ? GoldenReader.Int(seed) : 20261023);

    private static (int Replicates, int CenterPoints, int Seed, bool Randomize) FactorialOptions(JsonElement element) => (
        element.TryGetProperty("replicates", out var replicates) ? GoldenReader.Int(replicates) : 1,
        element.TryGetProperty("centerPoints", out var centerPoints) ? GoldenReader.Int(centerPoints) : 0,
        element.TryGetProperty("seed", out var seed) ? GoldenReader.Int(seed) : 20261023,
        !(element.TryGetProperty("randomize", out var randomize) && randomize.ValueKind == JsonValueKind.False));

    private static DoeDesign Design(JsonElement element) => new(
        element.GetProperty("type").GetString() ?? "factorial",
        Factors(element.GetProperty("factors")),
        Runs(element.GetProperty("runs")),
        GoldenReader.Number(element.GetProperty("replicateCount")),
        GoldenReader.Number(element.GetProperty("centerPoints")),
        GoldenReader.Number(element.GetProperty("seed")));

    private static FactorDef[] Factors(JsonElement element) =>
        element.EnumerateArray()
            .Select(factor => new FactorDef(
                factor.GetProperty("name").GetString() ?? string.Empty,
                GoldenReader.Number(factor.GetProperty("low")),
                GoldenReader.Number(factor.GetProperty("high"))))
            .ToArray();

    private static DesignRun[] Runs(JsonElement element) =>
        element.EnumerateArray()
            .Select(run => new DesignRun(
                GoldenReader.Number(run.GetProperty("standardOrder")),
                GoldenReader.Number(run.GetProperty("runOrder")),
                GoldenReader.Numbers(run.GetProperty("coded")),
                GoldenReader.Numbers(run.GetProperty("actual"))))
            .ToArray();
}
