using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Reliability;

namespace StatisticEngine.Domain.Analysis.Analyses;

/// <summary>
/// Análisis de fiabilidad del catálogo (port 1:1 de <c>registry-advanced.ts:404-464</c>).
/// </summary>
public static class ReliabilityAnalyses
{
    public static IReadOnlyList<AnalysisDefinition> All { get; } =
    [
        new AnalysisDefinition(
            "reliability-weibull",
            "Ajuste de distribución Weibull",
            "Fiabilidad",
            "Estimación por máxima verosimilitud y gráfico de probabilidad.",
            [new ColumnRequirement("x", "Tiempos de falla", true, false)],
            [],
            [],
            RunWeibull),
        new AnalysisDefinition(
            "reliability-kaplan-meier",
            "Kaplan-Meier",
            "Fiabilidad",
            "Curva de supervivencia con censura.",
            [new ColumnRequirement("time", "Tiempo", true, false)],
            [],
            [new AnalysisParamDefinition("event", "Evento observado (columna 0/1 opcional)", AnalysisParamKinds.Number, "", [], true)],
            RunKaplanMeier)
    ];

    private static Result<AnalysisResult> RunWeibull(AnalysisInput input)
    {
        var values = input.Num("x");
        var result = ReliabilityAnalysis.WeibullFit(values);
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;
        var points = ReliabilityAnalysis.ProbabilityPlotPoints(values);
        var slope = 1 / value.Shape;
        var intercept = -Math.Log(value.Scale) / value.Shape;

        // paridad: registry-advanced.ts:423 — media estimada = escala · Γ(1 + 1/forma).
        var estimatedMean = value.Scale * Math.Exp(SpecialFunctions.LogGamma(1 + 1 / value.Shape));
        var sections = FormatHelpers.Sections(
            FormatHelpers.Table(
                "Parámetros (MLE)",
                ["Forma (β)", "Escala (η)", "Log-verosimilitud", "AIC"],
                [[
                    FormatHelpers.N(value.Shape, 4),
                    FormatHelpers.N(value.Scale, 4),
                    FormatHelpers.N(value.LogLikelihood, 3),
                    FormatHelpers.N(value.Aic, 3)
                ]]),
            FormatHelpers.Table(
                "Resumen",
                ["N", "Media estimada"],
                [[JsNumber.ToString(value.N), FormatHelpers.N(estimatedMean)]]));

        // paridad: registry-advanced.ts:415-425 — los puntos del TS llevan rank (aunque el tipo
        // declarado de ChartSpec no lo refleje).
        var chartPoints = new ProbabilityPoint[points.Length];
        for (var index = 0; index < points.Length; index++)
        {
            chartPoints[index] = new ProbabilityPoint(points[index].X, points[index].Y, points[index].Rank);
        }
        return Result.Ok(new AnalysisResult(
            "reliability-weibull",
            "Ajuste de distribución Weibull",
            sections,
            Charts: [new ProbabilityChart("Gráfico de probabilidad Weibull", chartPoints, slope, intercept)]));
    }

    private static Result<AnalysisResult> RunKaplanMeier(AnalysisInput input)
    {
        var times = input.Num("time");
        // paridad: registry-advanced.ts:450 — `input.numeric['event']` es una columna opcional.
        bool[] events;
        if (input.Numeric.TryGetValue("event", out var eventColumn))
        {
            events = new bool[eventColumn.Length];
            for (var index = 0; index < eventColumn.Length; index++) events[index] = eventColumn[index] == 1;
        }
        else
        {
            events = new bool[times.Length];
            for (var index = 0; index < times.Length; index++) events[index] = true;
        }

        var result = ReliabilityAnalysis.KaplanMeier(times, events);
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;

        var rows = new List<IReadOnlyList<object?>>(value.Times.Length);
        for (var index = 0; index < value.Times.Length; index++)
        {
            rows.Add([
                FormatHelpers.N(value.Times[index]),
                FormatHelpers.N(value.Survival[index], 4),
                FormatHelpers.N(value.StandardError[index], 4),
                JsNumber.ToString(value.AtRisk[index]),
                JsNumber.ToString(value.Events[index])
            ]);
        }

        var sections = FormatHelpers.Sections(
            FormatHelpers.Table("Supervivencia", ["t", "S(t)", "EE", "En riesgo", "Eventos"], rows),
            FormatHelpers.Table("Mediana de supervivencia", ["Mediana"], [[FormatHelpers.N(value.Median)]]));
        return Result.Ok(new AnalysisResult(
            "reliability-kaplan-meier",
            "Kaplan-Meier",
            sections,
            Charts: [new LineChart("Curva de supervivencia", [new LineSeries("S(t)", value.Survival)], "Tiempo")]));
    }
}
