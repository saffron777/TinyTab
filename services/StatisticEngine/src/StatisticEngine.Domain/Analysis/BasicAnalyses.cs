namespace StatisticEngine.Domain.Analysis;

/// <summary>
/// Primera mitad del catálogo, en el orden exacto de <c>registry-basic.ts</c>
/// (estadística básica, distribuciones, tablas, pruebas de hipótesis, ANOVA y regresión).
/// </summary>
public static class BasicAnalyses
{
    /// <summary>Orden del catálogo TypeScript; cualquier cambio aquí altera la paridad del catálogo.</summary>
    private static readonly string[] Order =
    [
        "describe",
        "graphical-summary",
        "correlation",
        "covariance",
        "tally",
        "cross-tab",
        "normality",
        "probability-distribution",
        "one-sample-t",
        "one-sample-z",
        "two-sample-t",
        "paired-t",
        "one-proportion",
        "two-proportions",
        "one-variance",
        "two-variances",
        "chi-square-gof",
        "equivalence-tost",
        "anova-one-way",
        "anova-two-way",
        "equal-variances",
        "regression-multiple",
        "regression-stepwise",
        "regression-best-subsets",
        "regression-logistic",
        "regression-poisson"
    ];

    private static readonly IReadOnlyList<AnalysisDefinition> Sources =
    [
        .. Analyses.BasicStatisticsAnalyses.All,
        .. Analyses.TablesAnalyses.All,
        .. Analyses.DistributionAnalyses.All,
        .. Analyses.HypothesisAnalyses.All,
        .. Analyses.AnovaAnalyses.All,
        .. Analyses.RegressionAnalyses.All
    ];

    public static IReadOnlyList<AnalysisDefinition> All { get; } = Resolve();

    private static AnalysisDefinition[] Resolve()
    {
        var pool = new Dictionary<string, AnalysisDefinition>(StringComparer.Ordinal);
        foreach (var definition in Sources)
        {
            if (!pool.TryAdd(definition.Id, definition))
            {
                throw new InvalidOperationException($"Análisis duplicado en el catálogo: {definition.Id}.");
            }
        }

        var resolved = new AnalysisDefinition[Order.Length];
        for (var index = 0; index < Order.Length; index++)
        {
            var id = Order[index];
            if (!pool.TryGetValue(id, out var definition))
            {
                throw new InvalidOperationException($"Falta el análisis \"{id}\" en el catálogo básico.");
            }
            resolved[index] = definition;
        }
        return resolved;
    }
}
