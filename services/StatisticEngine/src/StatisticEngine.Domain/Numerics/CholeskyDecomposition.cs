using System.Numerics;

namespace StatisticEngine.Domain.Numerics;

/// <summary>
/// Descomposición de Cholesky y utilidades de matrices simétricas definidas positivas que usan los
/// análisis econométricos portados desde Sigma (paridad con <c>numpy.linalg.cholesky</c> y
/// <c>statsmodels.tools.linalg.logdet_symm</c>).
/// </summary>
public static class CholeskyDecomposition
{
    /// <summary>Factor inferior <c>L</c> con <c>A = L·Lᵀ</c>; error si la matriz no es definida positiva.</summary>
    public static Result<double[][]> Lower(double[][] matrix)
    {
        var n = matrix.Length;
        if (n == 0 || matrix[0].Length != n)
        {
            return Result<double[][]>.Err(DomainErrorCode.InvalidInput, "La matriz debe ser cuadrada.");
        }

        var lower = LinearAlgebra.Zeros(n, n);
        for (var row = 0; row < n; row++)
        {
            for (var column = 0; column <= row; column++)
            {
                var sum = matrix[row][column];
                for (var index = 0; index < column; index++) sum -= lower[row][index] * lower[column][index];
                if (row == column)
                {
                    if (!(sum > 0))
                    {
                        return Result<double[][]>.Err(
                            DomainErrorCode.SingularMatrix,
                            "La matriz no es definida positiva.");
                    }
                    lower[row][column] = Math.Sqrt(sum);
                }
                else
                {
                    lower[row][column] = sum / lower[column][column];
                }
            }
        }
        return Result<double[][]>.Ok(lower);
    }

    /// <summary>Logaritmo del determinante por Cholesky (como <c>logdet_symm</c>).</summary>
    public static double LogDeterminant(double[][] matrix) => LogDeterminant(matrix, out _);

    /// <summary>Logaritmo del determinante por Cholesky, devolviendo también el factor inferior.</summary>
    public static double LogDeterminant(double[][] matrix, out double[][]? lower)
    {
        lower = null;
        var factor = Lower(matrix);
        if (factor.IsError) return double.NaN;
        lower = factor.Value;
        var total = 0.0;
        for (var index = 0; index < matrix.Length; index++)
        {
            var diagonal = lower[index][index];
            if (!(diagonal > 0)) return double.NaN;
            total += Math.Log(diagonal);
        }
        return 2 * total;
    }

    /// <summary>Inversa de una matriz triangular inferior.</summary>
    public static Result<double[][]> InverseLower(double[][] lower)
    {
        var n = lower.Length;
        var output = LinearAlgebra.Zeros(n, n);
        for (var column = 0; column < n; column++)
        {
            if (Math.Abs(lower[column][column]) < 1e-14)
            {
                return Result<double[][]>.Err(DomainErrorCode.SingularMatrix, "La matriz es singular.");
            }
            output[column][column] = 1 / lower[column][column];
            for (var row = column + 1; row < n; row++)
            {
                var sum = 0.0;
                for (var index = column; index < row; index++) sum -= lower[row][index] * output[index][column];
                output[row][column] = sum / lower[row][row];
            }
        }
        return Result<double[][]>.Ok(output);
    }
}

/// <summary>
/// Eigenvalores de una matriz real general (no necesariamente simétrica): reducción de Hessenberg y
/// iteración QR con desplazamiento de Wilkinson en aritmética compleja. Se usa para las raíces del
/// polinomio característico de un VAR (paridad con <c>numpy.linalg.eig</c>).
/// </summary>
public static class ComplexEigen
{
    public static Complex[] Eigenvalues(double[][] matrix)
    {
        var n = matrix.Length;
        if (n == 0) return [];
        var a = new Complex[n][];
        for (var row = 0; row < n; row++)
        {
            a[row] = new Complex[n];
            for (var column = 0; column < n && column < matrix[row].Length; column++)
            {
                a[row][column] = matrix[row][column];
            }
        }

        Hessenberg(a, n);
        var values = new Complex[n];
        var m = n;
        var iterations = 0;
        while (m > 1 && iterations < 20000)
        {
            iterations++;
            var start = m - 1;
            while (start > 0 && !Negligible(a, start - 1)) start--;
            if (start == m - 1)
            {
                values[m - 1] = a[m - 1][m - 1];
                m--;
                continue;
            }
            if (start == m - 2)
            {
                var roots = TwoByTwo(a, start);
                values[start] = roots[0];
                values[start + 1] = roots[1];
                m -= 2;
                continue;
            }
            var mu = Shift(a, m);
            if (!Complex.IsFinite(mu)) mu = a[m - 1][m - 1];
            QrStep(a, start, m, n, mu);
        }

        if (m == 1) values[0] = a[0][0];
        return values;
    }

    /// <summary>Módulos de los eigenvalores (necesario para la estabilidad de un VAR).</summary>
    public static double[] Moduli(double[][] matrix) =>
        [.. Eigenvalues(matrix).Select(value => Complex.Abs(value))];

    private static bool Negligible(Complex[][] a, int row)
    {
        var scale = Complex.Abs(a[row][row]) + Complex.Abs(a[row + 1][row + 1]);
        return Complex.Abs(a[row + 1][row]) <= 1e-14 * (scale == 0 ? 1 : scale);
    }

    private static Complex[] TwoByTwo(Complex[][] a, int start)
    {
        Complex p = a[start][start], q = a[start][start + 1], r = a[start + 1][start], s = a[start + 1][start + 1];
        var trace = p + s;
        var det = p * s - q * r;
        var disc = Complex.Sqrt(trace * trace - 4 * det);
        return [(trace + disc) / 2, (trace - disc) / 2];
    }

    private static Complex Shift(Complex[][] a, int m)
    {
        var roots = TwoByTwo(a, m - 2);
        var last = a[m - 1][m - 1];
        return Complex.Abs(roots[0] - last) <= Complex.Abs(roots[1] - last) ? roots[0] : roots[1];
    }

    private static void Hessenberg(Complex[][] a, int n)
    {
        for (var column = 0; column < n - 2; column++)
        {
            var start = column + 1;
            var norm = 0.0;
            for (var row = start; row < n; row++) norm += Complex.Abs(a[row][column]) * Complex.Abs(a[row][column]);
            norm = Math.Sqrt(norm);
            if (norm < 1e-300) continue;

            var v = new Complex[n];
            var head = a[start][column];
            var phase = Complex.Abs(head) < 1e-300 ? Complex.One : head / Complex.Abs(head);
            v[start] = head + phase * norm;
            for (var row = start + 1; row < n; row++) v[row] = a[row][column];

            var vNorm = 0.0;
            for (var row = start; row < n; row++) vNorm += Complex.Abs(v[row]) * Complex.Abs(v[row]);
            vNorm = Math.Sqrt(vNorm);
            if (vNorm < 1e-300) continue;
            for (var row = start; row < n; row++) v[row] /= vNorm;

            for (var j = column; j < n; j++)
            {
                Complex dot = 0;
                for (var row = start; row < n; row++) dot += Complex.Conjugate(v[row]) * a[row][j];
                var factor = 2 * dot;
                for (var row = start; row < n; row++) a[row][j] -= factor * v[row];
            }
            for (var row = 0; row < n; row++)
            {
                Complex dot = 0;
                for (var j = start; j < n; j++) dot += a[row][j] * v[j];
                var factor = 2 * dot;
                for (var j = start; j < n; j++) a[row][j] -= factor * Complex.Conjugate(v[j]);
            }
        }
    }

    /// <summary>Un paso QR implícito sobre el bloque <c>[start, m)</c> con desplazamiento <paramref name="mu"/>.</summary>
    private static void QrStep(Complex[][] a, int start, int m, int n, Complex mu)
    {
        for (var i = start; i < m; i++) a[i][i] -= mu;

        var rotations = new List<(double C, Complex S)>(m - start);
        for (var k = start; k < m - 1; k++)
        {
            var x = a[k][k];
            var y = a[k + 1][k];
            double absX = Complex.Abs(x), absY = Complex.Abs(y);
            var norm = Math.Sqrt(absX * absX + absY * absY);
            if (norm < 1e-300)
            {
                rotations.Add((1.0, Complex.Zero));
                continue;
            }
            double c;
            Complex s;
            if (absX < 1e-300)
            {
                c = 0;
                s = Complex.Conjugate(y) / absY;
            }
            else
            {
                c = absX / norm;
                s = x / absX * Complex.Conjugate(y) / norm;
            }
            rotations.Add((c, s));

            for (var j = k; j < n; j++)
            {
                var first = a[k][j];
                var second = a[k + 1][j];
                a[k][j] = c * first + s * second;
                a[k + 1][j] = -Complex.Conjugate(s) * first + c * second;
            }
        }

        for (var index = 0; index < rotations.Count; index++)
        {
            var k = start + index;
            var (c, s) = rotations[index];
            for (var row = 0; row < n; row++)
            {
                var first = a[row][k];
                var second = a[row][k + 1];
                a[row][k] = c * first + Complex.Conjugate(s) * second;
                a[row][k + 1] = -s * first + c * second;
            }
        }

        for (var i = start; i < m; i++) a[i][i] += mu;
    }
}
