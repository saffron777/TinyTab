using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Quality;
using static StatisticEngine.Domain.Analysis.FormatHelpers;

namespace StatisticEngine.Domain.Analysis.Analyses;

/// <summary>Análisis de capacidad (paridad: <c>registry-advanced.ts:170-201</c>).</summary>
public static class CapabilityAnalyses
{
    /// <summary>
    /// Lee un límite de especificación: ausente o cadena vacía equivale a <c>undefined</c> en el
    /// motor (sin límite). Nunca se convierte en <c>NaN</c>: <c>double?</c> distingue ambos casos.
    /// </summary>
    private static double? SpecificationLimit(AnalysisInput input, string key)
    {
        if (!input.Params.TryGetValue(key, out var raw) || raw is null) return null;
        if (raw is string text && text.Length == 0) return null;
        var value = input.Pnum(key);
        return double.IsNaN(value) ? null : value;
    }

    private static readonly AnalysisDefinition CapabilityDefinition = DefinitionFactory.Define(
        "capability",
        "Análisis de capacidad",
        "Capacidad",
        "Cp, Cpk, Pp, Ppk y PPM esperados.",
        [DefinitionFactory.Numeric("x", "Mediciones")],
        [],
        [
            DefinitionFactory.Param("lsl", "Límite inferior (vacío = sin límite)", AnalysisParamKinds.Number, string.Empty, true),
            DefinitionFactory.Param("usl", "Límite superior (vacío = sin límite)", AnalysisParamKinds.Number, string.Empty, true),
            DefinitionFactory.Select(
                "method",
                "Método",
                "normal",
                DefinitionFactory.Option("normal", "Normal"),
                DefinitionFactory.Option("nonnormal", "No normal (empírico)"))
        ],
        input =>
        {
            var lowerSpec = SpecificationLimit(input, "lsl");
            var upperSpec = SpecificationLimit(input, "usl");
            if (lowerSpec is null && upperSpec is null)
            {
                return Result<AnalysisResult>.Err(DomainErrorCode.InvalidParameter, "Indique al menos un límite de especificación.");
            }

            var values = input.Num("x");
            var method = input.Pstr("method", "normal");
            var result = string.Equals(method, "nonnormal", StringComparison.Ordinal)
                ? Capability.NonnormalCapability(values, lowerSpec, upperSpec)
                : Capability.ProcessCapability(values, lowerSpec, upperSpec, new CapabilityOptions(method));
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);

            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "capability",
                "Análisis de capacidad",
                Sections(
                    Table(
                        "Índices",
                        ["Cp", "Cpk", "Pp", "Ppk"],
                        [[N(value.Cp, 3), N(value.Cpk, 3), N(value.Pp, 3), N(value.Ppk, 3)]]),
                    Table(
                        "Especificaciones",
                        ["LIE", "LSE", "Media", "σ"],
                        [[N(value.LowerSpec), N(value.UpperSpec), N(value.Mean), N(value.Sd)]]),
                    Table(
                        "PPM esperados",
                        ["Debajo LIE", "Sobre LSE", "Total"],
                        [[N(value.PpmBelow, 1), N(value.PpmAbove, 1), N(value.PpmTotal, 1)]])),
                Charts: [new HistogramChart("Histograma con especificaciones", values)]));
        });

    public static IReadOnlyList<AnalysisDefinition> All { get; } = [CapabilityDefinition];
}
