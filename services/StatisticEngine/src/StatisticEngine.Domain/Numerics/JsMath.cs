namespace StatisticEngine.Domain.Numerics;

/// <summary>
/// Emulación puntual de la semántica numérica de JavaScript que el motor usa y que
/// .NET implementa de forma distinta. Cada método documenta la diferencia que corrige.
/// </summary>
public static class JsMath
{
    /// <summary><c>Number.EPSILON</c>.</summary>
    public const double Epsilon = 2.220446049250313e-16;

    /// <summary><c>Number.MIN_VALUE</c> usado como piso de las fracciones continuas.</summary>
    public const double FpMin = 1e-300;

    /// <summary>
    /// <c>Math.round</c>: redondea al entero más cercano y, en el punto medio, hacia +∞.
    /// Difiere de <see cref="Math.Round(double)"/> (bancario) y conserva el signo de <c>-0</c>.
    /// </summary>
    public static double Round(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return value;
        var rounded = Math.Floor(value + 0.5);
        if (rounded == 0 && value < 0) return -0.0;
        return rounded;
    }

    /// <summary><c>Math.sign</c>: devuelve NaN para NaN (a diferencia de <see cref="Math.Sign(double)"/>, que lanza).</summary>
    public static double Sign(double value)
    {
        if (double.IsNaN(value)) return double.NaN;
        if (value > 0) return 1;
        if (value < 0) return -1;
        return value;
    }

    /// <summary><c>Number.isInteger</c>.</summary>
    public static bool IsInteger(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && Math.Truncate(value) == value;

    /// <summary><c>Number.isFinite</c>.</summary>
    public static bool IsFinite(double value) => double.IsFinite(value);

    /// <summary><c>Math.min(a, b)</c>: propaga NaN igual que la especificación ECMAScript.</summary>
    public static double Min(double a, double b)
    {
        if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
        return a < b ? a : b;
    }

    /// <summary><c>Math.max(a, b)</c>: propaga NaN igual que la especificación ECMAScript.</summary>
    public static double Max(double a, double b)
    {
        if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
        return a > b ? a : b;
    }

    /// <summary><c>Math.min(...values)</c> con propagación de NaN y sin límite de argumentos.</summary>
    public static double MinOf(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return double.PositiveInfinity;
        var result = values[0];
        for (var i = 1; i < values.Count; i++)
        {
            var value = values[i];
            if (double.IsNaN(value) || double.IsNaN(result)) return double.NaN;
            if (value < result) result = value;
        }
        return double.IsNaN(result) ? double.NaN : result;
    }

    /// <summary><c>Math.max(...values)</c> con propagación de NaN y sin límite de argumentos.</summary>
    public static double MaxOf(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return double.NegativeInfinity;
        var result = values[0];
        for (var i = 1; i < values.Count; i++)
        {
            var value = values[i];
            if (double.IsNaN(value) || double.IsNaN(result)) return double.NaN;
            if (value > result) result = value;
        }
        return double.IsNaN(result) ? double.NaN : result;
    }

    /// <summary><c>clamp</c> del motor: <c>Math.min(Math.max(value, lower), upper)</c>.</summary>
    public static double Clamp(double value, double lower, double upper) =>
        Min(Max(value, lower), upper);

    /// <summary>
    /// Inversión monótona por bisección, idéntica a <c>bisectInverse</c> del motor
    /// (mismo criterio de parada y misma tolerancia relativa).
    /// </summary>
    public static double BisectInverse(
        Func<double, double> cdf,
        double probability,
        double lower,
        double upper,
        double tolerance = 1e-12)
    {
        var lo = lower;
        var hi = upper;
        for (var i = 0; i < 400; i++)
        {
            var mid = lo + (hi - lo) / 2;
            if (mid == lo || mid == hi) break;
            var value = cdf(mid);
            if (Math.Abs(value - probability) <= tolerance * Math.Max(1, Math.Abs(probability))) return mid;
            if (value < probability) lo = mid;
            else hi = mid;
            if (Math.Abs(hi - lo) <= tolerance * Math.Max(1, Math.Abs(mid))) break;
        }
        return lo + (hi - lo) / 2;
    }
}
