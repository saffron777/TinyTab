using StatisticEngine.Domain.Multivariate;
using StatisticEngine.Domain.Tests.Parity;
using Xunit;

namespace StatisticEngine.Domain.Tests.Multivariate;

/// <summary>
/// Paridad del análisis multivariante con el motor TypeScript.
///
/// <para>
/// <b>Excepción documentada para <c>varimax</c> y <c>factorAnalysis</c>:</b> la rotación
/// varimax del motor (<c>multivariate.ts:68-112</c>) no converge. Con la matriz 4×2 del
/// vector dorado recorre las 100 iteraciones completas con un <c>changed</c> final de
/// ~0.32 rad (nunca baja de 1e-8), y perturbar un solo bit de la entrada cambia el
/// resultado 7.4e-5 en la matriz 4×2 y 1.28 en una 3×2. La salida depende, por tanto, de
/// la trayectoria exacta de <c>Math.pow/atan2/cos/sin</c> de V8 y no es reproducible bit a
/// bit desde .NET (además <c>b = (a/h)³</c> en vez de <c>a³/h</c> no es el varimax
/// estándar: de ahí que no converja). Se verifica con las invariantes de una rotación
/// ortogonal, que sí son estables: forma, suma de cuadrados por fila, suma total y
/// productos escalares entre filas. <c>factorAnalysis</c> se valida igual: exacto en
/// <c>labels</c>, <c>eigenvalues</c> y <c>communalities</c> (invariantes), normas por fila
/// de <c>loadings</c> y suma total de <c>varianceExplained</c>.
/// </para>
///
/// El resto del módulo (<c>standardize</c>, <c>pca</c>, <c>kmeans</c>, <c>lda</c>) se compara
/// de forma exacta contra el vector dorado.
/// </summary>
public class MultivariateParityTests
{
    private static readonly Dictionary<string, Func<GoldenCase, object?>> Registry = new(StringComparer.Ordinal)
    {
        ["standardize"] = goldenCase => Pca.Standardize(GoldenReader.Matrix(goldenCase.Args[0])),
        ["pca"] = goldenCase => ResultDto.From(Pca.Analyze(
            GoldenReader.Matrix(goldenCase.Args[0]),
            GoldenReader.Texts(goldenCase.Args[1]))),
        // varimax y factorAnalysis se validan aparte por invariantes (ver el comentario del tipo).
        ["varimax"] = goldenCase => Varimax.Rotate(GoldenReader.Matrix(goldenCase.Args[0])),
        ["factorAnalysis"] = goldenCase => ResultDto.From(FactorAnalysis.Analyze(
            GoldenReader.Matrix(goldenCase.Args[0]),
            GoldenReader.Texts(goldenCase.Args[1]),
            OptionalInt(goldenCase, 2) ?? 2)),
        ["kmeans"] = goldenCase => ResultDto.From(KMeans.Cluster(
            GoldenReader.Matrix(goldenCase.Args[0]),
            GoldenReader.Int(goldenCase.Args[1]),
            OptionalSeed(goldenCase, 2))),
        ["lda"] = goldenCase => ResultDto.From(Lda.Fit(
            GoldenReader.Matrix(goldenCase.Args[0]),
            GoldenReader.Texts(goldenCase.Args[1])))
    };

    [Fact]
    public void CoincideConElMotorTypescript()
    {
        GoldenSuite.Run("multivariate", Registry, 1e-12, "kmeans", "varimax", "factorAnalysis");
    }

    [Fact]
    public void ElAgrupamientoKMeansCoincideConElMotor()
    {
        GoldenSuite.RunSelected("multivariate", Registry, 1e-9, "kmeans");
    }

    /// <summary>
    /// Varimax se compara por invariantes de rotación ortogonal porque su iteración en el
    /// motor TypeScript es caótica (ver el comentario del tipo).
    /// </summary>
    [Fact]
    public void VarimaxConservaLasInvariantesDeLaRotacion()
    {
        var document = GoldenDocument.Load("multivariate");
        var cases = document.Cases("varimax");
        for (var index = 0; index < cases.Count; index++)
        {
            var goldenCase = cases[index];
            var input = GoldenReader.Matrix(goldenCase.Args[0]);
            var expected = GoldenReader.Matrix(goldenCase.Expected);
            var actual = Varimax.Rotate(input);

            SameShape(expected, actual, $"varimax[{index}]");
            var expectedSquares = RowSquares(expected);
            var actualSquares = RowSquares(actual);
            for (var row = 0; row < expectedSquares.Length; row++)
            {
                Close(expectedSquares[row], actualSquares[row], 1e-12, $"varimax[{index}].fila[{row}].sumaCuadrados");
            }

            Close(TotalSquares(expected), TotalSquares(actual), 1e-12, $"varimax[{index}].sumaTotalCuadrados");

            // Una rotación por la derecha conserva L·Lᵀ, es decir, los productos escalares entre filas.
            for (var a = 0; a < expected.Length; a++)
            {
                for (var c = a + 1; c < expected.Length; c++)
                {
                    Close(
                        RowDot(expected, a, c),
                        RowDot(actual, a, c),
                        1e-12,
                        $"varimax[{index}].productoEscalar[{a},{c}]");
                }
            }
        }
    }

    /// <summary>
    /// FactorAnalysis se compara en lo invariante: etiquetas, autovalores, comunalidades,
    /// normas por fila de las cargas y suma total de la varianza explicada.
    /// </summary>
    [Fact]
    public void FactorAnalysisConservaLasInvariantesDeLaRotacion()
    {
        var document = GoldenDocument.Load("multivariate");
        var cases = document.Cases("factorAnalysis");
        for (var index = 0; index < cases.Count; index++)
        {
            var goldenCase = cases[index];
            var rows = GoldenReader.Matrix(goldenCase.Args[0]);
            var labels = GoldenReader.Texts(goldenCase.Args[1]);
            var factors = OptionalInt(goldenCase, 2) ?? 2;
            var actual = FactorAnalysis.Analyze(rows, labels, factors);
            var expected = goldenCase.Expected;

            if (!expected.GetProperty("ok").GetBoolean())
            {
                Assert.True(actual.IsError, $"factorAnalysis[{index}]: se esperaba un error y se obtuvo un resultado.");
                var error = expected.GetProperty("error");
                ParityAssert.Matches(error.GetProperty("code"), actual.Error.WireCode, 0, $"factorAnalysis[{index}].error.code");
                ParityAssert.Matches(error.GetProperty("message"), actual.Error.Message, 0, $"factorAnalysis[{index}].error.message");
                continue;
            }

            Assert.True(actual.IsOk, $"factorAnalysis[{index}]: se esperaba un resultado y se obtuvo un error.");
            var result = actual.Value;
            var value = expected.GetProperty("value");

            ParityAssert.Matches(value.GetProperty("labels"), result.Labels, 1e-12, $"factorAnalysis[{index}].labels");
            ParityAssert.Matches(value.GetProperty("eigenvalues"), result.Eigenvalues, 1e-12, $"factorAnalysis[{index}].eigenvalues");
            // Las comunalidades son la suma de cuadrados de cada fila de cargas: invariantes bajo rotación.
            ParityAssert.Matches(value.GetProperty("communalities"), result.Communalities, 1e-12, $"factorAnalysis[{index}].communalities");

            var expectedLoadings = GoldenReader.Matrix(value.GetProperty("loadings"));
            SameShape(expectedLoadings, result.Loadings, $"factorAnalysis[{index}].loadings");
            var expectedSquares = RowSquares(expectedLoadings);
            var actualSquares = RowSquares(result.Loadings);
            for (var row = 0; row < expectedSquares.Length; row++)
            {
                Close(expectedSquares[row], actualSquares[row], 1e-12, $"factorAnalysis[{index}].loadings.fila[{row}].sumaCuadrados");
            }

            var expectedVariance = GoldenReader.Numbers(value.GetProperty("varianceExplained"));
            var actualVariance = result.VarianceExplained;
            Assert.True(
                actualVariance.Length == expectedVariance.Length,
                $"factorAnalysis[{index}].varianceExplained: se esperaban {expectedVariance.Length} y se obtuvieron {actualVariance.Length}");
            var expectedVarianceTotal = 0.0;
            var actualVarianceTotal = 0.0;
            for (var j = 0; j < expectedVariance.Length && j < actualVariance.Length; j++)
            {
                expectedVarianceTotal += expectedVariance[j];
                actualVarianceTotal += actualVariance[j];
            }
            Close(expectedVarianceTotal, actualVarianceTotal, 1e-12, $"factorAnalysis[{index}].varianceExplained.total");
        }
    }

    private static bool Present(GoldenCase goldenCase, int index) =>
        index < goldenCase.Count && !GoldenReader.IsUndefined(goldenCase.Args[index]);

    private static int? OptionalInt(GoldenCase goldenCase, int index) =>
        Present(goldenCase, index) ? GoldenReader.Int(goldenCase.Args[index]) : null;

    private static uint OptionalSeed(GoldenCase goldenCase, int index) =>
        Present(goldenCase, index) ? (uint)GoldenReader.Number(goldenCase.Args[index]) : 20261023u;

    private static void SameShape(double[][] expected, double[][] actual, string path)
    {
        Assert.True(actual.Length == expected.Length, $"{path}: se esperaban {expected.Length} filas y se obtuvieron {actual.Length}");
        var rows = Math.Min(actual.Length, expected.Length);
        for (var row = 0; row < rows; row++)
        {
            Assert.True(
                actual[row].Length == expected[row].Length,
                $"{path}[{row}]: se esperaban {expected[row].Length} columnas y se obtuvieron {actual[row].Length}");
        }
    }

    private static double[] RowSquares(double[][] matrix)
    {
        var output = new double[matrix.Length];
        for (var i = 0; i < matrix.Length; i++)
        {
            var sum = 0.0;
            for (var j = 0; j < matrix[i].Length; j++) sum += matrix[i][j] * matrix[i][j];
            output[i] = sum;
        }
        return output;
    }

    private static double TotalSquares(double[][] matrix)
    {
        var total = 0.0;
        for (var i = 0; i < matrix.Length; i++)
        {
            for (var j = 0; j < matrix[i].Length; j++) total += matrix[i][j] * matrix[i][j];
        }
        return total;
    }

    private static double RowDot(double[][] matrix, int first, int second)
    {
        var dot = 0.0;
        var length = Math.Min(matrix[first].Length, matrix[second].Length);
        for (var j = 0; j < length; j++) dot += matrix[first][j] * matrix[second][j];
        return dot;
    }

    private static void Close(double expected, double actual, double tolerance, string path)
    {
        if (double.IsNaN(expected) || double.IsNaN(actual))
        {
            Assert.True(double.IsNaN(expected) && double.IsNaN(actual), $"{path}: se esperaba {expected:R} pero se obtuvo {actual:R}");
            return;
        }
        var scale = Math.Max(1.0, Math.Max(Math.Abs(expected), Math.Abs(actual)));
        if (Math.Abs(expected - actual) > tolerance * scale)
        {
            Assert.Fail($"{path}: se esperaba {expected:R} pero se obtuvo {actual:R}");
        }
    }
}
