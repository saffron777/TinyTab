using StatisticEngine.Domain;
using StatisticEngine.Domain.Analysis;

namespace StatisticEngine.Application.Ports;

/// <summary>Puerto de ejecución de análisis (espeja el puerto <c>AnalysisRunner</c> de TinyTab).</summary>
public interface IAnalysisRunner
{
    Result<AnalysisResult> Run(string analysisId, AnalysisInput input);
}
