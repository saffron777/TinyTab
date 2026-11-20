namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

internal static class EconometricsAnalyses
{
    public static readonly IReadOnlyList<AnalysisDefinition> All =
    [
        VarAnalyses.Var,
        CointegrationAnalyses.Cointegration,
        VecmAnalyses.Vecm,
        ArdlAnalyses.Ardl,
        PanelAnalyses.Panel,
        CopulaAnalyses.Copula,
        GrangerAnalyses.Granger,
        BaiPerronAnalyses.BaiPerron,
        QuandtAndrewsAnalyses.QaBreak
    ];
}
