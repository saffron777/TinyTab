namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

internal static class ResamplingAnalyses
{
    public static readonly IReadOnlyList<AnalysisDefinition> All =
    [
        BootstrapAnalyses.Bootstrap,
        PermutationAnalyses.Permutation,
        MonteCarloAnalyses.MonteCarlo,
        RandomDataAnalyses.RandomData
    ];
}
