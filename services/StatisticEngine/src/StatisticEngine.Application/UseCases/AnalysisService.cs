using StatisticEngine.Application.Ports;
using StatisticEngine.Domain;
using StatisticEngine.Domain.Analysis;

namespace StatisticEngine.Application.UseCases;

/// <summary>
/// Casos de uso del catálogo de análisis: ejecutar, validar y listar.
/// Portado de <c>application/usecases/analysis-runner.ts</c>. Los análisis extendidos validan
/// roles y parámetros con <see cref="AnalysisValidator"/> antes de ejecutarse.
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

        if (analysis.Origin == AnalysisCategories.SigmaOrigin)
        {
            var validation = AnalysisValidator.Validate(analysis, input);
            if (validation.IsError)
            {
                return Result<AnalysisResult>.Err(validation.Error.Code, validation.Error.Message);
            }
        }

        return analysis.Run(input);
    }

    /// <summary>Paridad con <c>validateInput</c>: los 60 baseline sólo comprueban columnas obligatorias.</summary>
    public static Result<Unit> Validate(string analysisId, AnalysisInput input)
    {
        var analysis = AnalysisCatalog.Find(analysisId);
        if (analysis is null)
        {
            return Result<Unit>.Err(DomainErrorCode.InvalidInput, "Análisis desconocido.");
        }

        if (analysis.Origin == AnalysisCategories.SigmaOrigin)
        {
            return AnalysisValidator.Validate(analysis, input);
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
