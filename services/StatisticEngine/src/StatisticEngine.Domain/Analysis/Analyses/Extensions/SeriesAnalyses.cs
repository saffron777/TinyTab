namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

internal static class SeriesAnalyses
{
    public static readonly IReadOnlyList<AnalysisDefinition> All =
    [
        TsPlotAnalyses.TsPlot,
        TrendAnalyses.Trend,
        CcfAnalyses.Ccf,
        MannKendallAnalyses.MannKendall,
        PeriodogramAnalyses.Periodogram,
        FiltersAnalyses.Filters,
        DecomposeAnalyses.Decompose,
        AcfPacfAnalyses.AcfPacf,
        BacktestAnalyses.Backtest
    ];
}
