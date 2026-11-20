namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>
/// Análisis de pruebas adicionales portados desde Sigma (<c>sigma/domain/analysis/pruebas.py</c>).
/// Cada análisis conserva id, metadatos, roles, parámetros y salida de Sigma.
/// </summary>
public static class TestAnalyses
{
    public static IReadOnlyList<AnalysisDefinition> All { get; } =
    [
        .. NormalityAnalyses.All,
        .. VarianceAnalyses.All,
        .. ContingencyAnalyses.All,
        .. AgreementAnalyses.All,
        .. EstimationAnalyses.All,
        .. PowerDesignAnalyses.All,
        AdditionalTestAnalyses.SignTest,
        AdditionalTestAnalyses.RunsTest,
        AdditionalTestAnalyses.Tost
    ];
}
