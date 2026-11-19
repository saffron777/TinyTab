using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Models;

/// <summary>Regresión ordinal de probabilidades proporcionales (logit/probit) por máxima verosimilitud.</summary>
public sealed record OrdinalFit(
    double[] Coefficients,
    double[] StandardErrors,
    double[] ZValues,
    double[] PValues,
    double[] Thresholds,
    double[][] Probabilities,
    double LogLikelihood,
    double NullLogLikelihood,
    int[] Categories);

public static class OrdinalRegression
{
    public static Result<OrdinalFit> Fit(
        double[][] x,
        int[] categories,
        int levels,
        double[] startThresholds,
        bool probit = false,
        double alpha = 0.05)
    {
        var n = x.Length;
        var k = x[0].Length;
        var parameters = k + levels - 1;
        var beta = new double[k];

        static double Cdf(double value, bool probit) =>
            probit ? SpecialFunctions.NormalCdf(value) : 1 / (1 + Math.Exp(-value));

        double[] Thresholds(double[] theta)
        {
            var values = new double[levels - 1];
            values[0] = theta[k];
            for (var index = 1; index < levels - 1; index++)
            {
                values[index] = values[index - 1] + Math.Exp(theta[k + index]);
            }
            return values;
        }

        double NegativeLogLikelihood(double[] theta)
        {
            var thresholds = Thresholds(theta);
            var total = 0.0;
            for (var row = 0; row < n; row++)
            {
                var linear = 0.0;
                for (var column = 0; column < k; column++) linear += x[row][column] * theta[column];
                var category = categories[row];
                var lower = category == 0 ? double.NegativeInfinity : thresholds[category - 1] - linear;
                var upper = category == levels - 1 ? double.PositiveInfinity : thresholds[category] - linear;
                var probability = (category == levels - 1 ? 1 : Cdf(upper, probit))
                                  - (category == 0 ? 0 : Cdf(lower, probit));
                total -= Math.Log(Math.Max(probability, 1e-20));
            }
            return total;
        }

        var theta = new double[parameters];
        for (var column = 0; column < k; column++) theta[column] = beta[column];
        theta[k] = startThresholds[0];
        for (var index = 1; index < levels - 1; index++)
        {
            theta[k + index] = Math.Log(Math.Max(startThresholds[index] - startThresholds[index - 1], 1e-8));
        }

        var optimized = Optimize(NegativeLogLikelihood, theta);
        if (optimized.IsError) return Result.Err<OrdinalFit>(optimized.Error.Code, optimized.Error.Message);
        var estimate = optimized.Value;

        var probabilities = new double[n][];
        var thresholdsFinal = Thresholds(estimate);
        var fitted = new double[n];
        for (var row = 0; row < n; row++)
        {
            probabilities[row] = new double[levels];
            var linear = 0.0;
            for (var column = 0; column < k; column++) linear += x[row][column] * estimate[column];
            var previous = 0.0;
            for (var category = 0; category < levels; category++)
            {
                var cumulative = category == levels - 1 ? 1.0 : Cdf(thresholdsFinal[category] - linear, probit);
                probabilities[row][category] = Math.Max(0, cumulative - previous);
                previous = cumulative;
            }
            fitted[row] = linear;
        }

        var covariance = NumericalHessian(NegativeLogLikelihood, estimate);
        var inverse = LinearAlgebra.Inverse(covariance);
        if (inverse.IsError) return Result.Err<OrdinalFit>(inverse.Error.Code, inverse.Error.Message);
        var standardErrors = new double[k];
        var zValues = new double[k];
        var pValues = new double[k];
        for (var index = 0; index < k; index++)
        {
            standardErrors[index] = Math.Sqrt(Math.Max(0, inverse.Value[index][index]));
            zValues[index] = standardErrors[index] > 0 ? estimate[index] / standardErrors[index] : double.NaN;
            pValues[index] = 2 * (1 - SpecialFunctions.NormalCdf(Math.Abs(zValues[index])));
        }

        var counts = new int[levels];
        foreach (var category in categories) counts[category]++;
        var nullLogLikelihood = 0.0;
        for (var category = 0; category < levels; category++)
        {
            var share = counts[category] / (double)n;
            if (share > 0) nullLogLikelihood += counts[category] * Math.Log(share);
        }

        return Result.Ok(new OrdinalFit(
            estimate[..k],
            standardErrors,
            zValues,
            pValues,
            thresholdsFinal,
            probabilities,
            -NegativeLogLikelihood(estimate),
            nullLogLikelihood,
            categories));
    }

    private sealed record Optimized(double[] Parameters);

    private static Result<double[]> Optimize(Func<double[], double> objective, double[] start)
    {
        var x = (double[])start.Clone();
        var m = x.Length;
        var inverseHessian = new double[m][];
        for (var i = 0; i < m; i++)
        {
            inverseHessian[i] = new double[m];
            inverseHessian[i][i] = 1;
        }
        var value = objective(x);
        var gradient = Gradient(objective, x);
        for (var iteration = 0; iteration < 1000; iteration++)
        {
            var direction = new double[m];
            for (var i = 0; i < m; i++)
            {
                for (var j = 0; j < m; j++) direction[i] -= inverseHessian[i][j] * gradient[j];
            }
            var slope = 0.0;
            for (var i = 0; i < m; i++) slope += gradient[i] * direction[i];
            if (slope >= 0)
            {
                for (var i = 0; i < m; i++) direction[i] = -gradient[i];
                slope = -gradient.Sum(item => item * item);
            }
            var step = 1.0;
            var candidate = new double[m];
            var candidateValue = value;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                for (var i = 0; i < m; i++) candidate[i] = x[i] + step * direction[i];
                candidateValue = objective(candidate);
                if (candidateValue <= value + 1e-4 * step * slope) break;
                step *= 0.5;
            }
            if (candidateValue > value) break;
            var candidateGradient = Gradient(objective, candidate);
            var s = new double[m];
            var y = new double[m];
            for (var i = 0; i < m; i++)
            {
                s[i] = candidate[i] - x[i];
                y[i] = candidateGradient[i] - gradient[i];
            }
            var rho = 0.0;
            for (var i = 0; i < m; i++) rho += y[i] * s[i];
            if (Math.Abs(rho) > 1e-12)
            {
                rho = 1 / rho;
                var left = new double[m][];
                for (var i = 0; i < m; i++)
                {
                    left[i] = new double[m];
                    for (var j = 0; j < m; j++) left[i][j] = (i == j ? 1 : 0) - rho * s[i] * y[j];
                }
                var right = new double[m][];
                for (var i = 0; i < m; i++)
                {
                    right[i] = new double[m];
                    for (var j = 0; j < m; j++) right[i][j] = (i == j ? 1 : 0) - rho * y[i] * s[j];
                }
                inverseHessian = Multiply(Multiply(left, inverseHessian), right);
                for (var i = 0; i < m; i++)
                {
                    for (var j = 0; j < m; j++) inverseHessian[i][j] += rho * s[i] * s[j];
                }
            }
            x = candidate;
            gradient = candidateGradient;
            var difference = Math.Abs(candidateValue - value);
            value = candidateValue;
            if (gradient.Max(item => Math.Abs(item)) < 1e-7 || difference < 1e-12) break;
            if (iteration == 999) return Result.Err<double[]>(DomainErrorCode.NonConvergence, "El ajuste ordinal no convergió.");
        }
        return Result.Ok(x);
    }

    private static double[] Gradient(Func<double[], double> objective, double[] x)
    {
        var gradient = new double[x.Length];
        for (var i = 0; i < x.Length; i++)
        {
            var step = 1e-5 * Math.Max(1, Math.Abs(x[i]));
            var high = (double[])x.Clone();
            var low = (double[])x.Clone();
            high[i] += step;
            low[i] -= step;
            gradient[i] = (objective(high) - objective(low)) / (2 * step);
        }
        return gradient;
    }

    private static double[][] NumericalHessian(Func<double[], double> objective, double[] x)
    {
        var m = x.Length;
        var hessian = new double[m][];
        for (var i = 0; i < m; i++) hessian[i] = new double[m];
        for (var i = 0; i < m; i++)
        {
            var hi = 1e-4 * Math.Max(1, Math.Abs(x[i]));
            for (var j = i; j < m; j++)
            {
                var hj = 1e-4 * Math.Max(1, Math.Abs(x[j]));
                var fpp = Shift(objective, x, i, hi, j, hj);
                var fpm = Shift(objective, x, i, hi, j, -hj);
                var fmp = Shift(objective, x, i, -hi, j, hj);
                var fmm = Shift(objective, x, i, -hi, j, -hj);
                var value = (fpp - fpm - fmp + fmm) / (4 * hi * hj);
                hessian[i][j] = value;
                hessian[j][i] = value;
            }
        }
        return hessian;
    }

    private static double Shift(Func<double[], double> objective, double[] x, int i, double hi, int j, double hj)
    {
        var candidate = (double[])x.Clone();
        candidate[i] += hi;
        candidate[j] += hj;
        return objective(candidate);
    }

    private static double[][] Multiply(double[][] a, double[][] b)
    {
        var result = new double[a.Length][];
        for (var i = 0; i < a.Length; i++)
        {
            result[i] = new double[b[0].Length];
            for (var j = 0; j < b[0].Length; j++)
            {
                for (var k = 0; k < b.Length; k++) result[i][j] += a[i][k] * b[k][j];
            }
        }
        return result;
    }
}
