namespace StatisticEngine.Domain.Numerics;

/// <summary>
/// Funciones especiales propias (Lanczos, gamma/beta incompletas, erf) idénticas a
/// <c>numeric.ts</c>: mismos coeficientes, mismos topes de iteración y mismas constantes.
/// </summary>
public static class SpecialFunctions
{
    private static readonly double[] LanczosCoefficients =
    [
        0.99999999999980993,
        676.5203681218851,
        -1259.1392167224028,
        771.32342877765313,
        -176.61502916214059,
        12.507343278686905,
        -0.13857109526572012,
        9.9843695780195716e-6,
        1.5056327351493116e-7
    ];

    private static readonly double Sqrt2 = Math.Sqrt(2);
    private static readonly double Sqrt2Pi = Math.Sqrt(2 * Math.PI);

    /// <summary><c>lgamma</c> por aproximación de Lanczos (g = 7, n = 9), error &lt; 1e-14.</summary>
    public static double LogGamma(double x)
    {
        if (x < 0.5) return Math.Log(Math.PI / Math.Sin(Math.PI * x)) - LogGamma(1 - x);
        var z = x - 1;
        var a = LanczosCoefficients[0];
        var t = z + 7.5;
        for (var i = 1; i < LanczosCoefficients.Length; i++) a += LanczosCoefficients[i] / (z + i);
        return 0.5 * Math.Log(2 * Math.PI) + (z + 0.5) * Math.Log(t) - t + Math.Log(a);
    }

    public static double Gamma(double x) => Math.Exp(LogGamma(x));

    public static double Beta(double a, double b) => Math.Exp(LogGamma(a) + LogGamma(b) - LogGamma(a + b));

    /// <summary>Serie de potencias para erf (|x| ≤ 0.5).</summary>
    private static double ErfSeries(double x)
    {
        var term = x;
        var total = x;
        var x2 = x * x;
        for (var n = 1; n < 60; n++)
        {
            term *= -x2 / n;
            var contribution = term / (2 * n + 1);
            total += contribution;
            if (Math.Abs(contribution) < Math.Abs(total) * 1e-17) break;
        }
        return 2 / Math.Sqrt(Math.PI) * total;
    }

    /// <summary>erfc(x) para x ≥ 0 mediante la gamma incompleta Q(1/2, x²).</summary>
    private static double ErfcPositive(double x) => x == 0 ? 1 : Gammq(0.5, x * x);

    public static double Erf(double x)
    {
        if (x < 0) return -Erf(-x);
        if (x <= 0.5) return ErfSeries(x);
        return 1 - ErfcPositive(x);
    }

    public static double Erfc(double x)
    {
        if (x < 0) return 2 - Erfc(-x);
        if (x <= 0.5) return 1 - ErfSeries(x);
        return ErfcPositive(x);
    }

    public static double NormalPdf(double z) => Math.Exp(-0.5 * z * z) / Sqrt2Pi;

    public static double NormalCdf(double z) => 0.5 * Erfc(-z / Sqrt2);

    /// <summary>Inversa de la normal estándar (Acklam simplificada + refinamiento de Newton).</summary>
    public static double NormalInv(double p)
    {
        if (!(p > 0 && p < 1)) return double.NaN;
        if (p == 0.5) return 0;

        var lowerTail = p < 0.5;
        var q = lowerTail ? p : 1 - p;
        var t = Math.Sqrt(-2 * Math.Log(q));

        var c0 = 2.515517;
        var c1 = 0.802853;
        var c2 = 0.010328;
        var d1 = 1.432788;
        var d2 = 0.189269;
        var d3 = 0.001308;

        var z = t - (c0 + c1 * t + c2 * t * t) / (1 + d1 * t + d2 * t * t + d3 * t * t * t);
        if (lowerTail) z = -z;

        for (var i = 0; i < 6; i++)
        {
            var cdf = NormalCdf(z);
            if (cdf == 0 || cdf == 1) break;
            var step = (cdf - p) / NormalPdf(z);
            z -= step;
            if (Math.Abs(step) < 1e-14 * Math.Max(1, Math.Abs(z))) break;
        }
        return z;
    }

    /// <summary>Serie para la gamma incompleta P(a,x) cuando x &lt; a+1.</summary>
    private static double GammaSeries(double a, double x)
    {
        var ap = a;
        var deliver = 1 / a;
        var total = deliver;
        for (var n = 0; n < 2000; n++)
        {
            ap += 1;
            deliver *= x / ap;
            total += deliver;
            if (Math.Abs(deliver) < Math.Abs(total) * 1e-16) break;
        }
        return total * Math.Exp(-x + a * Math.Log(x) - LogGamma(a));
    }

    /// <summary>Fracción continua para Q(a,x) cuando x ≥ a+1 (Lentz modificado).</summary>
    private static double GammaContinuedFraction(double a, double x)
    {
        var b = x + 1 - a;
        var c = 1 / JsMath.FpMin;
        var d = 1 / b;
        var h = d;
        for (var i = 1; i < 2000; i++)
        {
            var an = -i * (i - a);
            b += 2;
            d = an * d + b;
            if (Math.Abs(d) < JsMath.FpMin) d = JsMath.FpMin;
            c = b + an / c;
            if (Math.Abs(c) < JsMath.FpMin) c = JsMath.FpMin;
            d = 1 / d;
            var deliver = d * c;
            h *= deliver;
            if (Math.Abs(deliver - 1) < 1e-16) break;
        }
        return Math.Exp(-x + a * Math.Log(x) - LogGamma(a)) * h;
    }

    /// <summary>P(a,x): gamma incompleta regularizada inferior.</summary>
    public static double Gammp(double a, double x)
    {
        if (!(a > 0) || x < 0) return double.NaN;
        if (x == 0) return 0;
        if (x < a + 1) return GammaSeries(a, x);
        return 1 - GammaContinuedFraction(a, x);
    }

    /// <summary>Q(a,x): gamma incompleta regularizada superior.</summary>
    public static double Gammq(double a, double x)
    {
        if (!(a > 0) || x < 0) return double.NaN;
        if (x == 0) return 1;
        if (x < a + 1) return 1 - GammaSeries(a, x);
        return GammaContinuedFraction(a, x);
    }

    /// <summary>Fracción continua para la beta incompleta (Lentz).</summary>
    private static double BetaContinuedFraction(double a, double b, double x)
    {
        var qab = a + b;
        var qap = a + 1;
        var qam = a - 1;
        var c = 1.0;
        var d = 1 - qab * x / qap;
        if (Math.Abs(d) < JsMath.FpMin) d = JsMath.FpMin;
        d = 1 / d;
        var h = d;
        for (var m = 1; m <= 3000; m++)
        {
            var m2 = 2 * m;
            var aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1 + aa * d;
            if (Math.Abs(d) < JsMath.FpMin) d = JsMath.FpMin;
            c = 1 + aa / c;
            if (Math.Abs(c) < JsMath.FpMin) c = JsMath.FpMin;
            d = 1 / d;
            h *= d * c;
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1 + aa * d;
            if (Math.Abs(d) < JsMath.FpMin) d = JsMath.FpMin;
            c = 1 + aa / c;
            if (Math.Abs(c) < JsMath.FpMin) c = JsMath.FpMin;
            d = 1 / d;
            var deliver = d * c;
            h *= deliver;
            if (Math.Abs(deliver - 1) < 1e-16) break;
        }
        return h;
    }

    /// <summary>I_x(a,b): beta incompleta regularizada.</summary>
    public static double Betai(double a, double b, double x)
    {
        if (!(a > 0) || !(b > 0)) return double.NaN;
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        var logBeta = LogGamma(a + b) - LogGamma(a) - LogGamma(b) + a * Math.Log(x) + b * Math.Log(1 - x);
        var front = Math.Exp(logBeta);
        if (x < (a + 1) / (a + b + 2)) return front * BetaContinuedFraction(a, b, x) / a;
        return 1 - front * BetaContinuedFraction(b, a, 1 - x) / b;
    }

    /// <summary>Inversión numérica monótona por bisección con tolerancia relativa.</summary>
    public static double BisectInverse(Func<double, double> cdf, double probability, double lower, double upper, double tolerance = 1e-12) =>
        JsMath.BisectInverse(cdf, probability, lower, upper, tolerance);
}
