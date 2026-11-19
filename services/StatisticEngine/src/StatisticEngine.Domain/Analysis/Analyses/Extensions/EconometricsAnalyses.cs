namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

internal static class EconometricsAnalyses
{
    public static readonly IReadOnlyList<AnalysisDefinition> All =
    [
        CopulaAnalyses.Copula,
        GrangerAnalyses.Granger,
        BaiPerronAnalyses.BaiPerron
    ];
}
