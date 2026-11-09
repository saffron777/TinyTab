using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Random;

namespace StatisticEngine.Domain.Distributions;

// Port 1:1 de las distribuciones continuas de src/domain/stats/distributions.ts.
// Se conservan la parametrización, las fórmulas y el orden de evaluación del motor.

/// <summary>Normal(mu, sigma).</summary>
public sealed class NormalDistribution : IDistribution
{
    public string Id => "normal";

    public string Label => "Normal";

    public DistributionKind Kind => DistributionKind.Continuous;

    public IReadOnlyList<DistParamDef> Parameters { get; } =
    [
        new("mu", "Media (μ)", 0),
        new("sigma", "Desviación estándar (σ)", 1, MinExclusive: 0)
    ];

    // paridad: distributions.ts:149 — el TS encadena finite(mu) y positive(sigma).
    public Result<Unit> Validate(DistParams p) =>
        DistributionMath.Finite(p, "mu").IsOk ? DistributionMath.Positive(p, "sigma") : DistributionMath.Finite(p, "mu");

    public double Pdf(double x, DistParams p) => SpecialFunctions.NormalPdf((x - p["mu"]) / p["sigma"]) / p["sigma"];

    public double Cdf(double x, DistParams p) => SpecialFunctions.NormalCdf((x - p["mu"]) / p["sigma"]);

    public double InvCdf(double q, DistParams p) => p["mu"] + p["sigma"] * SpecialFunctions.NormalInv(q);

    public double Random(Xoshiro128 rng, DistParams p) => p["mu"] + p["sigma"] * rng.Normal();

    public double Mean(DistParams p) => p["mu"];

    public double Sd(DistParams p) => p["sigma"];

    public DistributionSupport Support(DistParams p) =>
        new(double.NegativeInfinity, double.PositiveInfinity);
}

/// <summary>t de Student(df).</summary>
public sealed class StudentTDistribution : IDistribution
{
    public string Id => "t";

    public string Label => "t de Student";

    public DistributionKind Kind => DistributionKind.Continuous;

    public IReadOnlyList<DistParamDef> Parameters { get; } = [new("df", "Grados de libertad", 10, MinExclusive: 0)];

    public Result<Unit> Validate(DistParams p) => DistributionMath.Positive(p, "df");

    public double Pdf(double x, DistParams p)
    {
        var df = p["df"];
        return Math.Exp(SpecialFunctions.LogGamma((df + 1) / 2) - SpecialFunctions.LogGamma(df / 2)) /
               Math.Sqrt(df * Math.PI) *
               Math.Pow(1 + x * x / df, -(df + 1) / 2);
    }

    public double Cdf(double x, DistParams p)
    {
        var df = p["df"];
        var tail = 0.5 * SpecialFunctions.Betai(df / 2, 0.5, df / (df + x * x));
        return x >= 0 ? 1 - tail : tail;
    }

    public double InvCdf(double q, DistParams p)
    {
        var df = p["df"];
        var target = 1 - 2 * JsMath.Min(q, 1 - q);
        var u = DistributionMath.Invert(v => SpecialFunctions.Betai(df / 2, 0.5, v), target, 0, 1);
        var x = Math.Sqrt(df * (1 - u) / u);
        return q < 0.5 ? -x : x;
    }

    public double Random(Xoshiro128 rng, DistParams p)
    {
        var df = p["df"];
        var normal = rng.Normal();
        return normal / Math.Sqrt(rng.Gamma(df / 2) / (df / 2));
    }

    public double Mean(DistParams p) => p["df"] > 1 ? 0 : double.NaN;

    public double Sd(DistParams p) =>
        p["df"] > 2 ? Math.Sqrt(p["df"] / (p["df"] - 2)) : double.NaN;

    public DistributionSupport Support(DistParams p) =>
        new(double.NegativeInfinity, double.PositiveInfinity);
}

/// <summary>Chi-cuadrada(df).</summary>
public sealed class ChiSquareDistribution : IDistribution
{
    public string Id => "chi2";

    public string Label => "Chi-cuadrada";

    public DistributionKind Kind => DistributionKind.Continuous;

    public IReadOnlyList<DistParamDef> Parameters { get; } = [new("df", "Grados de libertad", 5, MinExclusive: 0)];

    public Result<Unit> Validate(DistParams p) => DistributionMath.Positive(p, "df");

    public double Pdf(double x, DistParams p)
    {
        var df = p["df"];
        if (x <= 0) return 0;
        return Math.Exp((df / 2 - 1) * Math.Log(x) - x / 2 - (df / 2) * Math.Log(2) - SpecialFunctions.LogGamma(df / 2));
    }

    public double Cdf(double x, DistParams p) => x <= 0 ? 0 : SpecialFunctions.Gammp(p["df"] / 2, x / 2);

    public double InvCdf(double q, DistParams p)
    {
        var df = p["df"];
        return DistributionMath.Invert(x => SpecialFunctions.Gammp(df / 2, x / 2), q, 0, double.PositiveInfinity);
    }

    public double Random(Xoshiro128 rng, DistParams p) => 2 * rng.Gamma(p["df"] / 2);

    public double Mean(DistParams p) => p["df"];

    public double Sd(DistParams p) => Math.Sqrt(2 * p["df"]);

    public DistributionSupport Support(DistParams p) => new(0, double.PositiveInfinity);
}

/// <summary>F de Fisher(df1, df2).</summary>
public sealed class FisherFDistribution : IDistribution
{
    public string Id => "f";

    public string Label => "F de Fisher";

    public DistributionKind Kind => DistributionKind.Continuous;

    public IReadOnlyList<DistParamDef> Parameters { get; } =
    [
        new("df1", "Grados de libertad numerador", 5, MinExclusive: 0),
        new("df2", "Grados de libertad denominador", 10, MinExclusive: 0)
    ];

    public Result<Unit> Validate(DistParams p) => DistributionMath.Positive(p, "df1", "df2");

    public double Pdf(double x, DistParams p)
    {
        var d1 = p["df1"];
        var d2 = p["df2"];
        if (x <= 0) return 0;
        return Math.Exp(
            0.5 * d1 * Math.Log(d1) +
            0.5 * d2 * Math.Log(d2) +
            (d1 / 2 - 1) * Math.Log(x) -
            ((d1 + d2) / 2) * Math.Log(d2 + d1 * x) -
            (SpecialFunctions.LogGamma(d1 / 2) + SpecialFunctions.LogGamma(d2 / 2) - SpecialFunctions.LogGamma((d1 + d2) / 2)));
    }

    public double Cdf(double x, DistParams p) => CdfOf(x, p);

    public double InvCdf(double q, DistParams p) =>
        DistributionMath.Invert(x => CdfOf(x, p), q, 0, double.PositiveInfinity);

    public double Random(Xoshiro128 rng, DistParams p)
    {
        var d1 = p["df1"];
        var d2 = p["df2"];
        var numerator = 2 * rng.Gamma(d1 / 2);
        var denominator = 2 * rng.Gamma(d2 / 2);
        return numerator / d1 / (denominator / d2);
    }

    public double Mean(DistParams p) =>
        p["df2"] > 2 ? p["df2"] / (p["df2"] - 2) : double.NaN;

    public double Sd(DistParams p)
    {
        var d2 = p["df2"];
        if (d2 <= 4) return double.NaN;
        return Math.Sqrt(2 * d2 * d2 * (d2 - 2) / (Math.Pow(d2 - 2, 2) * (d2 - 4)));
    }

    public DistributionSupport Support(DistParams p) => new(0, double.PositiveInfinity);

    // paridad: distributions.ts:212 — fCdf usa betai directamente (sin 1 - cola).
    private static double CdfOf(double x, DistParams p)
    {
        var d1 = p["df1"];
        var d2 = p["df2"];
        if (x <= 0) return 0;
        return SpecialFunctions.Betai(d1 / 2, d2 / 2, d1 * x / (d1 * x + d2));
    }
}

/// <summary>Gamma(shape, scale).</summary>
public sealed class GammaDistribution : IDistribution
{
    public string Id => "gamma";

    public string Label => "Gamma";

    public DistributionKind Kind => DistributionKind.Continuous;

    public IReadOnlyList<DistParamDef> Parameters { get; } =
    [
        new("shape", "Forma", 2, MinExclusive: 0),
        new("scale", "Escala", 1, MinExclusive: 0)
    ];

    public Result<Unit> Validate(DistParams p) => DistributionMath.Positive(p, "shape", "scale");

    public double Pdf(double x, DistParams p)
    {
        var k = p["shape"];
        var s = p["scale"];
        if (x <= 0) return 0;
        return Math.Exp((k - 1) * Math.Log(x) - x / s - k * Math.Log(s) - SpecialFunctions.LogGamma(k));
    }

    public double Cdf(double x, DistParams p) => x <= 0 ? 0 : SpecialFunctions.Gammp(p["shape"], x / p["scale"]);

    public double InvCdf(double q, DistParams p)
    {
        var k = p["shape"];
        var s = p["scale"];
        return DistributionMath.Invert(x => SpecialFunctions.Gammp(k, x / s), q, 0, double.PositiveInfinity);
    }

    public double Random(Xoshiro128 rng, DistParams p) => rng.Gamma(p["shape"], p["scale"]);

    public double Mean(DistParams p) => p["shape"] * p["scale"];

    public double Sd(DistParams p) => Math.Sqrt(p["shape"]) * p["scale"];

    public DistributionSupport Support(DistParams p) => new(0, double.PositiveInfinity);
}

/// <summary>Beta(a, b).</summary>
public sealed class BetaDistribution : IDistribution
{
    public string Id => "beta";

    public string Label => "Beta";

    public DistributionKind Kind => DistributionKind.Continuous;

    public IReadOnlyList<DistParamDef> Parameters { get; } =
    [
        new("a", "Alfa (α)", 2, MinExclusive: 0),
        new("b", "Beta (β)", 2, MinExclusive: 0)
    ];

    public Result<Unit> Validate(DistParams p) => DistributionMath.Positive(p, "a", "b");

    public double Pdf(double x, DistParams p)
    {
        var a = p["a"];
        var b = p["b"];
        if (x <= 0 || x >= 1) return 0;
        return Math.Exp(
            (a - 1) * Math.Log(x) + (b - 1) * Math.Log(1 - x) -
            (SpecialFunctions.LogGamma(a) + SpecialFunctions.LogGamma(b) - SpecialFunctions.LogGamma(a + b)));
    }

    public double Cdf(double x, DistParams p) => SpecialFunctions.Betai(p["a"], p["b"], x);

    public double InvCdf(double q, DistParams p)
    {
        var a = p["a"];
        var b = p["b"];
        return DistributionMath.Invert(x => SpecialFunctions.Betai(a, b, x), q, 0, 1);
    }

    public double Random(Xoshiro128 rng, DistParams p) => rng.Beta(p["a"], p["b"]);

    public double Mean(DistParams p) => p["a"] / (p["a"] + p["b"]);

    public double Sd(DistParams p)
    {
        var a = p["a"];
        var b = p["b"];
        return Math.Sqrt(a * b / ((a + b) * (a + b) * (a + b + 1)));
    }

    public DistributionSupport Support(DistParams p) => new(0, 1);
}

/// <summary>Exponencial(rate).</summary>
public sealed class ExponentialDistribution : IDistribution
{
    public string Id => "exponential";

    public string Label => "Exponencial";

    public DistributionKind Kind => DistributionKind.Continuous;

    public IReadOnlyList<DistParamDef> Parameters { get; } = [new("rate", "Tasa (λ)", 1, MinExclusive: 0)];

    public Result<Unit> Validate(DistParams p) => DistributionMath.Positive(p, "rate");

    public double Pdf(double x, DistParams p) => x < 0 ? 0 : p["rate"] * Math.Exp(-p["rate"] * x);

    public double Cdf(double x, DistParams p) => x < 0 ? 0 : 1 - Math.Exp(-p["rate"] * x);

    public double InvCdf(double q, DistParams p) => -Math.Log(1 - q) / p["rate"];

    public double Random(Xoshiro128 rng, DistParams p) => rng.Exponential(p["rate"]);

    public double Mean(DistParams p) => 1 / p["rate"];

    public double Sd(DistParams p) => 1 / p["rate"];

    public DistributionSupport Support(DistParams p) => new(0, double.PositiveInfinity);
}

/// <summary>Weibull(shape, scale).</summary>
public sealed class WeibullDistribution : IDistribution
{
    public string Id => "weibull";

    public string Label => "Weibull";

    public DistributionKind Kind => DistributionKind.Continuous;

    public IReadOnlyList<DistParamDef> Parameters { get; } =
    [
        new("shape", "Forma", 1.5, MinExclusive: 0),
        new("scale", "Escala", 1, MinExclusive: 0)
    ];

    public Result<Unit> Validate(DistParams p) => DistributionMath.Positive(p, "shape", "scale");

    public double Pdf(double x, DistParams p)
    {
        var k = p["shape"];
        var s = p["scale"];
        if (x < 0) return 0;
        return k / s * Math.Pow(x / s, k - 1) * Math.Exp(-Math.Pow(x / s, k));
    }

    public double Cdf(double x, DistParams p) =>
        x < 0 ? 0 : 1 - Math.Exp(-Math.Pow(x / p["scale"], p["shape"]));

    public double InvCdf(double q, DistParams p) =>
        p["scale"] * Math.Pow(-Math.Log(1 - q), 1 / p["shape"]);

    public double Random(Xoshiro128 rng, DistParams p) => rng.Weibull(p["shape"], p["scale"]);

    public double Mean(DistParams p) => p["scale"] * Math.Exp(SpecialFunctions.LogGamma(1 + 1 / p["shape"]));

    public double Sd(DistParams p)
    {
        var k = p["shape"];
        var s = p["scale"];
        var m1 = Math.Exp(SpecialFunctions.LogGamma(1 + 1 / k));
        var m2 = Math.Exp(SpecialFunctions.LogGamma(1 + 2 / k));
        return s * Math.Sqrt(m2 - m1 * m1);
    }

    public DistributionSupport Support(DistParams p) => new(0, double.PositiveInfinity);
}

/// <summary>Log-normal(meanLog, sdLog).</summary>
public sealed class LogNormalDistribution : IDistribution
{
    public string Id => "lognormal";

    public string Label => "Log-normal";

    public DistributionKind Kind => DistributionKind.Continuous;

    public IReadOnlyList<DistParamDef> Parameters { get; } =
    [
        new("meanLog", "Media de log (μ)", 0),
        new("sdLog", "Desviación de log (σ)", 1, MinExclusive: 0)
    ];

    public Result<Unit> Validate(DistParams p) => DistributionMath.Positive(p, "sdLog");

    public double Pdf(double x, DistParams p)
    {
        var mu = p["meanLog"];
        var sigma = p["sdLog"];
        if (x <= 0) return 0;
        return SpecialFunctions.NormalPdf((Math.Log(x) - mu) / sigma) / (x * sigma);
    }

    public double Cdf(double x, DistParams p) =>
        x <= 0 ? 0 : SpecialFunctions.NormalCdf((Math.Log(x) - p["meanLog"]) / p["sdLog"]);

    public double InvCdf(double q, DistParams p) =>
        Math.Exp(p["meanLog"] + p["sdLog"] * SpecialFunctions.NormalInv(q));

    public double Random(Xoshiro128 rng, DistParams p) => rng.LogNormal(p["meanLog"], p["sdLog"]);

    public double Mean(DistParams p) => Math.Exp(p["meanLog"] + Math.Pow(p["sdLog"], 2) / 2);

    public double Sd(DistParams p)
    {
        var mu = p["meanLog"];
        var sigma = p["sdLog"];
        return Math.Sqrt((Math.Exp(sigma * sigma) - 1) * Math.Exp(2 * mu + sigma * sigma));
    }

    public DistributionSupport Support(DistParams p) => new(0, double.PositiveInfinity);
}

/// <summary>Uniforme continua(a, b).</summary>
public sealed class UniformDistribution : IDistribution
{
    public string Id => "uniform";

    public string Label => "Uniforme continua";

    public DistributionKind Kind => DistributionKind.Continuous;

    public IReadOnlyList<DistParamDef> Parameters { get; } =
    [
        new("a", "Mínimo", 0),
        new("b", "Máximo", 1)
    ];

    public Result<Unit> Validate(DistParams p) =>
        p["a"] < p["b"]
            ? Result.Ok(Unit.Value)
            : Result<Unit>.Err(DomainErrorCode.InvalidParameter, "El mínimo debe ser menor que el máximo.");

    public double Pdf(double x, DistParams p) =>
        x < p["a"] || x > p["b"] ? 0 : 1 / (p["b"] - p["a"]);

    public double Cdf(double x, DistParams p) => JsMath.Clamp((x - p["a"]) / (p["b"] - p["a"]), 0, 1);

    public double InvCdf(double q, DistParams p) => p["a"] + q * (p["b"] - p["a"]);

    public double Random(Xoshiro128 rng, DistParams p) => rng.Uniform(p["a"], p["b"]);

    public double Mean(DistParams p) => (p["a"] + p["b"]) / 2;

    public double Sd(DistParams p) => Math.Abs(p["b"] - p["a"]) / Math.Sqrt(12);

    public DistributionSupport Support(DistParams p) => new(p["a"], p["b"]);
}

/// <summary>Logística(mu, s).</summary>
public sealed class LogisticDistribution : IDistribution
{
    public string Id => "logistic";

    public string Label => "Logística";

    public DistributionKind Kind => DistributionKind.Continuous;

    public IReadOnlyList<DistParamDef> Parameters { get; } =
    [
        new("mu", "Ubicación", 0),
        new("s", "Escala", 1, MinExclusive: 0)
    ];

    public Result<Unit> Validate(DistParams p) => DistributionMath.Positive(p, "s");

    public double Pdf(double x, DistParams p)
    {
        var s = p["s"];
        var z = (x - p["mu"]) / s;
        var e = Math.Exp(-z);
        return e / (s * (1 + e) * (1 + e));
    }

    public double Cdf(double x, DistParams p) => 1 / (1 + Math.Exp(-(x - p["mu"]) / p["s"]));

    public double InvCdf(double q, DistParams p) => p["mu"] + p["s"] * Math.Log(q / (1 - q));

    public double Random(Xoshiro128 rng, DistParams p)
    {
        // paridad: distributions.ts:417 — el primer next() es el numerador y el segundo el denominador.
        var numerator = rng.Next();
        var denominator = rng.Next();
        return p["mu"] + p["s"] * Math.Log(numerator / (1 - denominator));
    }

    public double Mean(DistParams p) => p["mu"];

    public double Sd(DistParams p) => Math.PI * p["s"] / Math.Sqrt(3);

    public DistributionSupport Support(DistParams p) =>
        new(double.NegativeInfinity, double.PositiveInfinity);
}

/// <summary>Cauchy(mu, s).</summary>
public sealed class CauchyDistribution : IDistribution
{
    public string Id => "cauchy";

    public string Label => "Cauchy";

    public DistributionKind Kind => DistributionKind.Continuous;

    public IReadOnlyList<DistParamDef> Parameters { get; } =
    [
        new("mu", "Ubicación", 0),
        new("s", "Escala", 1, MinExclusive: 0)
    ];

    public Result<Unit> Validate(DistParams p) => DistributionMath.Positive(p, "s");

    public double Pdf(double x, DistParams p)
    {
        var s = p["s"];
        var z = (x - p["mu"]) / s;
        return 1 / (Math.PI * s * (1 + z * z));
    }

    public double Cdf(double x, DistParams p) => 0.5 + Math.Atan((x - p["mu"]) / p["s"]) / Math.PI;

    public double InvCdf(double q, DistParams p) => p["mu"] + p["s"] * Math.Tan(Math.PI * (q - 0.5));

    public double Random(Xoshiro128 rng, DistParams p) =>
        p["mu"] + p["s"] * Math.Tan(Math.PI * (rng.Next() - 0.5));

    public double Mean(DistParams p) => double.NaN;

    public double Sd(DistParams p) => double.NaN;

    public DistributionSupport Support(DistParams p) =>
        new(double.NegativeInfinity, double.PositiveInfinity);
}

/// <summary>Laplace(mu, b).</summary>
public sealed class LaplaceDistribution : IDistribution
{
    public string Id => "laplace";

    public string Label => "Laplace";

    public DistributionKind Kind => DistributionKind.Continuous;

    public IReadOnlyList<DistParamDef> Parameters { get; } =
    [
        new("mu", "Ubicación", 0),
        new("b", "Escala", 1, MinExclusive: 0)
    ];

    public Result<Unit> Validate(DistParams p) => DistributionMath.Positive(p, "b");

    public double Pdf(double x, DistParams p) =>
        1 / (2 * p["b"]) * Math.Exp(-Math.Abs(x - p["mu"]) / p["b"]);

    public double Cdf(double x, DistParams p)
    {
        var z = (x - p["mu"]) / p["b"];
        return z < 0 ? 0.5 * Math.Exp(z) : 1 - 0.5 * Math.Exp(-z);
    }

    public double InvCdf(double q, DistParams p) =>
        q < 0.5
            ? p["mu"] + p["b"] * Math.Log(2 * q)
            : p["mu"] - p["b"] * Math.Log(2 * (1 - q));

    public double Random(Xoshiro128 rng, DistParams p)
    {
        var u = rng.Next() - 0.5;
        return p["mu"] - p["b"] * JsMath.Sign(u) * Math.Log(1 - 2 * Math.Abs(u));
    }

    public double Mean(DistParams p) => p["mu"];

    public double Sd(DistParams p) => Math.Sqrt(2) * p["b"];

    public DistributionSupport Support(DistParams p) =>
        new(double.NegativeInfinity, double.PositiveInfinity);
}

/// <summary>Triangular(a, c, b).</summary>
public sealed class TriangularDistribution : IDistribution
{
    public string Id => "triangular";

    public string Label => "Triangular";

    public DistributionKind Kind => DistributionKind.Continuous;

    public IReadOnlyList<DistParamDef> Parameters { get; } =
    [
        new("a", "Mínimo", 0),
        new("c", "Moda", 0.5),
        new("b", "Máximo", 1)
    ];

    public Result<Unit> Validate(DistParams p) =>
        p["a"] < p["b"] && p["c"] >= p["a"] && p["c"] <= p["b"]
            ? Result.Ok(Unit.Value)
            : Result<Unit>.Err(DomainErrorCode.InvalidParameter, "Se requiere a ≤ c ≤ b con a < b.");

    public double Pdf(double x, DistParams p)
    {
        var a = p["a"];
        var b = p["b"];
        var c = p["c"];
        if (x < a || x > b) return 0;
        if (x < c) return 2 * (x - a) / ((b - a) * (c - a));
        if (x == c) return 2 / (b - a);
        return 2 * (b - x) / ((b - a) * (b - c));
    }

    public double Cdf(double x, DistParams p)
    {
        var a = p["a"];
        var b = p["b"];
        var c = p["c"];
        if (x <= a) return 0;
        if (x >= b) return 1;
        if (x <= c) return (x - a) * (x - a) / ((b - a) * (c - a));
        return 1 - (b - x) * (b - x) / ((b - a) * (b - c));
    }

    public double InvCdf(double q, DistParams p)
    {
        var a = p["a"];
        var b = p["b"];
        var c = p["c"];
        var split = (c - a) / (b - a);
        return q <= split
            ? a + Math.Sqrt(q * (b - a) * (c - a))
            : b - Math.Sqrt((1 - q) * (b - a) * (b - c));
    }

    public double Random(Xoshiro128 rng, DistParams p)
    {
        var a = p["a"];
        var b = p["b"];
        var c = p["c"];
        var u = rng.Next();
        var split = (c - a) / (b - a);
        return u <= split
            ? a + Math.Sqrt(u * (b - a) * (c - a))
            : b - Math.Sqrt((1 - u) * (b - a) * (b - c));
    }

    public double Mean(DistParams p) => (p["a"] + p["b"] + p["c"]) / 3;

    public double Sd(DistParams p) =>
        Math.Sqrt(
            (p["a"] * p["a"] +
             p["b"] * p["b"] +
             p["c"] * p["c"] -
             p["a"] * p["b"] -
             p["a"] * p["c"] -
             p["b"] * p["c"]) / 18);

    public DistributionSupport Support(DistParams p) => new(p["a"], p["b"]);
}

/// <summary>Valor extremo menor (Gumbel).</summary>
public sealed class SmallestExtremeDistribution : IDistribution
{
    public string Id => "smallestExtreme";

    public string Label => "Valor extremo menor (Gumbel)";

    public DistributionKind Kind => DistributionKind.Continuous;

    public IReadOnlyList<DistParamDef> Parameters { get; } =
    [
        new("mu", "Ubicación", 0),
        new("s", "Escala", 1, MinExclusive: 0)
    ];

    public Result<Unit> Validate(DistParams p) => DistributionMath.Positive(p, "s");

    public double Pdf(double x, DistParams p)
    {
        var s = p["s"];
        var z = Math.Exp((x - p["mu"]) / s);
        return z * Math.Exp(-z) / s;
    }

    public double Cdf(double x, DistParams p) =>
        1 - Math.Exp(-Math.Exp((x - p["mu"]) / p["s"]));

    public double InvCdf(double q, DistParams p) =>
        p["mu"] + p["s"] * Math.Log(-Math.Log(1 - q));

    public double Random(Xoshiro128 rng, DistParams p) =>
        p["mu"] + p["s"] * Math.Log(-Math.Log(1 - rng.Next()));

    public double Mean(DistParams p) => p["mu"] - 0.5772156649015329 * p["s"];

    public double Sd(DistParams p) => Math.PI * p["s"] / Math.Sqrt(6);

    public DistributionSupport Support(DistParams p) =>
        new(double.NegativeInfinity, double.PositiveInfinity);
}

/// <summary>Valor extremo mayor (Gumbel).</summary>
public sealed class LargestExtremeDistribution : IDistribution
{
    public string Id => "largestExtreme";

    public string Label => "Valor extremo mayor (Gumbel)";

    public DistributionKind Kind => DistributionKind.Continuous;

    public IReadOnlyList<DistParamDef> Parameters { get; } =
    [
        new("mu", "Ubicación", 0),
        new("s", "Escala", 1, MinExclusive: 0)
    ];

    public Result<Unit> Validate(DistParams p) => DistributionMath.Positive(p, "s");

    public double Pdf(double x, DistParams p)
    {
        var s = p["s"];
        var z = Math.Exp(-(x - p["mu"]) / s);
        return z * Math.Exp(-z) / s;
    }

    public double Cdf(double x, DistParams p) =>
        Math.Exp(-Math.Exp(-(x - p["mu"]) / p["s"]));

    public double InvCdf(double q, DistParams p) =>
        p["mu"] - p["s"] * Math.Log(-Math.Log(q));

    public double Random(Xoshiro128 rng, DistParams p) =>
        p["mu"] - p["s"] * Math.Log(-Math.Log(rng.Next()));

    public double Mean(DistParams p) => p["mu"] + 0.5772156649015329 * p["s"];

    public double Sd(DistParams p) => Math.PI * p["s"] / Math.Sqrt(6);

    public DistributionSupport Support(DistParams p) =>
        new(double.NegativeInfinity, double.PositiveInfinity);
}

/// <summary>Normal plegada(mu, sigma).</summary>
public sealed class FoldedNormalDistribution : IDistribution
{
    public string Id => "foldedNormal";

    public string Label => "Normal plegada";

    public DistributionKind Kind => DistributionKind.Continuous;

    public IReadOnlyList<DistParamDef> Parameters { get; } =
    [
        new("mu", "Media (μ)", 0),
        new("sigma", "Desviación (σ)", 1, MinExclusive: 0)
    ];

    public Result<Unit> Validate(DistParams p) => DistributionMath.Positive(p, "sigma");

    public double Pdf(double x, DistParams p)
    {
        var mu = p["mu"];
        var s = p["sigma"];
        if (x < 0) return 0;
        return (SpecialFunctions.NormalPdf((x - mu) / s) + SpecialFunctions.NormalPdf((x + mu) / s)) / s;
    }

    public double Cdf(double x, DistParams p) => CdfOf(x, p);

    public double InvCdf(double q, DistParams p) =>
        DistributionMath.Invert(x => CdfOf(x, p), q, 0, double.PositiveInfinity);

    public double Random(Xoshiro128 rng, DistParams p) =>
        Math.Abs(p["mu"] + p["sigma"] * rng.Normal());

    public double Mean(DistParams p) => MeanOf(p);

    public double Sd(DistParams p)
    {
        var mean = MeanOf(p);
        return Math.Sqrt(p["mu"] * p["mu"] + p["sigma"] * p["sigma"] - mean * mean);
    }

    public DistributionSupport Support(DistParams p) => new(0, double.PositiveInfinity);

    // paridad: distributions.ts:579 — CDF y media auxiliares de la normal plegada.
    private static double CdfOf(double x, DistParams p)
    {
        if (x <= 0) return 0;
        var mu = p["mu"];
        var s = p["sigma"];
        return SpecialFunctions.NormalCdf((x - mu) / s) - SpecialFunctions.NormalCdf((-x - mu) / s);
    }

    private static double MeanOf(DistParams p)
    {
        var mu = p["mu"];
        var s = p["sigma"];
        return s * Math.Sqrt(2 / Math.PI) * Math.Exp(-(mu * mu) / (2 * s * s)) + mu * (1 - 2 * SpecialFunctions.NormalCdf(-mu / s));
    }
}
