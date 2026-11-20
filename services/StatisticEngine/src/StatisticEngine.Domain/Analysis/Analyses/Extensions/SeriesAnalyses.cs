namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

internal static class SeriesAnalyses
{
    public static readonly IReadOnlyList<AnalysisDefinition> All =
    [
        TsPlotAnalyses.TsPlot,
        TrendAnalyses.Trend,
        SmoothingAnalyses.Smoothing,
        ArimaAnalyses.Arima,
        CcfAnalyses.Ccf,
        MannKendallAnalyses.MannKendall,
        PeriodogramAnalyses.Periodogram,
        FiltersAnalyses.Filters,
        DecomposeAnalyses.Decompose,
        AcfPacfAnalyses.AcfPacf,
        BacktestAnalyses.Backtest,
        UnitRootAnalyses.UnitRoot
    ];
}
