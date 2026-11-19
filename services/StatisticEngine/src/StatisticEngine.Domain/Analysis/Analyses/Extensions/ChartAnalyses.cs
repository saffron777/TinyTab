namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

internal static class ChartAnalyses
{
    public static readonly IReadOnlyList<AnalysisDefinition> All =
    [
        SplomAnalyses.Splom,
        HeatmapAnalyses.Heatmap,
        SimpleChartAnalyses.LinePlot,
        SimpleChartAnalyses.Pie,
        SimpleChartAnalyses.Pareto,
        SimpleChartAnalyses.Scatter3D,
        IntervalScatterAnalyses.Interval,
        IntervalScatterAnalyses.Scatter,
        ProbabilityAnalyses.Probability
    ];
}
