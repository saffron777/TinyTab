namespace StatisticEngine.Domain.Analysis;

/// <summary>
/// Catálogo de los 60 análisis. <see cref="BasicAnalyses"/> y <see cref="AdvancedAnalyses"/>
/// contienen las definiciones; este tipo las agrega en el orden del motor TypeScript.
/// </summary>
public static class AnalysisCatalog
{
    private static readonly Lazy<IReadOnlyList<AnalysisDefinition>> LazyAll =
        new(() => [.. BasicAnalyses.All, .. AdvancedAnalyses.All], isThreadSafe: true);

    public static IReadOnlyList<AnalysisDefinition> All => LazyAll.Value;

    public static IReadOnlyList<string> Categories => AnalysisCategories.All;

    public static AnalysisDefinition? Find(string id) =>
        All.FirstOrDefault(analysis => string.Equals(analysis.Id, id, StringComparison.Ordinal));

    /// <summary>Agrupa por categoría en el orden de <see cref="AnalysisCategories"/>.</summary>
    public static IReadOnlyList<(string Category, IReadOnlyList<AnalysisDefinition> Analyses)> ByCategory() =>
        AnalysisCategories.All
            .Select(category => (category, (IReadOnlyList<AnalysisDefinition>)All.Where(analysis => analysis.Category == category).ToArray()))
            .Where(group => group.Item2.Count > 0)
            .ToArray();
}
