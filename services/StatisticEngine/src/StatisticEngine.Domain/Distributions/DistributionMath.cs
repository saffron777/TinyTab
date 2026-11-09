using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Distributions;

/// <summary>
/// Auxiliares compartidos por las 21 distribuciones (paridad 1:1 con los helpers privados de
/// <c>distributions.ts</c>): validaciones, inversión genérica y log-PMF.
/// </summary>
internal static class DistributionMath
{
    /// <summary><c>finite(p, [key])</c>: el parámetro debe ser finito.</summary>
    public static Result<Unit> Finite(DistParams p, string key) =>
        JsMath.IsFinite(p[key])
            ? Result.Ok(Unit.Value)
            : Result<Unit>.Err(DomainErrorCode.InvalidParameter, $"El parámetro \"{key}\" no es válido.");

    /// <summary><c>positive(p, [key])</c>: el parámetro debe ser estrictamente positivo.</summary>
    public static Result<Unit> Positive(DistParams p, string key) =>
        p[key] > 0
            ? Result.Ok(Unit.Value)
            : Result<Unit>.Err(DomainErrorCode.InvalidParameter, $"El parámetro \"{key}\" debe ser mayor que cero.");

    /// <summary><c>positive(p, [first, second])</c>: valida en orden y devuelve el primer error.</summary>
    public static Result<Unit> Positive(DistParams p, string first, string second)
    {
        var result = Positive(p, first);
        return result.IsOk ? Positive(p, second) : result;
    }

    /// <summary>
    /// <c>invert(cdf, q, lower, upper)</c>: inversión de una CDF monótona expandiendo el límite
    /// y biseccionando. Reproduce el mismo criterio de parada que el motor.
    /// </summary>
    public static double Invert(Func<double, double> cdf, double q, double lower, double upper)
    {
        if (!(q > 0)) return lower;
        if (q >= 1) return upper;
        var lo = lower;
        var hi = upper;
        if (!double.IsFinite(hi))
        {
            hi = JsMath.Max(1, double.IsFinite(lo) ? lo + 1 : 1);
            var guard = 0;
            while (cdf(hi) < q && guard < 200)
            {
                hi = hi * 2 + 1;
                guard += 1;
            }
        }
        if (!double.IsFinite(lo))
        {
            lo = -1;
            var guard = 0;
            while (cdf(lo) > q && guard < 200)
            {
                lo = lo * 2 - 1;
                guard += 1;
            }
        }
        return JsMath.BisectInverse(cdf, q, lo, hi);
    }

    /// <summary>
    /// <c>discreteInvert(cdf, q, lower, upper)</c>: inversión por búsqueda binaria sobre enteros.
    /// </summary>
    public static double DiscreteInvert(Func<double, double> cdf, double q, double lower, double upper)
    {
        var lo = JsMath.Max(0, Math.Floor(lower));
        var bounded = double.IsFinite(upper);
        if (!(q > 0)) return lo;
        if (q >= 1) return bounded ? Math.Floor(upper) : lo;
        var target = JsMath.Min(q, 1 - 1e-12);
        var hi = bounded ? Math.Floor(upper) : JsMath.Max(lo + 10, 1);
        var guard = 0;
        while (cdf(hi) < target && guard < 200)
        {
            hi = Math.Ceiling(hi * 2 + 1);
            guard += 1;
        }
        var a = lo;
        var b = hi;
        while (a < b)
        {
            var mid = Math.Floor((a + b) / 2);
            if (cdf(mid) >= target) b = mid;
            else a = mid + 1;
        }
        return a;
    }

    /// <summary><c>logPmfBinomial(n, k, p)</c> del motor.</summary>
    public static double LogPmfBinomial(double n, double k, double p) =>
        NumericMath.LogCombination(n, k) + k * Math.Log(p) + (n - k) * Math.Log(1 - p);

    /// <summary><c>logPmfPoisson(lambda, k)</c> del motor.</summary>
    public static double LogPmfPoisson(double lambda, double k) =>
        -lambda + k * Math.Log(lambda) - SpecialFunctions.LogGamma(k + 1);
}
