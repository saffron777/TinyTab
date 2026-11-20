using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Pruebas auxiliares de los ANOVA avanzados portadas desde Sigma.</summary>
internal static class AnovaStatistics
{
    /// <summary>
    /// Shapiro-Wilk completo (Royston 1995, AS R94): estadístico W y valor p, para 12 ≤ n ≤ 5000.
    /// Es la misma aproximación que usa el resto del motor para el contraste de normalidad.
    /// </summary>
    public static (double Statistic, double PValue) ShapiroWilk(double[] values)
    {
        var n = values.Length;
        if (n < 12 || n > 5000) return (double.NaN, double.NaN);
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);

        var m = new double[n];
        for (var index = 0; index < n; index++) m[index] = SpecialFunctions.NormalInv((index + 1 - 0.375) / (n + 0.25));
        var sumM = m.Sum(value => value * value);
        var c = m.Select(value => value / Math.Sqrt(sumM)).ToArray();
        var u = 1 / Math.Sqrt(n);
        double Polynomial(double value, double c1, double c2, double c3, double c4, double c5) =>
            value + c1 * u + c2 * u * u + c3 * Math.Pow(u, 3) + c4 * Math.Pow(u, 4) + c5 * Math.Pow(u, 5);

        var a = new double[n];
        a[n - 1] = Polynomial(c[n - 1], 0.221157, -0.147981, -2.071190, 4.434685, -2.706056);
        a[n - 2] = Polynomial(c[n - 2], 0.042981, -0.293762, -1.752461, 5.682633, -3.582633);
        a[0] = -a[n - 1];
        a[1] = -a[n - 2];
        var phi = (sumM - 2 * m[n - 1] * m[n - 1] - 2 * m[n - 2] * m[n - 2]) /
                  (1 - 2 * a[n - 1] * a[n - 1] - 2 * a[n - 2] * a[n - 2]);
        for (var index = 2; index < n - 2; index++) a[index] = m[index] / Math.Sqrt(phi);

        var mean = sorted.Average();
        var numerator = a.Select((coefficient, index) => coefficient * sorted[index]).Sum();
        var denominator = sorted.Sum(value => Math.Pow(value - mean, 2));
        if (denominator <= 0) return (double.NaN, double.NaN);
        var w = numerator * numerator / denominator;
        var logN = Math.Log(n);
        var mu = -1.5861 - 0.31082 * logN - 0.083751 * logN * logN + 0.0038915 * Math.Pow(logN, 3);
        var sigma = Math.Exp(-0.4803 - 0.082676 * logN + 0.0030302 * logN * logN);
        var z = (Math.Log(1 - Math.Min(w, 1 - 1e-15)) - mu) / sigma;
        return (w, 1 - SpecialFunctions.NormalCdf(z));
    }
}
