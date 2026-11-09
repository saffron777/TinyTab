using System.Text.Json;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Tests.Parity;
using Xunit;

namespace StatisticEngine.Domain.Tests.Inference;

/// <summary>Paridad de las pruebas de hipótesis clásicas y no paramétricas.</summary>
public class HypothesisTestsParityTests
{
    [Fact]
    public void LasPruebasDeHipotesisCoincidenConElMotor()
    {
        var registry = new Dictionary<string, Func<GoldenCase, object?>>(StringComparer.Ordinal)
        {
            ["tCdf"] = goldenCase => InferenceDistributions.TCdf(N(goldenCase, 0), N(goldenCase, 1)),

            ["tQuantile"] = goldenCase => InferenceDistributions.TQuantile(N(goldenCase, 0), N(goldenCase, 1)),

            ["oneSampleT"] = goldenCase => ResultDto.From(HypothesisTests.OneSampleT(
                GoldenReader.Numbers(goldenCase.Args[0]),
                N(goldenCase, 1, 0),
                N(goldenCase, 2, 0.95),
                AlternativeAt(goldenCase, 3))),

            ["oneSampleZ"] = goldenCase => ResultDto.From(HypothesisTests.OneSampleZ(
                GoldenReader.Numbers(goldenCase.Args[0]),
                N(goldenCase, 1),
                N(goldenCase, 2, 0),
                N(goldenCase, 3, 0.95),
                AlternativeAt(goldenCase, 4))),

            ["twoSampleT"] = goldenCase => ResultDto.From(TwoSampleT(goldenCase)),

            ["pairedT"] = goldenCase => ResultDto.From(PairedT(goldenCase)),

            ["oneProportion"] = goldenCase => ResultDto.From(HypothesisTests.OneProportion(
                N(goldenCase, 0),
                N(goldenCase, 1),
                N(goldenCase, 2, 0.5),
                N(goldenCase, 3, 0.95),
                AlternativeAt(goldenCase, 4))),

            ["twoProportions"] = goldenCase => ResultDto.From(HypothesisTests.TwoProportions(
                N(goldenCase, 0),
                N(goldenCase, 1),
                N(goldenCase, 2),
                N(goldenCase, 3),
                N(goldenCase, 4, 0.95),
                AlternativeAt(goldenCase, 5))),

            ["oneVariance"] = goldenCase => ResultDto.From(HypothesisTests.OneVariance(
                N(goldenCase, 0),
                N(goldenCase, 1),
                N(goldenCase, 2, 1),
                N(goldenCase, 3, 0.95),
                AlternativeAt(goldenCase, 4))),

            ["twoVariances"] = goldenCase => ResultDto.From(HypothesisTests.TwoVariances(
                N(goldenCase, 0),
                N(goldenCase, 1),
                N(goldenCase, 2),
                N(goldenCase, 3),
                N(goldenCase, 4, 0.95),
                AlternativeAt(goldenCase, 5))),

            ["chiSquareGoodnessOfFit"] = goldenCase => ResultDto.From(HypothesisTests.ChiSquareGoodnessOfFit(
                GoldenReader.Numbers(goldenCase.Args[0]),
                GoldenReader.Numbers(goldenCase.Args[1]))),

            ["chiSquareIndependence"] = goldenCase => ResultDto.From(
                HypothesisTests.ChiSquareIndependence(Columns(goldenCase.Args[0]))),

            ["andersonDarling"] = goldenCase => ResultDto.From(
                HypothesisTests.AndersonDarling(GoldenReader.Numbers(goldenCase.Args[0]))),

            ["signTest"] = goldenCase => ResultDto.From(HypothesisTests.SignTest(
                GoldenReader.Numbers(goldenCase.Args[0]),
                N(goldenCase, 1, 0),
                AlternativeAt(goldenCase, 2))),

            ["wilcoxonSignedRank"] = goldenCase => ResultDto.From(HypothesisTests.WilcoxonSignedRank(
                GoldenReader.Numbers(goldenCase.Args[0]),
                N(goldenCase, 1, 0),
                AlternativeAt(goldenCase, 2))),

            ["mannWhitney"] = goldenCase => ResultDto.From(HypothesisTests.MannWhitney(
                GoldenReader.Numbers(goldenCase.Args[0]),
                GoldenReader.Numbers(goldenCase.Args[1]),
                AlternativeAt(goldenCase, 2))),

            ["kruskalWallis"] = goldenCase => ResultDto.From(
                HypothesisTests.KruskalWallis(Columns(goldenCase.Args[0]))),

            ["runsTest"] = goldenCase => ResultDto.From(
                HypothesisTests.RunsTest(GoldenReader.Numbers(goldenCase.Args[0]))),

            ["spearmanCorrelation"] = goldenCase => ResultDto.From(HypothesisTests.SpearmanCorrelation(
                GoldenReader.Numbers(goldenCase.Args[0]),
                GoldenReader.Numbers(goldenCase.Args[1]))),

            ["tostTwoSample"] = goldenCase => ResultDto.From(HypothesisTests.TostTwoSample(
                GoldenReader.Numbers(goldenCase.Args[0]),
                GoldenReader.Numbers(goldenCase.Args[1]),
                N(goldenCase, 2),
                N(goldenCase, 3, 0.95)))
        };

        GoldenSuite.Run("tests", registry, 1e-12);
    }

    private static double N(GoldenCase goldenCase, int index) => GoldenReader.Number(goldenCase.Args[index]);

    private static double N(GoldenCase goldenCase, int index, double fallback) =>
        goldenCase.Count > index ? GoldenReader.Number(goldenCase.Args[index]) : fallback;

    private static Alternative AlternativeAt(GoldenCase goldenCase, int index) =>
        goldenCase.Count > index
            ? Alternatives.Parse(GoldenReader.Text(goldenCase.Args[index]))
            : Alternative.TwoSided;

    private static Result<TTestResult> TwoSampleT(GoldenCase goldenCase)
    {
        var first = GoldenReader.Numbers(goldenCase.Args[0]);
        var second = GoldenReader.Numbers(goldenCase.Args[1]);
        var pooled = false;
        var confidence = 0.95;
        var alternative = Alternative.TwoSided;
        if (goldenCase.Count > 2 && goldenCase.Args[2].ValueKind == JsonValueKind.Object)
        {
            var options = goldenCase.Args[2];
            if (options.TryGetProperty("pooled", out var pooledElement)) pooled = pooledElement.GetBoolean();
            if (options.TryGetProperty("confidence", out var confidenceElement)) confidence = GoldenReader.Number(confidenceElement);
            if (options.TryGetProperty("alternative", out var alternativeElement)) alternative = Alternatives.Parse(GoldenReader.Text(alternativeElement));
        }
        return HypothesisTests.TwoSampleT(first, second, pooled, confidence, alternative);
    }

    private static Result<TTestResult> PairedT(GoldenCase goldenCase)
    {
        var first = GoldenReader.Numbers(goldenCase.Args[0]);
        var second = GoldenReader.Numbers(goldenCase.Args[1]);
        var confidence = 0.95;
        var alternative = Alternative.TwoSided;
        if (goldenCase.Count > 2 && goldenCase.Args[2].ValueKind == JsonValueKind.Object)
        {
            var options = goldenCase.Args[2];
            if (options.TryGetProperty("confidence", out var confidenceElement)) confidence = GoldenReader.Number(confidenceElement);
            if (options.TryGetProperty("alternative", out var alternativeElement)) alternative = Alternatives.Parse(GoldenReader.Text(alternativeElement));
        }
        return HypothesisTests.PairedT(first, second, confidence, alternative);
    }

    private static IReadOnlyList<double>[] Columns(JsonElement element)
    {
        var rows = GoldenReader.Matrix(element);
        var output = new IReadOnlyList<double>[rows.Length];
        for (var i = 0; i < rows.Length; i++) output[i] = rows[i];
        return output;
    }
}
