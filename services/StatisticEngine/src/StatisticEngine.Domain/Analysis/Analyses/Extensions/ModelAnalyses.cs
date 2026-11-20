namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Análisis de modelos de regresión portados desde Sigma (<c>models.py</c>, <c>models_more.py</c>).</summary>
public static class ModelAnalyses
{
    public static IReadOnlyList<AnalysisDefinition> All { get; } =
    [
        WlsAnalyses.Wls,
        PolynomialAnalyses.Polynomial,
        RobustAnalyses.Robust,
        GlmAnalyses.Glm,
        CountAnalyses.Count,
        SurveyAnalyses.SurveyOls,
        SurveyLogisticAnalyses.SurveyLogistic,
        MultinomialAnalyses.Multinomial,
        QuantileAnalyses.Quantile,
        DiagnosticsAnalyses.Diagnostics,
        LoglinearAnalyses.Loglinear,
        IvAnalyses.Iv,
        PlsAnalyses.Pls,
        ProbitAnalyses.Probit,
        NonlinearAnalyses.Nonlinear,
        OrdinalAnalyses.Ordinal,
        RegularizedAnalyses.Regularized,
        GamAnalyses.Gam
    ];
}
