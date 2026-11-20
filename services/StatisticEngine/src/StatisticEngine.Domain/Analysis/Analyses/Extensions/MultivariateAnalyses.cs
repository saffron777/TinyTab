namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

internal static class MultivariateAnalyses
{
    public static readonly IReadOnlyList<AnalysisDefinition> All =
    [
        MahalanobisAnalyses.Mahalanobis,
        MdsAnalyses.Mds,
        HierarchicalAnalyses.Hierarchical,
        ReliabilityAnalyses.Reliability,
        CorrespondenceAnalyses.Correspondence,
        CcaAnalyses.Cca,
        MixtureAnalyses.Mixture
    ];
}
