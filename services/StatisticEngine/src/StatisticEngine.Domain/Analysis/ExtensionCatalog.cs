namespace StatisticEngine.Domain.Analysis;

/// <summary>
/// Análisis portados desde Sigma. Cada fase añade su lista; el orden es el de la fase, el módulo de
/// Sigma y la declaración original. Los análisis se publican con <c>Origin = "sigma"</c>.
/// </summary>
public static class ExtensionCatalog
{
    public static IReadOnlyList<AnalysisDefinition> All { get; } =
    [
        // Fase 1: pruebas adicionales y potencia.
        .. Analyses.Extensions.TestAnalyses.All,

        // Fase 2: modelos, ANOVA avanzado y encuestas.
        .. Analyses.Extensions.ModelAnalyses.All,
        .. Analyses.Extensions.AnovaAnalyses.All,
        .. Analyses.Extensions.SeriesAnalyses.All,
        .. Analyses.Extensions.EconometricsAnalyses.All,
        .. Analyses.Extensions.MultivariateAnalyses.All,
        .. Analyses.Extensions.ResamplingAnalyses.All,
        .. Analyses.Extensions.ChartAnalyses.All,
        .. Analyses.Extensions.OptimizationAnalysesCatalog.All,
        .. Analyses.Extensions.DoeAnalysesCatalog.All,
        .. Analyses.Extensions.SurvivalAnalysesCatalog.All

        // Fase 3: series temporales y econometría.
        // Fase 4: multivariante, supervivencia, calidad, DOE, optimización y remuestreo.
        // Fase 5: gráficos y operaciones de datos (estas últimas en DataCatalog).
    ];

    /// <summary>Ids de extensión declarados, útil para pruebas de unicidad.</summary>
    public static IReadOnlyList<string> Ids => All.Select(analysis => analysis.Id).ToArray();
}
