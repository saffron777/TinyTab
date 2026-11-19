using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Models;

/// <summary>Ajuste de regresión cuantílica por IRLS (port del algoritmo de statsmodels QuantReg).</summary>
public sealed record QuantileFit(
    double[] Coefficients,
    double[] StandardErrors,
    double[] TValues,
    double[] PValues,
    double PseudoR2,
    double Sparsity,
    double Bandwidth,
    int Iterations)
{
    public (double Low, double High)[] ConfidenceIntervals(double alpha, double degreesOfFreedom)
    {
        var critical = InferenceDistributions.TQuantile(1 - alpha / 2, degreesOfFreedom);
        var output = new (double Low, double High)[Coefficients.Length];
        for (var index = 0; index < Coefficients.Length; index++)
        {
            output[index] = (
                Coefficients[index] - critical * StandardErrors[index],
                Coefficients[index] + critical * StandardErrors[index]);
        }
        return output;
    }
}

public static class QuantileRegression
{
    public static QuantileFit Fit(double[][] x, double[] y, double q, double alpha = 0.05)
    {
        var n = x.Length;
        var p = x[0].Length;
        var beta = Enumerable.Repeat(1.0, p).ToArray();
        var xstar = x.Select(row => (double[])row.Clone()).ToArray();
        var difference = 10.0;
        var iterations = 0;
        while (iterations < 1000 && difference > 1e-6)
        {
            iterations++;
            var previous = (double[])beta.Clone();
            var xtx = new double[p][];
            var xty = new double[p];
            for (var i = 0; i < p; i++)
            {
                xtx[i] = new double[p];
                for (var j = 0; j < p; j++)
                {
                    var sum = 0.0;
                    for (var row = 0; row < n; row++) sum += xstar[row][i] * x[row][j];
                    xtx[i][j] = sum;
                }
                var sumY = 0.0;
                for (var row = 0; row < n; row++) sumY += xstar[row][i] * y[row];
                xty[i] = sumY;
            }
            var solved = LinearAlgebra.SolveLinear(xtx, xty);
            if (solved.IsError) break;
            beta = solved.Value;

            var residuals = new double[n];
            for (var row = 0; row < n; row++)
            {
                var fitted = 0.0;
                for (var column = 0; column < p; column++) fitted += x[row][column] * beta[column];
                var residual = y[row] - fitted;
                if (Math.Abs(residual) < 1e-6) residual = (residual >= 0 ? 1 : -1) * 1e-6;
                residuals[row] = Math.Abs(residual < 0 ? q * residual : (1 - q) * residual);
            }
            for (var row = 0; row < n; row++)
            {
                for (var column = 0; column < p; column++) xstar[row][column] = x[row][column] / residuals[row];
            }
            difference = Enumerable.Range(0, p).Max(index => Math.Abs(beta[index] - previous[index]));
        }

        var e = new double[n];
        for (var row = 0; row < n; row++)
        {
            var fitted = 0.0;
            for (var column = 0; column < p; column++) fitted += x[row][column] * beta[column];
            e[row] = y[row] - fitted;
        }

        var iqre = NumericMath.Quantile(e, 0.75) - NumericMath.Quantile(e, 0.25);
        var zq = SpecialFunctions.NormalInv(q);
        var pdf = SpecialFunctions.NormalPdf(zq);
        var hall = Math.Pow(n, -1.0 / 3) * Math.Pow(SpecialFunctions.NormalInv(1 - 0.05 / 2), 2.0 / 3)
                   * Math.Pow(1.5 * pdf * pdf / (2 * zq * zq + 1), 1.0 / 3);
        var bandwidth = Math.Min(NumericMath.Sd(y), iqre / 1.34)
                        * (SpecialFunctions.NormalInv(q + hall) - SpecialFunctions.NormalInv(q - hall));
        var fhat = 0.0;
        for (var row = 0; row < n; row++)
        {
            var u = e[row] / bandwidth;
            if (Math.Abs(u) <= 1) fhat += 0.75 * (1 - u * u);
        }
        fhat /= n * bandwidth;
        var sparsity = 1 / fhat;

        var xtxBase = new double[p][];
        for (var i = 0; i < p; i++)
        {
            xtxBase[i] = new double[p];
            for (var j = 0; j < p; j++)
            {
                var sum = 0.0;
                for (var row = 0; row < n; row++) sum += x[row][i] * x[row][j];
                xtxBase[i][j] = sum;
            }
        }
        var xtxInverse = LinearAlgebra.Inverse(xtxBase);
        var covariance = new double[p][];
        if (xtxInverse.IsOk)
        {
            var d = new double[n];
            for (var row = 0; row < n; row++)
            {
                d[row] = e[row] > 0 ? Math.Pow(q / fhat, 2) : Math.Pow((1 - q) / fhat, 2);
            }
            var middle = new double[p][];
            for (var i = 0; i < p; i++)
            {
                middle[i] = new double[p];
                for (var j = 0; j < p; j++)
                {
                    var sum = 0.0;
                    for (var row = 0; row < n; row++) sum += x[row][i] * d[row] * x[row][j];
                    middle[i][j] = sum;
                }
            }
            var left = LinearAlgebra.Multiply(xtxInverse.Value, middle);
            covariance = LinearAlgebra.Multiply(left, xtxInverse.Value);
        }
        else
        {
            for (var i = 0; i < p; i++) covariance[i] = new double[p];
        }

        var standardErrors = new double[p];
        var tValues = new double[p];
        var pValues = new double[p];
        var df = n - p;
        for (var index = 0; index < p; index++)
        {
            standardErrors[index] = Math.Sqrt(Math.Max(0, covariance[index][index]));
            tValues[index] = standardErrors[index] > 0 ? beta[index] / standardErrors[index] : double.NaN;
            pValues[index] = 2 * (1 - InferenceDistributions.TCdf(Math.Abs(tValues[index]), df));
        }

        var weighted = e.Select(residual => Math.Abs(residual < 0 ? (1 - q) * residual : q * residual)).Sum();
        var quantile = NumericMath.Quantile(y, q);
        var baseline = y.Select(value =>
        {
            var residual = value - quantile;
            return Math.Abs(residual < 0 ? (1 - q) * residual : q * residual);
        }).Sum();
        var pseudo = baseline > 0 ? 1 - weighted / baseline : double.NaN;

        return new QuantileFit(beta, standardErrors, tValues, pValues, pseudo, sparsity, bandwidth, iterations);
    }
}
