namespace StatisticEngine.Domain.Numerics;

/// <summary>
/// <c>numpy.random.RandomState(seed)</c> (Mersenne Twister MT19937 con <c>init_genrand</c>) y su
/// generación de dobles de 53 bits. Se usa en los análisis cuya paridad depende de la secuencia
/// exacta de números de <c>np.random.RandomState</c> (la inicialización k-means++ de
/// <c>sklearn.mixture.GaussianMixture</c>).
/// </summary>
internal sealed class NumpyRandomState
{
    private const int StateSize = 624;

    private const int MidPoint = 397;

    private const double TwoPowMinus53 = 1.0 / 9007199254740992.0;

    private readonly uint[] _state = new uint[StateSize];

    private int _index = StateSize;

    public NumpyRandomState(ulong seed)
    {
        _state[0] = (uint)(seed & 0xFFFFFFFF);
        for (var index = 1; index < StateSize; index++)
        {
            _state[index] = unchecked(1812433253u * (_state[index - 1] ^ (_state[index - 1] >> 30)) + (uint)index);
        }
    }

    /// <summary><c>mt_rand_int32</c>: avanza la secuencia y aplica el templado estándar.</summary>
    public uint NextUInt32()
    {
        if (_index >= StateSize) Twist();
        var value = _state[_index++];
        value ^= value >> 11;
        value ^= (value << 7) & 0x9D2C5680u;
        value ^= (value << 15) & 0xEFC60000u;
        value ^= value >> 18;
        return value;
    }

    /// <summary><c>random_sample</c>: 53 bits (27 del primer entero y 26 del segundo).</summary>
    public double RandomSample()
    {
        var high = (ulong)(NextUInt32() >> 5);
        var low = (ulong)(NextUInt32() >> 6);
        return (high * 67108864.0 + low) * TwoPowMinus53;
    }

    /// <summary><c>uniform(size=n)</c>: n dobles consecutivos.</summary>
    public double[] Uniform(int count)
    {
        var values = new double[count];
        for (var index = 0; index < count; index++) values[index] = RandomSample();
        return values;
    }

    private void Twist()
    {
        for (var index = 0; index < StateSize; index++)
        {
            var lower = (_state[index] & 0x80000000u) | (_state[(index + 1) % StateSize] & 0x7FFFFFFFu);
            _state[index] = _state[(index + MidPoint) % StateSize] ^ (lower >> 1) ^ ((lower & 1) != 0 ? 0x9908B0DFu : 0u);
        }
        _index = 0;
    }
}
