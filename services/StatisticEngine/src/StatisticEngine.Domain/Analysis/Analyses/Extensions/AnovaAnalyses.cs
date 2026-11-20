namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

internal static class AnovaAnalyses
{
    public static readonly IReadOnlyList<AnalysisDefinition> All =
    [
        AnovaPosthocAnalyses.Posthoc,
        NestedAnovaAnalyses.Nested,
        RepeatedMeasuresAnalyses.Repeated,
        AnovaFactorialAnalyses.Factorial,
        ManovaAnalyses.Manova
    ];
}
