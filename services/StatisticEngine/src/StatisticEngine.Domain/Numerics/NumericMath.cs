namespace StatisticEngine.Domain.Numerics;

/// <summary>
/// Estadística descriptiva base del motor (<c>numeric.ts</c>): sumas, momentos,
/// cuantiles tipo 7 (R/Excel/Minitab), rangos con empates y correlaciones.
/// </summary>
public static class NumericMath
{
    public static double Sum(IReadOnlyList<double> values)
    {
        var total = 0.0;
        for (var i = 0; i < values.Count; i++) total += values[i];
        return total;
    }

    public static double Mean(IReadOnlyList<double> values) =>
        values.Count == 0 ? double.NaN : Sum(values) / values.Count;

    public static double Variance(IReadOnlyList<double> values, int ddof = 1)
    {
        if (values.Count - ddof <= 0) return double.NaN;
        var center = Mean(values);
        var acc = 0.0;
        for (var i = 0; i < values.Count; i++)
        {
            var delta = values[i] - center;
            acc += delta * delta;
        }
        return acc / (values.Count - ddof);
    }

    public static double Sd(IReadOnlyList<double> values, int ddof = 1) =>
        Math.Sqrt(Variance(values, ddof));

    public static double StandardErrorOfMean(IReadOnlyList<double> values) =>
        Sd(values) / Math.Sqrt(values.Count);

    /// <summary>Orden ascendente con comparador que neutraliza NaN (el <c>sort</c> de JS lo hace estable).</summary>
    public static double[] Sorted(IReadOnlyList<double> values)
    {
        var copy = new double[values.Count];
        for (var i = 0; i < values.Count; i++) copy[i] = values[i];
        Array.Sort(copy, CompareNaNSafe);
        return copy;
    }

    public static double MinOf(IReadOnlyList<double> values) => JsMath.MinOf(values);

    public static double MaxOf(IReadOnlyList<double> values) => JsMath.MaxOf(values);

    /// <summary>Cuantil tipo 7 (R, Excel, Minitab).</summary>
    public static double Quantile(IReadOnlyList<double> values, double probability)
    {
        if (values.Count == 0) return double.NaN;
        var p = JsMath.Clamp(probability, 0, 1);
        var ordered = Sorted(values);
        if (ordered.Length == 1) return ordered[0];
        var h = (ordered.Length - 1) * p;
        var lower = (int)Math.Floor(h);
        var upper = (int)Math.Ceiling(h);
        var a = ordered[lower];
        var b = ordered[upper];
        return a + (h - lower) * (b - a);
    }

    public static double Median(IReadOnlyList<double> values) => Quantile(values, 0.5);

    public static double Iqr(IReadOnlyList<double> values) => Quantile(values, 0.75) - Quantile(values, 0.25);

    public static double Skewness(IReadOnlyList<double> values)
    {
        var n = values.Count;
        if (n < 3) return double.NaN;
        var m = Mean(values);
        var s = Sd(values);
        if (s == 0) return 0;
        var acc = 0.0;
        for (var i = 0; i < n; i++) acc += Math.Pow((values[i] - m) / s, 3);
        return n / ((n - 1.0) * (n - 2.0)) * acc;
    }

    public static double Kurtosis(IReadOnlyList<double> values)
    {
        var n = values.Count;
        if (n < 4) return double.NaN;
        var m = Mean(values);
        var s = Sd(values);
        if (s == 0) return 0;
        var acc = 0.0;
        for (var i = 0; i < n; i++) acc += Math.Pow((values[i] - m) / s, 4);
        var term1 = n * (n + 1.0) / ((n - 1.0) * (n - 2.0) * (n - 3.0));
        var term2 = 3 * (n - 1.0) * (n - 1.0) / ((n - 2.0) * (n - 3.0));
        return term1 * acc - term2;
    }

    public static double Covariance(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var n = Math.Min(x.Count, y.Count);
        if (n < 2) return double.NaN;
        var mx = Mean(Slice(x, n));
        var my = Mean(Slice(y, n));
        var acc = 0.0;
        for (var i = 0; i < n; i++) acc += (x[i] - mx) * (y[i] - my);
        return acc / (n - 1);
    }

    public static double Correlation(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var n = Math.Min(x.Count, y.Count);
        if (n < 2) return double.NaN;
        var cov = Covariance(x, y);
        var sx = Sd(Slice(x, n));
        var sy = Sd(Slice(y, n));
        if (sx == 0 || sy == 0) return double.NaN;
        return cov / (sx * sy);
    }

    /// <summary>Rangos con corrección de empates (promedio) y orden estable como el <c>sort</c> de JS.</summary>
    public static double[] Ranks(IReadOnlyList<double> values)
    {
        var n = values.Count;
        var indexed = new (double Value, int Index)[n];
        for (var i = 0; i < n; i++) indexed[i] = (values[i], i);

        var ordered = indexed.OrderBy(item => item.Value, Comparer<double>.Create(CompareNaNSafe)).ToArray();
        var output = new double[n];
        var start = 0;
        while (start < ordered.Length)
        {
            var end = start;
            while (end + 1 < ordered.Length && ordered[end + 1].Value == ordered[start].Value) end++;
            var average = (start + end) / 2.0 + 1;
            for (var k = start; k <= end; k++) output[ordered[k].Index] = average;
            start = end + 1;
        }
        return output;
    }

    public static double LogFactorial(double n) => n < 0 ? double.NaN : SpecialFunctions.LogGamma(n + 1);

    public static double LogCombination(double n, double k)
    {
        if (k < 0 || k > n) return double.NegativeInfinity;
        return SpecialFunctions.LogGamma(n + 1) - SpecialFunctions.LogGamma(k + 1) - SpecialFunctions.LogGamma(n - k + 1);
    }

    /// <summary>Copia los primeros <paramref name="count"/> elementos (equivalente a <c>slice(0, n)</c>).</summary>
    public static double[] Slice(IReadOnlyList<double> values, int count)
    {
        var output = new double[count];
        for (var i = 0; i < count; i++) output[i] = values[i];
        return output;
    }

    private static int CompareNaNSafe(double a, double b)
    {
        if (double.IsNaN(a) || double.IsNaN(b)) return 0;
        if (a < b) return -1;
        return a > b ? 1 : 0;
    }
}
