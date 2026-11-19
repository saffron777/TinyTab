namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

internal static class SurvivalAnalysesCatalog
{
    public static readonly IReadOnlyList<AnalysisDefinition> All =
    [
        ParametricSurvivalAnalyses.Parametric,
        CoxSurvivalAnalyses.Cox
    ];
}
