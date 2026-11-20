namespace StatisticEngine.Domain.Numerics;

/// <summary>
/// Generador PCG64 de NumPy (<c>numpy.random.default_rng(seed)</c>) y normales estándar por el
/// método ziggurat, reimplementados bit a bit.
/// <para>
/// Se usa en los análisis cuya paridad depende de la secuencia exacta de números aleatorios de
/// NumPy (por ejemplo, el valor p simulado de Quandt-Andrews fija la semilla 20240601). La
/// secuencia se reproduce con <see cref="NumpySeedSequence"/>, PCG64 (XSL-RR 128/64) y las tablas
/// de <see cref="NumpyZigguratTables"/>, que son las de NumPy 2.5.3.
/// </para>
/// </summary>
internal sealed class NumpyRandom
{
    private const double TwoPowMinus53 = 1.0 / 9007199254740992.0;

    private const ulong Mask64 = ulong.MaxValue;

    private static readonly UInt128 Multiplier = ((UInt128)2549297995355413924UL << 64) | 4865540595714422341UL;

    private UInt128 _state;

    private UInt128 _increment;

    /// <summary>Equivalente a <c>numpy.random.default_rng(seed)</c> para una semilla entera no negativa.</summary>
    public NumpyRandom(ulong seed)
    {
        var words = new NumpySeedSequence(seed).GenerateState64(4);
        var initialState = ((UInt128)words[0] << 64) | words[1];
        var initialSequence = ((UInt128)words[2] << 64) | words[3];
        _state = UInt128.Zero;
        _increment = (initialSequence << 1) | UInt128.One;
        Step();
        _state += initialState;
        Step();
    }

    private void Step() => _state = unchecked(_state * Multiplier + _increment);

    /// <summary><c>next_uint64</c> de PCG64 (XSL-RR 128/64): avanza y devuelve el estado rotado.</summary>
    public ulong NextUInt64()
    {
        Step();
        var high = (ulong)(_state >> 64);
        var low = (ulong)_state;
        var xorshifted = high ^ low;
        var rotation = (int)(high >> 58);
        return rotation == 0 ? xorshifted : (xorshifted >> rotation) | (xorshifted << (64 - rotation));
    }

    /// <summary><c>next_double</c>: 53 bits altos escalados a [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * TwoPowMinus53;

    /// <summary>Normal estándar por ziggurat, con el mismo orden de consumo que NumPy.</summary>
    public double StandardNormal()
    {
        while (true)
        {
            var bits = NextUInt64();
            var index = (int)(bits & 0xFF);
            bits >>= 8;
            var negative = (bits & 1) != 0;
            var magnitude = (bits >> 1) & 0x000FFFFFFFFFFFFFUL;
            var x = magnitude * NumpyZigguratTables.Wi[index];
            if (negative) x = -x;
            if (magnitude < NumpyZigguratTables.Ki[index]) return x;
            if (index == 0)
            {
                while (true)
                {
                    var xx = -NumpyZigguratTables.NormalInvR * Log1p(-NextDouble());
                    var yy = -Log1p(-NextDouble());
                    if (yy + yy > xx * xx)
                    {
                        return ((magnitude >> 8) & 1) != 0
                            ? -(NumpyZigguratTables.NormalR + xx)
                            : NumpyZigguratTables.NormalR + xx;
                    }
                }
            }

            if ((NumpyZigguratTables.Fi[index - 1] - NumpyZigguratTables.Fi[index]) * NextDouble()
                + NumpyZigguratTables.Fi[index]
                < Math.Exp(-0.5 * x * x))
            {
                return x;
            }
        }
    }

    /// <summary>log(1 + x) con la precisión de <c>log1p</c> de la libm de C.</summary>
    private static double Log1p(double x)
    {
        if (Math.Abs(x) >= 1e-4) return Math.Log(1.0 + x);
        var sum = 0.0;
        var power = x;
        for (var k = 1; k <= 14; k++)
        {
            sum += (k % 2 == 1 ? 1.0 : -1.0) * power / k;
            power *= x;
        }
        return sum;
    }
}

/// <summary>
/// <c>numpy.random.SeedSequence</c> con tamaño de pool 4: mezcla la entropía con los mismos
/// hashes y multiplicadores de NumPy para que el estado inicial de PCG64 coincida.
/// </summary>
internal sealed class NumpySeedSequence
{
    private const uint InitA = 0x43B0D7E5;

    private const uint MultA = 0x931E8875;

    private const uint InitB = 0x8B51F9DD;

    private const uint MultB = 0x58F38DED;

    private const uint MixMultL = 0xCA01F9DD;

    private const uint MixMultR = 0x4973F715;

    private const int XShift = 16;

    private readonly uint[] _pool = new uint[4];

    public NumpySeedSequence(ulong entropy)
    {
        var words = new List<uint>(2);
        var value = entropy;
        if (value == 0) words.Add(0u);
        while (value > 0)
        {
            words.Add((uint)(value & uint.MaxValue));
            value >>= 32;
        }

        var hash = InitA;
        for (var index = 0; index < _pool.Length; index++)
        {
            _pool[index] = HashMix(index < words.Count ? words[index] : 0u, ref hash);
        }

        for (var source = 0; source < _pool.Length; source++)
        {
            for (var target = 0; target < _pool.Length; target++)
            {
                if (source == target) continue;
                _pool[target] = Mix(_pool[target], HashMix(_pool[source], ref hash));
            }
        }

        for (var source = _pool.Length; source < words.Count; source++)
        {
            for (var target = 0; target < _pool.Length; target++)
            {
                _pool[target] = Mix(_pool[target], HashMix(words[source], ref hash));
            }
        }
    }

    /// <summary><c>generate_state(n_words, np.uint64)</c> de NumPy (palabras de 64 bits little-endian).</summary>
    public ulong[] GenerateState64(int count)
    {
        var hash = InitB;
        var half = new uint[count * 2];
        for (var index = 0; index < half.Length; index++)
        {
            var value = _pool[index % _pool.Length];
            value ^= hash;
            hash = unchecked(hash * MultB);
            value = unchecked(value * hash);
            value ^= value >> XShift;
            half[index] = value;
        }

        var state = new ulong[count];
        for (var index = 0; index < count; index++)
        {
            state[index] = half[2 * index] | ((ulong)half[(2 * index) + 1] << 32);
        }
        return state;
    }

    private static uint HashMix(uint value, ref uint hash)
    {
        value ^= hash;
        hash = unchecked(hash * MultA);
        value = unchecked(value * hash);
        value ^= value >> XShift;
        return value;
    }

    private static uint Mix(uint x, uint y)
    {
        var result = unchecked((MixMultL * x) - (MixMultR * y));
        result ^= result >> XShift;
        return result;
    }
}
