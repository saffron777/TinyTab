using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Tests.Parity;
using Xunit;

namespace StatisticEngine.Domain.Tests.Inference;

/// <summary>Paridad de las funciones de distribución compartidas por varios módulos.</summary>
public class InferenceDistributionsParityTests
{
    [Fact]
    public void LaTdeStudentCoincideConElMotor()
    {
        var registry = new Dictionary<string, Func<GoldenCase, object?>>(StringComparer.Ordinal)
        {
            ["tCdf"] = goldenCase => InferenceDistributions.TCdf(GoldenReader.Number(goldenCase.Args[0]), GoldenReader.Number(goldenCase.Args[1])),
            ["tQuantile"] = goldenCase => InferenceDistributions.TQuantile(GoldenReader.Number(goldenCase.Args[0]), GoldenReader.Number(goldenCase.Args[1]))
        };
        GoldenSuite.RunSelected("tests", registry, 1e-12, "tCdf", "tQuantile");
    }

    [Fact]
    public void LaFDeFisherCoincideConElMotor()
    {
        var registry = new Dictionary<string, Func<GoldenCase, object?>>(StringComparer.Ordinal)
        {
            ["fCdf"] = goldenCase => InferenceDistributions.FCdf(GoldenReader.Number(goldenCase.Args[0]), GoldenReader.Number(goldenCase.Args[1]), GoldenReader.Number(goldenCase.Args[2])),
            ["fCdfUpper"] = goldenCase => InferenceDistributions.FCdfUpper(GoldenReader.Number(goldenCase.Args[0]), GoldenReader.Number(goldenCase.Args[1]), GoldenReader.Number(goldenCase.Args[2]))
        };
        GoldenSuite.RunSelected("anova", registry, 1e-12, "fCdf", "fCdfUpper");
    }
}
