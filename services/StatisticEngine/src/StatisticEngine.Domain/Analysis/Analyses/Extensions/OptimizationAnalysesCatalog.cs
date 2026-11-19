namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

internal static class OptimizationAnalysesCatalog
{
    public static readonly IReadOnlyList<AnalysisDefinition> All =
    [
        OptimizationAnalyses.LinearProgramming
    ];
}
