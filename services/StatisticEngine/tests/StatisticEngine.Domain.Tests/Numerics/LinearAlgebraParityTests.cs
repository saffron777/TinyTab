using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Tests.Parity;
using Xunit;

namespace StatisticEngine.Domain.Tests.Numerics;

public class LinearAlgebraParityTests
{
    private static readonly Dictionary<string, Func<GoldenCase, object?>> Registry = new(StringComparer.Ordinal)
    {
        ["zeros"] = goldenCase => LinearAlgebra.Zeros(GoldenReader.Int(goldenCase.Args[0]), GoldenReader.Int(goldenCase.Args[1])),
        ["identity"] = goldenCase => LinearAlgebra.Identity(GoldenReader.Int(goldenCase.Args[0])),
        ["transpose"] = goldenCase => LinearAlgebra.Transpose(GoldenReader.Matrix(goldenCase.Args[0])),
        ["multiply"] = goldenCase => LinearAlgebra.Multiply(GoldenReader.Matrix(goldenCase.Args[0]), GoldenReader.Matrix(goldenCase.Args[1])),
        ["matVec"] = goldenCase => LinearAlgebra.MatVec(GoldenReader.Matrix(goldenCase.Args[0]), GoldenReader.Numbers(goldenCase.Args[1])),
        ["solveLinear"] = goldenCase => ResultDto.From(LinearAlgebra.SolveLinear(GoldenReader.Matrix(goldenCase.Args[0]), GoldenReader.Numbers(goldenCase.Args[1]))),
        ["inverse"] = goldenCase => ResultDto.From(LinearAlgebra.Inverse(GoldenReader.Matrix(goldenCase.Args[0]))),
        ["determinant"] = goldenCase => LinearAlgebra.Determinant(GoldenReader.Matrix(goldenCase.Args[0])),
        ["leastSquares"] = goldenCase => ResultDto.From(LinearAlgebra.LeastSquares(GoldenReader.Matrix(goldenCase.Args[0]), GoldenReader.Numbers(goldenCase.Args[1]))),
        ["eigenSymmetric"] = goldenCase => LinearAlgebra.EigenSymmetric(GoldenReader.Matrix(goldenCase.Args[0])),
        ["conditionEstimate"] = goldenCase => LinearAlgebra.ConditionEstimate(GoldenReader.Numbers(goldenCase.Args[0]))
    };

    [Fact]
    public void CoincideConElMotorTypescript()
    {
        GoldenSuite.Run("linalg", Registry, 1e-12);
    }
}
