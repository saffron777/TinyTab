namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

internal static class DoeAnalysesCatalog
{
    public static readonly IReadOnlyList<AnalysisDefinition> All =
    [
        DoeAnalyses.Factorial,
        DoeAnalyses.ResponseSurface
    ];
}
