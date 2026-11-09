using StatisticEngine.Application.Ports;
using StatisticEngine.Domain;
using StatisticEngine.Domain.Analysis;

namespace StatisticEngine.Application.UseCases;

/// <summary>
/// Casos de uso del catálogo de análisis: ejecutar, validar y listar.
/// Portado de <c>application/usecases/analysis-runner.ts</c>.
/// </summary>
public sealed class AnalysisService : IAnalysisRunner
{
    public Result<AnalysisResult> Run(string analysisId, AnalysisInput input)
    {
        var analysis = AnalysisCatalog.Find(analysisId);
        if (analysis is null)
        {
            return Result<AnalysisResult>.Err(DomainErrorCode.InvalidInput, $"Análisis desconocido: {analysisId}");
        }
        return analysis.Run(input);
    }

    /// <summary>Paridad con <c>validateInput</c>: sólo comprueba requisitos obligatorios de columnas.</summary>
    public static Result<Unit> Validate(string analysisId, AnalysisInput input)
    {
        var analysis = AnalysisCatalog.Find(analysisId);
        if (analysis is null)
        {
            return Result<Unit>.Err(DomainErrorCode.InvalidInput, "Análisis desconocido.");
        }

        foreach (var requirement in analysis.NumericColumns)
        {
            if (!requirement.Required) continue;
            if (requirement.Multiple)
            {
                if (input.Numeric.Count == 0)
                {
                    return Result<Unit>.Err(DomainErrorCode.InsufficientData, $"Falta \"{requirement.Label}\".");
                }
            }
            else if (!input.Numeric.TryGetValue(requirement.Key, out var values) || values.Length == 0)
            {
                return Result<Unit>.Err(DomainErrorCode.InsufficientData, $"Falta \"{requirement.Label}\".");
            }
        }
        return Result<Unit>.Ok(Unit.Value);
    }

    public static IReadOnlyList<AnalysisDefinition> Catalog() => AnalysisCatalog.All;

    public static AnalysisDefinition? Find(string analysisId) => AnalysisCatalog.Find(analysisId);
}
