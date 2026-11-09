using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.TimeSeries;

/// <summary>Prueba de Ljung-Box (paridad: <c>timeseries.ts:248-263</c>).</summary>
public sealed record LjungBoxResult(double Statistic, int Df, double PValue);

/// <summary>
/// Prueba de autocorrelación conjunta de los residuos. Las funciones gamma incompletas se
/// replican literalmente de <c>timeseries.ts:265-314</c> para conservar los mismos topes de
/// iteración y los mismos pisos numéricos.
/// </summary>
public static class LjungBox
{
    public static LjungBoxResult Compute(IReadOnlyList<double> residuals, int lags = 10)
    {
        var n = residuals.Count;
        var center = NumericMath.Mean(residuals);
        var denominator = 0.0;
        for (var i = 0; i < n; i++)
        {
            var delta = residuals[i] - center;
            denominator += delta * delta;
        }

        var statistic = 0.0;
        var maxLag = Math.Min(lags, n - 1);
        for (var k = 1; k <= maxLag; k++)
        {
            var numerator = 0.0;
            for (var i = k; i < n; i++) numerator += (residuals[i] - center) * (residuals[i - k] - center);
            var rk = numerator / denominator;
            statistic += rk * rk / (n - k);
        }
        statistic *= n * (n + 2.0);
        return new LjungBoxResult(statistic, maxLag, ChiSquareUpper(statistic, maxLag));
    }

    private static double ChiSquareUpper(double statistic, double df) => 1 - LowerGamma(df / 2, statistic / 2);

    /// <summary>P(a,x) con la misma serie que <c>lowerGamma</c> del motor (1000 iteraciones).</summary>
    private static double LowerGamma(double a, double x)
    {
        if (x <= 0) return 0;
        if (x < a + 1)
        {
            var ap = a;
            var deliver = 1 / a;
            var total = deliver;
            for (var n = 0; n < 1000; n++)
            {
                ap += 1;
                deliver *= x / ap;
                total += deliver;
                if (Math.Abs(deliver) < Math.Abs(total) * 1e-16) break;
            }
            return total * Math.Exp(-x + a * Math.Log(x) - SpecialFunctions.LogGamma(a));
        }
        return 1 - UpperGamma(a, x);
    }

    /// <summary>Q(a,x) con la fracción continua del motor (piso 1e-300, tope 1000).</summary>
    private static double UpperGamma(double a, double x)
    {
        var b = x + 1 - a;
        var c = 1e300;
        var d = 1 / b;
        var h = d;
        for (var i = 1; i < 1000; i++)
        {
            var an = -i * (i - a);
            b += 2;
            d = an * d + b;
            if (Math.Abs(d) < 1e-300) d = 1e-300;
            c = b + an / c;
            if (Math.Abs(c) < 1e-300) c = 1e-300;
            d = 1 / d;
            var deliver = d * c;
            h *= deliver;
            if (Math.Abs(deliver - 1) < 1e-16) break;
        }
        return Math.Exp(-x + a * Math.Log(x) - SpecialFunctions.LogGamma(a)) * h;
    }
}
