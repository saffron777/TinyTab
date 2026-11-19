using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Models;

public enum GlmFamily
{
    Gaussian,
    Binomial,
    Poisson,
    Gamma,
    InverseGaussian,
    NegativeBinomial
}

public enum GlmLink
{
    Identity,
    Log,
    Logit,
    Probit,
    CLogLog,
    InversePower,
    InverseSquared,
    Sqrt
}

/// <summary>Ajuste de un modelo lineal generalizado por IRLS.</summary>
public sealed record GlmFit(
    double[] Coefficients,
    double[] StandardErrors,
    double[] ZValues,
    double[] PValues,
    double[] Fitted,
    double[] PearsonResiduals,
    double[] DevianceResiduals,
    double Deviance,
    double NullDeviance,
    double PearsonChi2,
    double Scale,
    double LogLikelihood,
    double Aic,
    int N,
    int Parameters)
{
    public (double Low, double High)[] ConfidenceIntervals(double alpha)
    {
        var critical = SpecialFunctions.NormalInv(1 - alpha / 2);
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

/// <summary>GLM con familias y ligas de statsmodels, suficiente para Poisson/binomial/gamma.</summary>
public static class GlmModel
{
    public static Result<GlmFit> Fit(
        double[][] x,
        double[] y,
        GlmFamily family,
        GlmLink link,
        double[]? offset = null,
        double negativeBinomialAlpha = 1.0,
        bool pearsonScale = false)
    {
        var n = x.Length;
        if (n == 0) return Result.Err<GlmFit>(DomainErrorCode.InsufficientData, "No hay observaciones.");
        var p = x[0].Length;
        if (n <= p) return Result.Err<GlmFit>(DomainErrorCode.InsufficientData, "Se necesitan más observaciones que parámetros.");

        var beta = new double[p];
        var eta = new double[n];
        var mu = new double[n];
        for (var row = 0; row < n; row++)
        {
            eta[row] = offset?[row] ?? 0;
            mu[row] = InitialMean(y[row], family);
        }
        // Valores iniciales por mínimos cuadrados sobre la respuesta transformada.
        var startTarget = new double[n];
        for (var row = 0; row < n; row++) startTarget[row] = Link(mu[row], link) - (offset?[row] ?? 0);
        var start = LinearModel.Fit(x, startTarget);
        if (start.IsOk) beta = start.Value.Coefficients;

        var deviance = double.NaN;
        for (var iteration = 0; iteration < 200; iteration++)
        {
            for (var row = 0; row < n; row++)
            {
                eta[row] = (offset?[row] ?? 0);
                for (var column = 0; column < p; column++) eta[row] += x[row][column] * beta[column];
                mu[row] = InverseLink(eta[row], link, family);
            }

            var w = new double[n];
            var z = new double[n];
            for (var row = 0; row < n; row++)
            {
                var variance = Variance(mu[row], family, negativeBinomialAlpha);
                var derivative = LinkDerivative(mu[row], link);
                var weight = Math.Max(1e-12, 1 / (variance * derivative * derivative));
                w[row] = weight;
                z[row] = eta[row] + (y[row] - mu[row]) * derivative;
            }

            var fit = LinearModel.Fit(x, z, w);
            if (fit.IsError) return Result.Err<GlmFit>(fit.Error.Code, fit.Error.Message);
            beta = fit.Value.Coefficients;

            var next = Deviance(y, mu, family);
            if (double.IsFinite(deviance) && Math.Abs(next - deviance) <= 1e-10 * Math.Max(1, Math.Abs(next))) break;
            deviance = next;
        }

        for (var row = 0; row < n; row++)
        {
            eta[row] = (offset?[row] ?? 0);
            for (var column = 0; column < p; column++) eta[row] += x[row][column] * beta[column];
            mu[row] = InverseLink(eta[row], link, family);
        }

        var finalW = new double[n];
        for (var row = 0; row < n; row++)
        {
            var derivative = LinkDerivative(mu[row], link);
            finalW[row] = Math.Max(1e-12, 1 / (Variance(mu[row], family, negativeBinomialAlpha) * derivative * derivative));
        }
        var xtwx = new double[p][];
        for (var i = 0; i < p; i++) xtwx[i] = new double[p];
        for (var row = 0; row < n; row++)
        {
            for (var i = 0; i < p; i++)
            {
                for (var j = 0; j < p; j++) xtwx[i][j] += finalW[row] * x[row][i] * x[row][j];
            }
        }
        var covarianceResult = LinearAlgebra.Inverse(xtwx);
        if (covarianceResult.IsError)
        {
            return Result.Err<GlmFit>(covarianceResult.Error.Code, covarianceResult.Error.Message);
        }
        var covariance = covarianceResult.Value;

        var devianceFinal = Deviance(y, mu, family);
        var mean = y.Average();
        var nullMu = Enumerable.Repeat(mean, n).ToArray();
        var nullDeviance = Deviance(y, nullMu, family);

        var pearsonChi2 = 0.0;
        var pearson = new double[n];
        var devianceResiduals = new double[n];
        for (var row = 0; row < n; row++)
        {
            var variance = Variance(mu[row], family, negativeBinomialAlpha);
            var residual = (y[row] - mu[row]) / Math.Sqrt(Math.Max(1e-300, variance));
            pearson[row] = residual;
            pearsonChi2 += residual * residual;
            devianceResiduals[row] = Math.Sign(y[row] - mu[row]) * Math.Sqrt(Math.Max(0, UnitDeviance(y[row], mu[row], family)));
        }

        var scale = family is GlmFamily.Binomial or GlmFamily.Poisson
            ? pearsonScale ? pearsonChi2 / Math.Max(1, n - p) : 1.0
            : pearsonChi2 / Math.Max(1, n - p);
        var standardErrors = new double[p];
        for (var index = 0; index < p; index++)
        {
            standardErrors[index] = Math.Sqrt(Math.Max(0, covariance[index][index] * scale));
        }
        var zValues = new double[p];
        var pValues = new double[p];
        for (var index = 0; index < p; index++)
        {
            zValues[index] = standardErrors[index] > 0 ? beta[index] / standardErrors[index] : double.NaN;
            pValues[index] = 2 * (1 - SpecialFunctions.NormalCdf(Math.Abs(zValues[index])));
        }

        var logLikelihood = LogLikelihood(y, mu, family, scale);
        return Result.Ok(new GlmFit(
            beta,
            standardErrors,
            zValues,
            pValues,
            mu,
            pearson,
            devianceResiduals,
            devianceFinal,
            nullDeviance,
            pearsonChi2,
            scale,
            logLikelihood,
            -2 * logLikelihood + 2 * p,
            n,
            p));
    }

    private static double InitialMean(double y, GlmFamily family) =>
        family switch
        {
            GlmFamily.Binomial => Math.Clamp((y + 0.5) / 2, 1e-6, 1 - 1e-6),
            GlmFamily.Poisson or GlmFamily.Gamma or GlmFamily.InverseGaussian or GlmFamily.NegativeBinomial => Math.Max(y, 0.1),
            _ => y
        };

    public static double Link(double mu, GlmLink link) =>
        link switch
        {
            GlmLink.Identity => mu,
            GlmLink.Log => Math.Log(mu),
            GlmLink.Logit => Math.Log(mu / (1 - mu)),
            GlmLink.Probit => SpecialFunctions.NormalInv(Math.Clamp(mu, 1e-12, 1 - 1e-12)),
            GlmLink.CLogLog => Math.Log(-Math.Log(1 - mu)),
            GlmLink.InversePower => 1 / mu,
            GlmLink.InverseSquared => 1 / (mu * mu),
            GlmLink.Sqrt => Math.Sqrt(mu),
            _ => mu
        };

    public static double InverseLink(double eta, GlmLink link, GlmFamily family) =>
        link switch
        {
            GlmLink.Identity => eta,
            GlmLink.Log => Math.Exp(eta),
            GlmLink.Logit => 1 / (1 + Math.Exp(-eta)),
            GlmLink.Probit => SpecialFunctions.NormalCdf(eta),
            GlmLink.CLogLog => 1 - Math.Exp(-Math.Exp(eta)),
            GlmLink.InversePower => 1 / eta,
            GlmLink.InverseSquared => 1 / Math.Sqrt(eta),
            GlmLink.Sqrt => eta * eta,
            _ => eta
        };

    public static double LinkDerivative(double mu, GlmLink link) =>
        link switch
        {
            GlmLink.Identity => 1,
            GlmLink.Log => 1 / mu,
            GlmLink.Logit => 1 / (mu * (1 - mu)),
            GlmLink.Probit => Math.Sqrt(2 * Math.PI) / Math.Exp(-0.5 * Math.Pow(SpecialFunctions.NormalInv(Math.Clamp(mu, 1e-12, 1 - 1e-12)), 2)),
            GlmLink.CLogLog => 1 / ((1 - mu) * Math.Log(1 - mu)),
            GlmLink.InversePower => -1 / (mu * mu),
            GlmLink.InverseSquared => -2 / (mu * mu * mu),
            GlmLink.Sqrt => 1 / (2 * Math.Sqrt(mu)),
            _ => 1
        };

    private static double Variance(double mu, GlmFamily family, double alpha) =>
        family switch
        {
            GlmFamily.Poisson => mu,
            GlmFamily.Binomial => mu * (1 - mu),
            GlmFamily.Gamma => mu * mu,
            GlmFamily.InverseGaussian => mu * mu * mu,
            GlmFamily.NegativeBinomial => mu + alpha * mu * mu,
            _ => 1
        };

    public static double UnitDeviance(double y, double mu, GlmFamily family)
    {
        switch (family)
        {
            case GlmFamily.Gaussian:
                return Math.Pow(y - mu, 2);
            case GlmFamily.Binomial:
            {
                var first = y <= 0 ? 0 : y * Math.Log(y / mu);
                var second = y >= 1 ? 0 : (1 - y) * Math.Log((1 - y) / (1 - mu));
                return 2 * (first + second);
            }
            case GlmFamily.Poisson:
                return 2 * (y <= 0 ? mu : y * Math.Log(y / mu) - (y - mu));
            case GlmFamily.Gamma:
                return 2 * (-Math.Log(y / mu) + (y - mu) / mu);
            case GlmFamily.InverseGaussian:
                return Math.Pow(y - mu, 2) / (y * mu * mu);
            default:
                return Math.Pow(y - mu, 2);
        }
    }

    private static double Deviance(double[] y, double[] mu, GlmFamily family)
    {
        var total = 0.0;
        for (var index = 0; index < y.Length; index++) total += UnitDeviance(y[index], mu[index], family);
        return total;
    }

    private static double LogLikelihood(double[] y, double[] mu, GlmFamily family, double scale)
    {
        var total = 0.0;
        for (var index = 0; index < y.Length; index++)
        {
            total += family switch
            {
                GlmFamily.Poisson => y[index] * Math.Log(mu[index]) - mu[index] - SpecialFunctions.LogGamma(y[index] + 1),
                GlmFamily.Binomial => y[index] * Math.Log(mu[index]) + (1 - y[index]) * Math.Log(1 - mu[index]),
                _ => -0.5 * (Math.Log(2 * Math.PI * scale) + Math.Pow(y[index] - mu[index], 2) / scale)
            };
        }
        return total;
    }
}
