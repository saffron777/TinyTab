namespace StatisticEngine.Domain.Analysis;

/// <summary>
/// Segunda mitad del catálogo, en el orden exacto de <c>registry-advanced.ts</c>
/// (calidad, capacidad, MSA, DOE, series temporales, fiabilidad, multivariante,
/// potencia y no paramétricos).
/// </summary>
public static class AdvancedAnalyses
{
    public static IReadOnlyList<AnalysisDefinition> All { get; } =
    [
        .. Analyses.ControlChartAnalyses.All,
        .. Analyses.CapabilityAnalyses.All,
        .. Analyses.MsaAnalyses.All,
        .. Analyses.DoeAnalyses.All,
        .. Analyses.TimeSeriesAnalyses.All,
        .. Analyses.ReliabilityAnalyses.All,
        .. Analyses.MultivariateAnalyses.All,
        .. Analyses.PowerAnalyses.All,
        .. Analyses.NonParametricAnalyses.All
    ];
}
