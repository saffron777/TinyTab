using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Random;

/// <summary>
/// Generador pseudoaleatorio xoshiro128** con semilla explícita, idéntico bit a bit a
/// <c>random.ts</c> (SplitMix32 de siembra, Box-Muller con caché, Marsaglia-Tsang).
/// </summary>
public sealed class Xoshiro128
{
    /// <summary>Semilla por defecto del motor (20261023).</summary>
    public const uint DefaultSeed = 20261023;

    private readonly uint[] _state = new uint[4];
    private double? _normalCache;

    public Xoshiro128(uint seed = DefaultSeed) => Seed(seed);

    public void Seed(uint seed)
    {
        var x = seed;
        for (var i = 0; i < 4; i++)
        {
            x = unchecked(x + 0x9e3779b9u);
            var z = x;
            z = unchecked((z ^ (z >> 16)) * 0x21f0aaadu);
            z = unchecked((z ^ (z >> 15)) * 0x735a2d97u);
            var value = z ^ (z >> 15);
            _state[i] = value == 0 ? 1u : value;
        }
        _normalCache = null;
    }

    /// <summary>Entero sin signo de 32 bits.</summary>
    public uint NextUInt32()
    {
        uint s0 = _state[0];
        uint s1 = _state[1];
        uint s2 = _state[2];
        uint s3 = _state[3];

        var sum = unchecked(s0 + s3);
        var product = unchecked(sum * 5u);
        var inner = unchecked(product + s0);
        var result = unchecked(RotateLeft(inner, 7) * 9u);

        var t = unchecked(s1 << 9);
        s2 ^= s0;
        s3 ^= s1;
        s1 ^= s2;
        s0 ^= s3;
        s2 ^= t;
        s3 = RotateLeft(s3, 11);

        _state[0] = s0;
        _state[1] = s1;
        _state[2] = s2;
        _state[3] = s3;
        return result;
    }

    /// <summary>Uniforme en [0, 1).</summary>
    public double Next() => NextUInt32() / 4294967296.0;

    public double Uniform(double lower = 0, double upper = 1) => lower + (upper - lower) * Next();

    /// <summary>Normal estándar por Box-Muller con caché (dos llamadas consumen dos uniformes).</summary>
    public double Normal()
    {
        if (_normalCache is { } cached)
        {
            _normalCache = null;
            return cached;
        }
        var u = 0.0;
        while (u <= JsMath.Epsilon) u = Next();
        var v = Next();
        var radius = Math.Sqrt(-2 * Math.Log(u));
        var angle = 2 * Math.PI * v;
        _normalCache = radius * Math.Sin(angle);
        return radius * Math.Cos(angle);
    }

    public double Exponential(double rate = 1) => -Math.Log(1 - Next()) / rate;

    /// <summary>Gamma(shape, scale) por Marsaglia-Tsang.</summary>
    public double Gamma(double shape, double scale = 1)
    {
        if (shape < 1)
        {
            var u = Next();
            return Gamma(shape + 1, scale) * Math.Pow(u, 1 / shape);
        }
        var d = shape - 1.0 / 3.0;
        var c = 1 / Math.Sqrt(9 * d);
        while (true)
        {
            var x = Normal();
            var v = Math.Pow(1 + c * x, 3);
            if (v <= 0) continue;
            var u = Next();
            if (u < 1 - 0.0331 * x * x * x * x) return d * v * scale;
            if (Math.Log(u) < 0.5 * x * x + d * (1 - v + Math.Log(v))) return d * v * scale;
        }
    }

    public double Beta(double a, double b)
    {
        var x = Gamma(a);
        var y = Gamma(b);
        return x / (x + y);
    }

    public double Weibull(double shape, double scale = 1) =>
        scale * Math.Pow(-Math.Log(1 - Next()), 1 / shape);

    public double LogNormal(double location = 0, double scale = 1) =>
        Math.Exp(location + scale * Normal());

    /// <summary>Binomial por inversión de la CDF (n ≤ 200) o aproximación normal.</summary>
    public double Binomial(double n, double p)
    {
        if (n <= 0) return 0;
        if (p <= 0) return 0;
        if (p >= 1) return n;
        if (n <= 200)
        {
            var probability = Math.Pow(1 - p, n);
            var u = Next();
            var cumulative = probability;
            double k = 0;
            while (u > cumulative && k < n)
            {
                k += 1;
                probability *= (n - k + 1) / k * (p / (1 - p));
                cumulative += probability;
            }
            return k;
        }
        var meanValue = n * p;
        var sdValue = Math.Sqrt(n * p * (1 - p));
        return JsMath.Clamp(JsMath.Round(meanValue + sdValue * Normal()), 0, n);
    }

    /// <summary>Poisson por Knuth (λ &lt; 30, tope de 10.000 iteraciones) o aproximación normal.</summary>
    public double Poisson(double lambda)
    {
        if (lambda <= 0) return 0;
        if (lambda < 30)
        {
            var limit = Math.Exp(-lambda);
            var product = 1.0;
            var k = 0;
            do
            {
                k += 1;
                product *= Next();
            }
            while (product > limit && k < 10000);
            return k - 1;
        }
        return Math.Max(0, JsMath.Round(lambda + Math.Sqrt(lambda) * Normal()));
    }

    public T Sample<T>(IReadOnlyList<T> items) => items[(int)Math.Floor(Next() * items.Count)];

    private static uint RotateLeft(uint value, int shift) =>
        unchecked((value << shift) | (value >> (32 - shift)));
}
