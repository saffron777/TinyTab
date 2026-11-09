using System.Text.Json;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Tests.Parity;
using Xunit;

namespace StatisticEngine.Domain.Tests.Models;

/// <summary>Paridad de los modelos de regresión (<c>regression.ts</c>).</summary>
public class RegressionParityTests
{
    private static readonly Dictionary<string, Func<GoldenCase, object?>> Registry = new(StringComparer.Ordinal)
    {
        ["bestSubsets"] = goldenCase => ResultDto.From(BestSubsetSearch.BestSubsets(
            Input(goldenCase.Args[0]),
            goldenCase.Count > 1 ? GoldenReader.Int(goldenCase.Args[1]) : 4)),
        ["durbinWatson"] = goldenCase => Regression.DurbinWatson(GoldenReader.Numbers(goldenCase.Args[0])),
        ["logisticRegression"] = goldenCase => ResultDto.From(Logistic.LogisticRegression(
            GoldenReader.Numbers(goldenCase.Args[0]),
            GoldenReader.Matrix(goldenCase.Args[1]),
            GoldenReader.Texts(goldenCase.Args[2]))),
        ["multipleRegression"] = goldenCase => ResultDto.From(Regression.MultipleRegression(Input(goldenCase.Args[0]))),
        ["poissonRegression"] = goldenCase => ResultDto.From(Poisson.PoissonRegression(
            GoldenReader.Numbers(goldenCase.Args[0]),
            GoldenReader.Matrix(goldenCase.Args[1]),
            GoldenReader.Texts(goldenCase.Args[2]))),
        ["stepwiseRegression"] = goldenCase => ResultDto.From(Stepwise.StepwiseRegression(
            Input(goldenCase.Args[0]),
            goldenCase.Count > 1 ? GoldenReader.Text(goldenCase.Args[1]) : "forward"))
    };

    [Fact]
    public void CoincideConElMotorTypescript() => GoldenSuite.Run("regression", Registry, 1e-12);

    private static RegressionInput Input(JsonElement element) => new(
        GoldenReader.Numbers(element.GetProperty("response")),
        GoldenReader.Matrix(element.GetProperty("predictors")),
        element.GetProperty("names").EnumerateArray().Select(GoldenReader.Text).ToArray());
}
