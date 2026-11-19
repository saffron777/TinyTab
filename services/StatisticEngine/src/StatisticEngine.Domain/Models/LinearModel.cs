using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Models;

/// <summary>Ajuste de un modelo lineal por mínimos cuadrados (ordinarios o ponderados).</summary>
public sealed record LinearFit(
    double[] Coefficients,
    double[] StandardErrors,
    double[] TValues,
    double[] PValues,
    double[][] Covariance,
    double[] Fitted,
    double[] Residuals,
    double R2,
    double R2Adjusted,
    double Sigma,
    double DegreesOfFreedom,
    double Sse,
    double Ssr,
    double Sst,
    double F,
    double FPValue,
    double LogLikelihood,
    double Aic,
    double Bic)
{
    public int N => Fitted.Length;

    public int Parameters => Coefficients.Length;

    /// <summary>Intervalos de confianza al nivel <paramref name="alpha"/>.</summary>
    public (double Low, double High)[] ConfidenceIntervals(double alpha)
    {
        var critical = InferenceDistributions.TQuantile(1 - alpha / 2, DegreesOfFreedom);
        var intervals = new (double Low, double High)[Coefficients.Length];
        for (var index = 0; index < Coefficients.Length; index++)
        {
            intervals[index] = (
                Coefficients[index] - critical * StandardErrors[index],
                Coefficients[index] + critical * StandardErrors[index]);
        }
        return intervals;
    }
}

/// <summary>Mínimos cuadrados, errores clásicos y robustos (HC0/HC1/HC3/HAC) y auxiliares de ajuste.</summary>
public static class LinearModel
{
    public static Result<LinearFit> Fit(
        double[][] x,
        double[] y,
        double[]? weights = null,
        string covariance = "classical",
        int lags = 0)
    {
        var n = x.Length;
        if (n == 0) return Result.Err<LinearFit>(DomainErrorCode.InsufficientData, "No hay observaciones.");
        var p = x[0].Length;
        if (n <= p)
        {
            return Result.Err<LinearFit>(DomainErrorCode.InsufficientData, "Se necesitan más observaciones que parámetros.");
        }
        if (y.Length != n) return Result.Err<LinearFit>(DomainErrorCode.InvalidInput, "La respuesta y el diseño no coinciden.");

        var xtx = new double[p][];
        var xty = new double[p];
        for (var i = 0; i < p; i++)
        {
            xtx[i] = new double[p];
            for (var j = 0; j < p; j++)
            {
                var sum = 0.0;
                for (var row = 0; row < n; row++)
                {
                    var weight = weights is null ? 1.0 : weights[row];
                    sum += weight * x[row][i] * x[row][j];
                }
                xtx[i][j] = sum;
            }
            var sumY = 0.0;
            for (var row = 0; row < n; row++)
            {
                var weight = weights is null ? 1.0 : weights[row];
                sumY += weight * x[row][i] * y[row];
            }
            xty[i] = sumY;
        }

        var beta = LinearAlgebra.SolveLinear(xtx, xty);
        if (beta.IsError) return Result.Err<LinearFit>(beta.Error.Code, beta.Error.Message);
        var inverse = LinearAlgebra.Inverse(xtx);
        if (inverse.IsError) return Result.Err<LinearFit>(inverse.Error.Code, inverse.Error.Message);

        var fitted = new double[n];
        var residuals = new double[n];
        var weightedSse = 0.0;
        for (var row = 0; row < n; row++)
        {
            var acc = 0.0;
            for (var column = 0; column < p; column++) acc += x[row][column] * beta.Value[column];
            fitted[row] = acc;
            residuals[row] = y[row] - acc;
            weightedSse += (weights is null ? 1.0 : weights[row]) * residuals[row] * residuals[row];
        }

        var df = n - p;
        var sigma2 = weightedSse / df;
        var classical = new double[p][];
        for (var i = 0; i < p; i++)
        {
            classical[i] = new double[p];
            for (var j = 0; j < p; j++) classical[i][j] = sigma2 * inverse.Value[i][j];
        }

        var usedCovariance = covariance switch
        {
            "classical" => classical,
            "hc0" => RobustCovariance(x, residuals, inverse.Value, null),
            "hc1" => RobustCovariance(x, residuals, inverse.Value, n / (double)df),
            "hc3" => RobustCovariance(x, residuals, inverse.Value, null, leverage: true),
            "hac" => HacCovariance(x, residuals, inverse.Value, lags, weighted: weights is not null),
            _ => classical
        };

        var standardErrors = new double[p];
        var tValues = new double[p];
        var pValues = new double[p];
        for (var index = 0; index < p; index++)
        {
            standardErrors[index] = Math.Sqrt(Math.Max(0, usedCovariance[index][index]));
            tValues[index] = standardErrors[index] > 0 ? beta.Value[index] / standardErrors[index] : double.NaN;
            pValues[index] = 2 * (1 - InferenceDistributions.TCdf(Math.Abs(tValues[index]), df));
        }

        var meanWeight = 1.0;
        var weightedMean = 0.0;
        if (weights is not null)
        {
            var sumWeights = weights.Sum();
            meanWeight = sumWeights / n;
            for (var row = 0; row < n; row++) weightedMean += weights[row] * y[row];
            weightedMean /= sumWeights;
        }
        else
        {
            weightedMean = NumericMath.Mean(y);
        }
        var sst = 0.0;
        for (var row = 0; row < n; row++)
        {
            var weight = weights is null ? 1.0 : weights[row];
            sst += weight * Math.Pow(y[row] - weightedMean, 2);
        }
        var r2 = sst > 0 ? 1 - weightedSse / sst : 1;
        var r2Adjusted = 1 - (1 - r2) * ((n - 1) / (double)Math.Max(1, df));
        var f = p > 1 && r2 < 1 ? (r2 / (p - 1)) / ((1 - r2) / df) : double.NaN;
        var fP = double.IsFinite(f) ? ExtendedStatistics.FSf(f, p - 1, df) : double.NaN;
        var logLikelihood = -0.5 * n * (Math.Log(2 * Math.PI) + Math.Log(weightedSse / n) + 1);
        _ = meanWeight;

        return Result.Ok(new LinearFit(
            beta.Value,
            standardErrors,
            tValues,
            pValues,
            usedCovariance,
            fitted,
            residuals,
            r2,
            r2Adjusted,
            Math.Sqrt(sigma2),
            df,
            weightedSse,
            sst - weightedSse,
            sst,
            f,
            fP,
            logLikelihood,
            -2 * logLikelihood + 2 * p,
            -2 * logLikelihood + p * Math.Log(n)));
    }

    public static Result<LinearFit> Fit(Design design, double[]? weights = null, string covariance = "classical", int lags = 0) =>
        Fit(design.Matrix, design.Response, weights, covariance, lags);

    private static double[][] RobustCovariance(
        double[][] x,
        double[] residuals,
        double[][] inverse,
        double? multiplier,
        bool leverage = false)
    {
        var n = x.Length;
        var p = x[0].Length;
        var meat = new double[p][];
        for (var i = 0; i < p; i++) meat[i] = new double[p];
        var leverageValues = leverage ? Leverage(x, inverse) : null;
        for (var row = 0; row < n; row++)
        {
            var factor = multiplier ?? 1.0;
            var value = residuals[row] * residuals[row] * factor;
            if (leverageValues is not null)
            {
                var denominator = Math.Max(1e-12, 1 - leverageValues[row]);
                value /= denominator * denominator;
            }
            for (var i = 0; i < p; i++)
            {
                for (var j = 0; j < p; j++) meat[i][j] += value * x[row][i] * x[row][j];
            }
        }
        return Sandwich(inverse, meat);
    }

    private static double[][] HacCovariance(
        double[][] x,
        double[] residuals,
        double[][] inverse,
        int lags,
        bool weighted)
    {
        var n = x.Length;
        var p = x[0].Length;
        var meat = new double[p][];
        for (var i = 0; i < p; i++) meat[i] = new double[p];
        for (var row = 0; row < n; row++)
        {
            var value = residuals[row] * residuals[row];
            for (var i = 0; i < p; i++)
            {
                for (var j = 0; j < p; j++) meat[i][j] += value * x[row][i] * x[row][j];
            }
        }
        _ = weighted;
        for (var lag = 1; lag <= lags && lag < n; lag++)
        {
            var factor = 1 - lag / (lags + 1.0);
            for (var row = lag; row < n; row++)
            {
                var product = residuals[row] * residuals[row - lag] * factor;
                for (var i = 0; i < p; i++)
                {
                    for (var j = 0; j < p; j++)
                    {
                        meat[i][j] += product * (x[row][i] * x[row - lag][j] + x[row - lag][i] * x[row][j]);
                    }
                }
            }
        }
        return Sandwich(inverse, meat);
    }

    private static double[][] Sandwich(double[][] inverse, double[][] meat)
    {
        var left = LinearAlgebra.Multiply(inverse, meat);
        return LinearAlgebra.Multiply(left, inverse);
    }

    private static double[] Leverage(double[][] x, double[][] inverse)
    {
        var n = x.Length;
        var p = x[0].Length;
        var output = new double[n];
        for (var row = 0; row < n; row++)
        {
            var acc = 0.0;
            for (var i = 0; i < p; i++)
            {
                for (var j = 0; j < p; j++) acc += x[row][i] * inverse[i][j] * x[row][j];
            }
            output[row] = acc;
        }
        return output;
    }

    /// <summary>Breusch-Pagan: regresión auxiliar de los residuos al cuadrado sobre el diseño.</summary>
    public static (double Statistic, double PValue) BreuschPagan(double[] residuals, double[][] x)
    {
        var squared = residuals.Select(value => value * value).ToArray();
        var fit = Fit(x, squared);
        if (fit.IsError) return (double.NaN, double.NaN);
        var statistic = fit.Value.N * fit.Value.R2;
        var df = x[0].Length - 1;
        return (statistic, ExtendedStatistics.ChiSquareSf(statistic, df));
    }

    /// <summary>Durbin-Watson.</summary>
    public static double DurbinWatson(double[] residuals)
    {
        var numerator = 0.0;
        for (var index = 1; index < residuals.Length; index++)
        {
            var delta = residuals[index] - residuals[index - 1];
            numerator += delta * delta;
        }
        var denominator = residuals.Sum(value => value * value);
        return denominator > 0 ? numerator / denominator : double.NaN;
    }
}
