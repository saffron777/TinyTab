namespace StatisticEngine.Domain.Numerics;

public sealed record LeastSquaresResult(
    double[] Coefficients,
    double[] Fitted,
    double[] Residuals,
    double RSquared,
    double AdjustedRSquared,
    double StandardError,
    int DegreesOfFreedom);

public sealed record EigenResult(double[] Values, double[][] Vectors);

/// <summary>
/// Álgebra lineal numérica propia, idéntica a <c>linalg.ts</c>: eliminación gaussiana con
/// pivoteo parcial, QR de Householder para mínimos cuadrados y Jacobi para eigenvalores.
/// </summary>
public static class LinearAlgebra
{
    public static double[][] Zeros(int rows, int cols)
    {
        var output = new double[rows][];
        for (var i = 0; i < rows; i++) output[i] = new double[cols];
        return output;
    }

    public static double[][] Identity(int n)
    {
        var output = Zeros(n, n);
        for (var i = 0; i < n; i++) output[i][i] = 1;
        return output;
    }

    public static double[][] Transpose(double[][] a)
    {
        var rows = a.Length;
        var cols = rows > 0 ? a[0].Length : 0;
        var output = Zeros(cols, rows);
        for (var j = 0; j < cols; j++)
        {
            for (var i = 0; i < rows; i++) output[j][i] = a[i][j];
        }
        return output;
    }

    public static double[][] Multiply(double[][] a, double[][] b)
    {
        var n = a.Length;
        var m = b.Length > 0 ? b[0].Length : 0;
        var inner = b.Length;
        var output = Zeros(n, m);
        for (var i = 0; i < n; i++)
        {
            var rowA = a[i];
            var rowOut = output[i];
            for (var k = 0; k < inner; k++)
            {
                var aik = rowA[k];
                if (aik == 0) continue;
                var rowB = b[k];
                for (var j = 0; j < m; j++) rowOut[j] += aik * rowB[j];
            }
        }
        return output;
    }

    public static double[] MatVec(double[][] a, IReadOnlyList<double> v)
    {
        var output = new double[a.Length];
        for (var i = 0; i < a.Length; i++)
        {
            var row = a[i];
            var acc = 0.0;
            for (var j = 0; j < row.Length; j++) acc += row[j] * v[j];
            output[i] = acc;
        }
        return output;
    }

    /// <summary>Resolución de A·x = b por eliminación gaussiana con pivoteo parcial.</summary>
    public static Result<double[]> SolveLinear(double[][] a, IReadOnlyList<double> b)
    {
        var n = a.Length;
        var square = n > 0;
        for (var i = 0; i < n && square; i++) square = a[i].Length == n;
        if (!square || b.Count != n)
        {
            return Result<double[]>.Err(DomainErrorCode.InvalidInput, "La matriz debe ser cuadrada y coincidir con el vector.");
        }

        var m = new double[n][];
        for (var i = 0; i < n; i++) m[i] = (double[])a[i].Clone();
        var x = new double[n];
        for (var i = 0; i < n; i++) x[i] = b[i];

        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var row = col + 1; row < n; row++)
            {
                if (Math.Abs(m[row][col]) > Math.Abs(m[pivot][col])) pivot = row;
            }
            var pivotValue = m[pivot][col];
            if (Math.Abs(pivotValue) < 1e-14)
            {
                return Result<double[]>.Err(DomainErrorCode.SingularMatrix, "La matriz es singular o casi singular.");
            }
            if (pivot != col)
            {
                (m[pivot], m[col]) = (m[col], m[pivot]);
                (x[pivot], x[col]) = (x[col], x[pivot]);
            }
            var rowCol = m[col];
            for (var row = col + 1; row < n; row++)
            {
                var factor = m[row][col] / rowCol[col];
                if (factor == 0) continue;
                var target = m[row];
                for (var j = col; j < n; j++) target[j] -= factor * rowCol[j];
                x[row] -= factor * x[col];
            }
        }

        var solution = new double[n];
        for (var row = n - 1; row >= 0; row--)
        {
            var acc = x[row];
            var rowValues = m[row];
            for (var j = row + 1; j < n; j++) acc -= rowValues[j] * solution[j];
            solution[row] = acc / rowValues[row];
            if (!double.IsFinite(solution[row]))
            {
                return Result<double[]>.Err(DomainErrorCode.SingularMatrix, "La matriz es singular.");
            }
        }
        return Result<double[]>.Ok(solution);
    }

    public static Result<double[][]> Inverse(double[][] a)
    {
        var n = a.Length;
        var output = Zeros(n, n);
        for (var col = 0; col < n; col++)
        {
            var unit = new double[n];
            unit[col] = 1;
            var solved = SolveLinear(a, unit);
            if (solved.IsError) return Result<double[][]>.Err(solved.Error.Code, solved.Error.Message);
            var solution = solved.Value;
            for (var row = 0; row < n; row++) output[row][col] = solution[row];
        }
        return Result<double[][]>.Ok(output);
    }

    public static double Determinant(double[][] a)
    {
        var n = a.Length;
        var m = new double[n][];
        for (var i = 0; i < n; i++) m[i] = (double[])a[i].Clone();
        var det = 1.0;
        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var row = col + 1; row < n; row++)
            {
                if (Math.Abs(m[row][col]) > Math.Abs(m[pivot][col])) pivot = row;
            }
            var pivotValue = m[pivot][col];
            if (Math.Abs(pivotValue) < 1e-14) return 0;
            if (pivot != col)
            {
                (m[pivot], m[col]) = (m[col], m[pivot]);
                det = -det;
            }
            det *= pivotValue;
            var rowCol = m[col];
            for (var row = col + 1; row < n; row++)
            {
                var factor = m[row][col] / pivotValue;
                var target = m[row];
                for (var j = col; j < n; j++) target[j] -= factor * rowCol[j];
            }
        }
        return det;
    }

    /// <summary>
    /// Mínimos cuadrados por QR con transformaciones de Householder.
    /// <paramref name="x"/> es n×p sin columna de unos: se añade automáticamente.
    /// </summary>
    public static Result<LeastSquaresResult> LeastSquares(double[][] x, IReadOnlyList<double> y)
    {
        var n = x.Length;
        var p = (n > 0 ? x[0].Length : 0) + 1;
        if (n < p)
        {
            return Result<LeastSquaresResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan más observaciones que predictores.");
        }

        var design = new double[n][];
        for (var i = 0; i < n; i++)
        {
            design[i] = new double[p];
            design[i][0] = 1;
            for (var j = 1; j < p; j++) design[i][j] = x[i][j - 1];
        }

        var r = new double[n][];
        for (var i = 0; i < n; i++) r[i] = (double[])design[i].Clone();
        var qtY = new double[n];
        for (var i = 0; i < n; i++) qtY[i] = y[i];

        for (var k = 0; k < p; k++)
        {
            var norm = 0.0;
            for (var i = k; i < n; i++) norm += Math.Pow(r[i][k], 2);
            norm = Math.Sqrt(norm);
            if (norm < 1e-14)
            {
                return Result<LeastSquaresResult>.Err(DomainErrorCode.SingularMatrix, "Hay colinealidad perfecta entre las variables.");
            }
            var alpha = r[k][k] > 0 ? -norm : norm;
            var v = new double[n];
            for (var i = 0; i < n; i++) v[i] = i < k ? 0 : r[i][k];
            v[k] -= alpha;
            var vNorm = 0.0;
            for (var i = k; i < n; i++) vNorm += v[i] * v[i];
            vNorm = Math.Sqrt(vNorm);
            if (vNorm < 1e-14) continue;
            for (var i = k; i < n; i++) v[i] /= vNorm;

            for (var j = k; j < p; j++)
            {
                var dot = 0.0;
                for (var i = k; i < n; i++) dot += v[i] * r[i][j];
                for (var i = k; i < n; i++) r[i][j] -= 2 * dot * v[i];
            }
            var dotY = 0.0;
            for (var i = k; i < n; i++) dotY += v[i] * qtY[i];
            for (var i = k; i < n; i++) qtY[i] -= 2 * dotY * v[i];
        }

        var coefficients = new double[p];
        for (var i = p - 1; i >= 0; i--)
        {
            var acc = qtY[i];
            for (var j = i + 1; j < p; j++) acc -= r[i][j] * coefficients[j];
            var diagonal = r[i][i];
            if (Math.Abs(diagonal) < 1e-14)
            {
                return Result<LeastSquaresResult>.Err(DomainErrorCode.SingularMatrix, "El sistema es singular.");
            }
            coefficients[i] = acc / diagonal;
        }

        var fitted = new double[n];
        for (var i = 0; i < n; i++)
        {
            var acc = 0.0;
            for (var j = 0; j < p; j++) acc += design[i][j] * coefficients[j];
            fitted[i] = acc;
        }

        var residuals = new double[n];
        for (var i = 0; i < n; i++) residuals[i] = y[i] - fitted[i];

        var meanY = 0.0;
        for (var i = 0; i < n; i++) meanY += y[i];
        meanY /= n;

        var sst = 0.0;
        for (var i = 0; i < n; i++)
        {
            var delta = y[i] - meanY;
            sst += delta * delta;
        }
        var sse = 0.0;
        for (var i = 0; i < n; i++) sse += residuals[i] * residuals[i];

        var df = n - p;
        var mse = df > 0 ? sse / df : double.NaN;
        var rSquared = sst > 0 ? 1 - sse / sst : 1;
        return Result<LeastSquaresResult>.Ok(
            new LeastSquaresResult(
                coefficients,
                fitted,
                residuals,
                rSquared,
                1 - (1 - rSquared) * ((n - 1) / (double)Math.Max(1, df)),
                Math.Sqrt(mse),
                df));
    }

    /// <summary>Eigenvalores/vectores de una matriz simétrica por rotaciones de Jacobi.</summary>
    public static EigenResult EigenSymmetric(double[][] a, int maxSweeps = 100)
    {
        var n = a.Length;
        var m = new double[n][];
        for (var i = 0; i < n; i++) m[i] = (double[])a[i].Clone();
        var v = Identity(n);

        for (var sweep = 0; sweep < maxSweeps; sweep++)
        {
            var off = 0.0;
            for (var i = 0; i < n; i++)
            {
                for (var j = i + 1; j < n; j++) off += Math.Pow(m[i][j], 2);
            }
            if (off < 1e-24) break;

            for (var p = 0; p < n - 1; p++)
            {
                for (var q = p + 1; q < n; q++)
                {
                    var apq = m[p][q];
                    if (Math.Abs(apq) < 1e-18) continue;
                    var app = m[p][p];
                    var aqq = m[q][q];
                    var theta = (aqq - app) / (2 * apq);
                    var t = JsMath.Sign(theta == 0 ? 1 : theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    var c = 1 / Math.Sqrt(t * t + 1);
                    var s = t * c;

                    for (var k = 0; k < n; k++)
                    {
                        var mkp = m[k][p];
                        var mkq = m[k][q];
                        m[k][p] = c * mkp - s * mkq;
                        m[k][q] = s * mkp + c * mkq;
                    }
                    for (var k = 0; k < n; k++)
                    {
                        var mpk = m[p][k];
                        var mqk = m[q][k];
                        m[p][k] = c * mpk - s * mqk;
                        m[q][k] = s * mpk + c * mqk;
                    }
                    for (var k = 0; k < n; k++)
                    {
                        var vkp = v[k][p];
                        var vkq = v[k][q];
                        v[k][p] = c * vkp - s * vkq;
                        v[k][q] = s * vkp + c * vkq;
                    }
                }
            }
        }

        var values = new double[n];
        for (var i = 0; i < n; i++) values[i] = m[i][i];
        return new EigenResult(values, v);
    }

    /// <summary>Número de condición aproximado a partir de valores absolutos.</summary>
    public static double ConditionEstimate(IReadOnlyList<double> values)
    {
        double max = 0;
        double min = double.PositiveInfinity;
        var any = false;
        for (var i = 0; i < values.Count; i++)
        {
            var abs = Math.Abs(values[i]);
            if (!(abs > 0)) continue;
            any = true;
            if (abs > max) max = abs;
            if (abs < min) min = abs;
        }
        if (!any) return double.PositiveInfinity;
        return max / min;
    }
}
