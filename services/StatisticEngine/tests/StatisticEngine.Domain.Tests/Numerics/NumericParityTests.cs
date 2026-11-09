using StatisticEngine.Domain;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Tests.Parity;
using Xunit;

namespace StatisticEngine.Domain.Tests.Numerics;

public class NumericParityTests
{
    private static readonly Dictionary<string, Func<GoldenCase, object?>> Registry = new(StringComparer.Ordinal)
    {
        ["sum"] = goldenCase => NumericMath.Sum(GoldenReader.Numbers(goldenCase.Args[0])),
        ["mean"] = goldenCase => NumericMath.Mean(GoldenReader.Numbers(goldenCase.Args[0])),
        ["variance"] = goldenCase => NumericMath.Variance(GoldenReader.Numbers(goldenCase.Args[0]), GoldenReader.Int(goldenCase.Args[1])),
        ["sd"] = goldenCase => NumericMath.Sd(GoldenReader.Numbers(goldenCase.Args[0]), GoldenReader.Int(goldenCase.Args[1])),
        ["seMean"] = goldenCase => NumericMath.StandardErrorOfMean(GoldenReader.Numbers(goldenCase.Args[0])),
        ["sorted"] = goldenCase => NumericMath.Sorted(GoldenReader.Numbers(goldenCase.Args[0])),
        ["minOf"] = goldenCase => NumericMath.MinOf(GoldenReader.Numbers(goldenCase.Args[0])),
        ["maxOf"] = goldenCase => NumericMath.MaxOf(GoldenReader.Numbers(goldenCase.Args[0])),
        ["quantile"] = goldenCase => NumericMath.Quantile(GoldenReader.Numbers(goldenCase.Args[0]), GoldenReader.Number(goldenCase.Args[1])),
        ["median"] = goldenCase => NumericMath.Median(GoldenReader.Numbers(goldenCase.Args[0])),
        ["iqr"] = goldenCase => NumericMath.Iqr(GoldenReader.Numbers(goldenCase.Args[0])),
        ["skewness"] = goldenCase => NumericMath.Skewness(GoldenReader.Numbers(goldenCase.Args[0])),
        ["kurtosis"] = goldenCase => NumericMath.Kurtosis(GoldenReader.Numbers(goldenCase.Args[0])),
        ["covariance"] = goldenCase => NumericMath.Covariance(GoldenReader.Numbers(goldenCase.Args[0]), GoldenReader.Numbers(goldenCase.Args[1])),
        ["correlation"] = goldenCase => NumericMath.Correlation(GoldenReader.Numbers(goldenCase.Args[0]), GoldenReader.Numbers(goldenCase.Args[1])),
        ["ranks"] = goldenCase => NumericMath.Ranks(GoldenReader.Numbers(goldenCase.Args[0])),
        ["clamp"] = goldenCase => JsMath.Clamp(GoldenReader.Number(goldenCase.Args[0]), GoldenReader.Number(goldenCase.Args[1]), GoldenReader.Number(goldenCase.Args[2])),
        ["logFactorial"] = goldenCase => NumericMath.LogFactorial(GoldenReader.Number(goldenCase.Args[0])),
        ["logCombination"] = goldenCase => NumericMath.LogCombination(GoldenReader.Number(goldenCase.Args[0]), GoldenReader.Number(goldenCase.Args[1])),
        ["logGamma"] = goldenCase => SpecialFunctions.LogGamma(GoldenReader.Number(goldenCase.Args[0])),
        ["gammaFn"] = goldenCase => SpecialFunctions.Gamma(GoldenReader.Number(goldenCase.Args[0])),
        ["betaFn"] = goldenCase => SpecialFunctions.Beta(GoldenReader.Number(goldenCase.Args[0]), GoldenReader.Number(goldenCase.Args[1])),
        ["erf"] = goldenCase => SpecialFunctions.Erf(GoldenReader.Number(goldenCase.Args[0])),
        ["erfc"] = goldenCase => SpecialFunctions.Erfc(GoldenReader.Number(goldenCase.Args[0])),
        ["normalPdf"] = goldenCase => SpecialFunctions.NormalPdf(GoldenReader.Number(goldenCase.Args[0])),
        ["normalCdf"] = goldenCase => SpecialFunctions.NormalCdf(GoldenReader.Number(goldenCase.Args[0])),
        ["normalInv"] = goldenCase => SpecialFunctions.NormalInv(GoldenReader.Number(goldenCase.Args[0])),
        ["gammp"] = goldenCase => SpecialFunctions.Gammp(GoldenReader.Number(goldenCase.Args[0]), GoldenReader.Number(goldenCase.Args[1])),
        ["gammq"] = goldenCase => SpecialFunctions.Gammq(GoldenReader.Number(goldenCase.Args[0]), GoldenReader.Number(goldenCase.Args[1])),
        ["betai"] = goldenCase => SpecialFunctions.Betai(GoldenReader.Number(goldenCase.Args[0]), GoldenReader.Number(goldenCase.Args[1]), GoldenReader.Number(goldenCase.Args[2])),
        ["fmt"] = goldenCase => NumberFormat.Fmt(GoldenReader.Number(goldenCase.Args[0]), goldenCase.Count > 1 ? GoldenReader.Int(goldenCase.Args[1]) : 4),
        ["fmtP"] = goldenCase => NumberFormat.FmtP(GoldenReader.Number(goldenCase.Args[0]))
    };

    [Fact]
    public void CoincideConElMotorTypescript()
    {
        GoldenSuite.Run("numeric", Registry, 1e-12);
    }

    [Fact]
    public void LosVectoresCubrenTodasLasFuncionesDelNucleo()
    {
        var document = GoldenDocument.Load("numeric");
        var covered = document.FunctionNames.ToHashSet(StringComparer.Ordinal);
        var expected = new[]
        {
            "sum", "mean", "variance", "sd", "seMean", "sorted", "minOf", "maxOf", "quantile", "median",
            "iqr", "skewness", "kurtosis", "covariance", "correlation", "ranks", "clamp", "logFactorial",
            "logCombination", "logGamma", "gammaFn", "betaFn", "erf", "erfc", "normalPdf", "normalCdf",
            "normalInv", "gammp", "gammq", "betai", "fmt", "fmtP"
        };
        var missing = expected.Where(name => !covered.Contains(name)).ToArray();
        Assert.True(missing.Length == 0, $"Faltan vectores para: {string.Join(", ", missing)}");
        Assert.True(document.Count > 200, $"El vector numérico es demasiado pequeño: {document.Count} casos.");
    }
}
