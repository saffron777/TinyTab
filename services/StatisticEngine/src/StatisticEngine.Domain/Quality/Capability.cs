using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Quality;

/// <summary>Opciones de capacidad (paridad: <c>spc.ts:256-260</c>).</summary>
public sealed record CapabilityOptions(string? Method = null, double? Confidence = null);

/// <summary>Índices de capacidad (paridad: <c>spc.ts:239-254</c>).</summary>
public sealed record CapabilityResult(
    double Mean,
    double Sd,
    double LowerSpec,
    double UpperSpec,
    double Cp,
    double Cpk,
    double Cpu,
    double Cpl,
    double Pp,
    double Ppk,
    double PpmBelow,
    double PpmAbove,
    double PpmTotal,
    string Method);

/// <summary>
/// Estudios de capacidad. Defecto del motor reproducido: <c>sigmaWithin</c> se calcula con
/// la desviación estándar global, así que <c>cp == pp</c>, <c>cpu == ppu</c>, <c>cpl == ppl</c>
/// y <c>cpk == ppk</c>; con un solo límite <c>Math.min(cpu, cpl)</c> propaga NaN.
/// </summary>
public static class Capability
{
    /// <summary>Capacidad (paridad: <c>spc.ts:256-304</c>).</summary>
    public static Result<CapabilityResult> ProcessCapability(
        IReadOnlyList<double> values,
        double? lowerSpec,
        double? upperSpec,
        CapabilityOptions? options = null)
    {
        if (values.Count < 2) return Result<CapabilityResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos 2 observaciones.");

        var center = NumericMath.Mean(values);
        // paridad: spc.ts:264 — el motor usa sd(values) también como sigma within.
        var sigmaWithin = NumericMath.Sd(values);
        var method = options?.Method ?? "normal";
        var nonnormal = string.Equals(method, "nonnormal", StringComparison.Ordinal);

        var ppmBelow = 0.0;
        var ppmAbove = 0.0;
        if (nonnormal)
        {
            var below = 0;
            var above = 0;
            for (var i = 0; i < values.Count; i++)
            {
                if (lowerSpec.HasValue && values[i] < lowerSpec.Value) below++;
                if (upperSpec.HasValue && values[i] > upperSpec.Value) above++;
            }
            if (lowerSpec.HasValue) ppmBelow = 1e6 * (below / (double)values.Count);
            if (upperSpec.HasValue) ppmAbove = 1e6 * (above / (double)values.Count);
        }
        else
        {
            if (lowerSpec.HasValue) ppmBelow = 1e6 * SpecialFunctions.NormalCdf((lowerSpec.Value - center) / sigmaWithin);
            if (upperSpec.HasValue) ppmAbove = 1e6 * (1 - SpecialFunctions.NormalCdf((upperSpec.Value - center) / sigmaWithin));
        }

        var cp = lowerSpec.HasValue && upperSpec.HasValue && sigmaWithin > 0
            ? (upperSpec.Value - lowerSpec.Value) / (6 * sigmaWithin)
            : double.NaN;
        var cpu = upperSpec.HasValue && sigmaWithin > 0 ? (upperSpec.Value - center) / (3 * sigmaWithin) : double.NaN;
        var cpl = lowerSpec.HasValue && sigmaWithin > 0 ? (center - lowerSpec.Value) / (3 * sigmaWithin) : double.NaN;

        var overallSd = NumericMath.Sd(values);
        var pp = lowerSpec.HasValue && upperSpec.HasValue && overallSd > 0
            ? (upperSpec.Value - lowerSpec.Value) / (6 * overallSd)
            : double.NaN;
        var ppu = upperSpec.HasValue && overallSd > 0 ? (upperSpec.Value - center) / (3 * overallSd) : double.NaN;
        var ppl = lowerSpec.HasValue && overallSd > 0 ? (center - lowerSpec.Value) / (3 * overallSd) : double.NaN;

        return Result<CapabilityResult>.Ok(new CapabilityResult(
            center,
            sigmaWithin,
            lowerSpec ?? double.NaN,
            upperSpec ?? double.NaN,
            cp,
            // paridad: spc.ts:294 — NaN con un solo límite.
            JsMath.Min(cpu, cpl),
            cpu,
            cpl,
            pp,
            JsMath.Min(ppu, ppl),
            ppmBelow,
            ppmAbove,
            ppmBelow + ppmAbove,
            nonnormal ? "No normal (percentiles empíricos)" : "Normal"));
    }

    /// <summary>Capacidad no normal por cuantiles empíricos (paridad: <c>spc.ts:306-322</c>).</summary>
    public static Result<CapabilityResult> NonnormalCapability(
        IReadOnlyList<double> values,
        double? lowerSpec,
        double? upperSpec)
    {
        var result = ProcessCapability(values, lowerSpec, upperSpec, new CapabilityOptions("nonnormal"));
        if (result.IsError) return Result<CapabilityResult>.Err(result.Error.Code, result.Error.Message);

        var value = result.Value;
        var q50 = NumericMath.Quantile(values, 0.5);
        var q999865 = NumericMath.Quantile(values, 0.99865);
        var q00135 = NumericMath.Quantile(values, 0.00135);
        var spread = q999865 - q00135;
        var sixSigma = spread / 6;
        var cp = lowerSpec.HasValue && upperSpec.HasValue && spread > 0
            ? (upperSpec.Value - lowerSpec.Value) / (6 * sixSigma)
            : double.NaN;
        var cpu = upperSpec.HasValue && spread > 0 ? (upperSpec.Value - q50) / (3 * sixSigma) : double.NaN;
        var cpl = lowerSpec.HasValue && spread > 0 ? (q50 - lowerSpec.Value) / (3 * sixSigma) : double.NaN;
        return Result<CapabilityResult>.Ok(value with
        {
            Cp = cp,
            Cpu = cpu,
            Cpl = cpl,
            Cpk = JsMath.Min(cpu, cpl)
        });
    }
}
