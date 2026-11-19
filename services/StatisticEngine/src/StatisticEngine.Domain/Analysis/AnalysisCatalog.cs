namespace StatisticEngine.Domain.Analysis;

/// <summary>
/// Catálogo de análisis. <see cref="Baseline"/> son los 60 análisis con paridad 1:1 contra el motor
/// TypeScript de TinyTab (orden y resultados congelados); <see cref="Extensions"/> son los análisis
/// portados desde Sigma. <see cref="All"/> es la concatenación que publica el API.
/// </summary>
public static class AnalysisCatalog
{
    private static readonly Lazy<IReadOnlyList<AnalysisDefinition>> LazyBaseline =
        new(() => [.. BasicAnalyses.All, .. AdvancedAnalyses.All], isThreadSafe: true);

    private static readonly Lazy<IReadOnlyList<AnalysisDefinition>> LazyAll =
        new(() => [.. LazyBaseline.Value, .. ExtensionCatalog.All], isThreadSafe: true);

    private static readonly Lazy<IReadOnlyList<string>> LazyCategories = new(BuildCategories, isThreadSafe: true);

    /// <summary>Los 60 análisis del motor TinyTab, en el orden congelado de paridad.</summary>
    public static IReadOnlyList<AnalysisDefinition> Baseline => LazyBaseline.Value;

    /// <summary>Análisis portados desde Sigma (vacío hasta que se implemente cada fase).</summary>
    public static IReadOnlyList<AnalysisDefinition> Extensions => ExtensionCatalog.All;

    /// <summary>Catálogo publicado por el API: baseline + extensiones.</summary>
    public static IReadOnlyList<AnalysisDefinition> All => LazyAll.Value;

    /// <summary>Categorías del baseline, en el orden del motor TypeScript.</summary>
    public static IReadOnlyList<string> BaselineCategories => AnalysisCategories.All;

    /// <summary>Categorías publicadas: las del baseline más las de extensión que tengan análisis.</summary>
    public static IReadOnlyList<string> Categories => LazyCategories.Value;

    public static AnalysisDefinition? Find(string id) =>
        All.FirstOrDefault(analysis => string.Equals(analysis.Id, id, StringComparison.Ordinal));

    public static AnalysisDefinition? FindBaseline(string id) =>
        Baseline.FirstOrDefault(analysis => string.Equals(analysis.Id, id, StringComparison.Ordinal));

    /// <summary>Agrupa todo el catálogo por categoría, en el orden de <see cref="Categories"/>.</summary>
    public static IReadOnlyList<(string Category, IReadOnlyList<AnalysisDefinition> Analyses)> ByCategory() =>
        Group(Categories, All);

    /// <summary>Agrupa sólo el baseline (proyección usada por las pruebas de paridad).</summary>
    public static IReadOnlyList<(string Category, IReadOnlyList<AnalysisDefinition> Analyses)> BaselineByCategory() =>
        Group(BaselineCategories, Baseline);

    private static IReadOnlyList<(string Category, IReadOnlyList<AnalysisDefinition> Analyses)> Group(
        IReadOnlyList<string> categories,
        IReadOnlyList<AnalysisDefinition> analyses) =>
        categories
            .Select(category => (
                category,
                (IReadOnlyList<AnalysisDefinition>)analyses.Where(analysis => analysis.Category == category).ToArray()))
            .Where(group => group.Item2.Count > 0)
            .ToArray();

    private static IReadOnlyList<string> BuildCategories()
    {
        var used = All.Select(analysis => analysis.Category).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. AnalysisCategories.All.Where(used.Contains),
            .. AnalysisCategories.Extensions.Where(used.Contains)
        ];
    }
}
