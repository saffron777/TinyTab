using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Power;

/// <summary>Potencia de una prueba (paridad: <c>power.ts:6-12</c>).</summary>
public sealed record PowerResult(double Power, double Alpha, double N, double Effect, string Method);

/// <summary>Punto de la curva OC (paridad: <c>power.ts:92-97</c>).</summary>
public sealed record OcPoint(double N, double Power);

/// <summary>
/// Potencia estadística y tamaño de muestra por aproximaciones normales.
/// Paridad 1:1 con <c>power.ts</c>. Defecto reproducido: las búsquedas de tamaño de muestra
/// agotan el bucle por completo y devuelven el tope sin error.
/// </summary>
public static class PowerAnalysis
{
    /// <summary>Potencia de la t de una muestra (paridad: <c>power.ts:19-23</c>).</summary>
    public static Result<PowerResult> PowerOneSampleT(double effect, double n, double alpha = 0.05)
    {
        if (!(n >= 2) || effect == 0 || !(alpha > 0 && alpha < 1))
        {
            return Result<PowerResult>.Err(DomainErrorCode.InvalidParameter, "Parámetros de potencia inválidos.");
        }
        var noncentrality = Math.Abs(effect) * Math.Sqrt(n);
        return Result<PowerResult>.Ok(new PowerResult(
            TwoSidedPower(noncentrality, alpha), alpha, n, effect, "t de 1 muestra (aprox. normal)"));
    }

    /// <summary>Tamaño de muestra para la t de una muestra (paridad: <c>power.ts:25-33</c>).</summary>
    public static Result<PowerResult> SampleSizeOneSampleT(double effect, double power = 0.8, double alpha = 0.05)
    {
        if (effect == 0 || !(alpha > 0 && alpha < 1) || !(power > 0 && power < 1))
        {
            return Result<PowerResult>.Err(DomainErrorCode.InvalidParameter, "Parámetros inválidos.");
        }
        var n = 2;
        for (; n < 1_000_000; n++)
        {
            var result = PowerOneSampleT(effect, n, alpha);
            if (result.IsOk && result.Value.Power >= power) break;
        }
        return Result<PowerResult>.Ok(new PowerResult(power, alpha, n, effect, "t de 1 muestra (aprox. normal)"));
    }

    /// <summary>Potencia de la t de dos muestras (paridad: <c>power.ts:35-39</c>).</summary>
    public static Result<PowerResult> PowerTwoSampleT(double effect, double nPerGroup, double alpha = 0.05)
    {
        if (!(nPerGroup >= 2) || effect == 0)
        {
            return Result<PowerResult>.Err(DomainErrorCode.InvalidParameter, "Parámetros inválidos.");
        }
        var noncentrality = Math.Abs(effect) * Math.Sqrt(nPerGroup) / Math.Sqrt(2);
        return Result<PowerResult>.Ok(new PowerResult(
            TwoSidedPower(noncentrality, alpha), alpha, nPerGroup, effect, "t de 2 muestras (aprox. normal)"));
    }

    /// <summary>Tamaño de muestra para la t de dos muestras (paridad: <c>power.ts:41-49</c>).</summary>
    public static Result<PowerResult> SampleSizeTwoSampleT(double effect, double power = 0.8, double alpha = 0.05)
    {
        if (effect == 0) return Result<PowerResult>.Err(DomainErrorCode.InvalidParameter, "El efecto debe ser distinto de cero.");
        var n = 2;
        for (; n < 1_000_000; n++)
        {
            var result = PowerTwoSampleT(effect, n, alpha);
            if (result.IsOk && result.Value.Power >= power) break;
        }
        return Result<PowerResult>.Ok(new PowerResult(power, alpha, n, effect, "t de 2 muestras (aprox. normal)"));
    }

    /// <summary>Potencia de una proporción (paridad: <c>power.ts:51-59</c>).</summary>
    public static Result<PowerResult> PowerOneProportion(double p0, double p1, double n, double alpha = 0.05)
    {
        if (!(p0 > 0 && p0 < 1) || p1 < 0 || p1 > 1 || !(n >= 1))
        {
            return Result<PowerResult>.Err(DomainErrorCode.InvalidParameter, "Parámetros inválidos.");
        }
        var zAlpha = SpecialFunctions.NormalInv(1 - alpha / 2);
        var seNull = Math.Sqrt(p0 * (1 - p0) / n);
        // paridad: power.ts:55 — `|| 1e-12` sobre un 0 (o NaN) de la desviación alternativa.
        var seAlt = Math.Sqrt(p1 * (1 - p1) / n);
        if (seAlt == 0 || double.IsNaN(seAlt)) seAlt = 1e-12;
        var difference = Math.Abs(p1 - p0);
        var power = SpecialFunctions.NormalCdf((difference - zAlpha * seNull) / seAlt);
        return Result<PowerResult>.Ok(new PowerResult(power, alpha, n, p1 - p0, "proporción de 1 muestra (aprox. normal)"));
    }

    /// <summary>Tamaño de muestra para una proporción (paridad: <c>power.ts:61-69</c>).</summary>
    public static Result<PowerResult> SampleSizeOneProportion(double p0, double p1, double power = 0.8, double alpha = 0.05)
    {
        if (!(p0 > 0 && p0 < 1) || !(p1 > 0 && p1 < 1) || p0 == p1)
        {
            return Result<PowerResult>.Err(DomainErrorCode.InvalidParameter, "Proporciones inválidas.");
        }
        var n = 1;
        for (; n < 10_000_000; n++)
        {
            var result = PowerOneProportion(p0, p1, n, alpha);
            if (result.IsOk && result.Value.Power >= power) break;
        }
        return Result<PowerResult>.Ok(new PowerResult(power, alpha, n, p1 - p0, "proporción de 1 muestra (aprox. normal)"));
    }

    /// <summary>Potencia del ANOVA de un factor (paridad: <c>power.ts:71-79</c>).</summary>
    public static Result<PowerResult> PowerOneWayAnova(double effectSize, double nPerGroup, double groups, double alpha = 0.05)
    {
        if (!(effectSize > 0) || !(nPerGroup >= 2) || !(groups >= 2))
        {
            return Result<PowerResult>.Err(DomainErrorCode.InvalidParameter, "Parámetros inválidos.");
        }
        var df1 = groups - 1;
        var df2 = groups * (nPerGroup - 1);
        var critical = FCritical(df1, df2, alpha);
        var latent = Math.Max(0, effectSize * effectSize * nPerGroup * groups / (1 + effectSize * effectSize));
        var power = 1 - SpecialFunctions.NormalCdf((critical - latent) / Math.Sqrt(2 * Math.Max(1, latent)));
        return Result<PowerResult>.Ok(new PowerResult(
            Math.Min(1, Math.Max(0, power)), alpha, nPerGroup, effectSize, "ANOVA de 1 factor (aprox. normal)"));
    }

    /// <summary>Curva OC de la t de una muestra (paridad: <c>power.ts:92-97</c>).</summary>
    public static OcPoint[] OcCurve(double effect, IReadOnlyList<double> sizes, double alpha = 0.05)
    {
        var output = new OcPoint[sizes.Count];
        for (var i = 0; i < sizes.Count; i++)
        {
            var n = sizes[i];
            var result = PowerOneSampleT(effect, n, alpha);
            output[i] = new OcPoint(n, result.IsOk ? result.Value.Power : double.NaN);
        }
        return output;
    }

    /// <summary>Potencia acumulada de dos colas (paridad: <c>power.ts:14-17</c>).</summary>
    private static double TwoSidedPower(double noncentrality, double alpha)
    {
        var z = SpecialFunctions.NormalInv(1 - alpha / 2);
        return SpecialFunctions.NormalCdf(noncentrality - z) + SpecialFunctions.NormalCdf(-noncentrality - z);
    }

    /// <summary>Cuasi-cuantil F por bisección (paridad: <c>power.ts:81-90</c>).</summary>
    private static double FCritical(double df1, double df2, double alpha)
    {
        var low = 0.0;
        var high = 100.0;
        for (var i = 0; i < 100; i++)
        {
            var mid = (low + high) / 2;
            if (1 - InferenceDistributions.FCdfUpper(mid, df1, df2) >= 1 - alpha) high = mid;
            else low = mid;
        }
        return high;
    }
}
