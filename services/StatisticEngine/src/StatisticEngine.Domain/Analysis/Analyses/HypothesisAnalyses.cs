using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Numerics;
using static StatisticEngine.Domain.Analysis.FormatHelpers;

namespace StatisticEngine.Domain.Analysis.Analyses;

/// <summary>Pruebas de hipótesis paramétricas (paridad: <c>registry-basic.ts:251-503</c>).</summary>
public static class HypothesisAnalyses
{
    private static AnalysisParamDefinition AlternativeParam() =>
        DefinitionFactory.Select(
            "alternative",
            "Hipótesis alternativa",
            "two-sided",
            DefinitionFactory.Option("two-sided", "≠"),
            DefinitionFactory.Option("less", "<"),
            DefinitionFactory.Option("greater", ">"));

    private static Result<AnalysisResult> TTest(string id, string title, Result<TTestResult> result)
    {
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;
        var effect = value.Estimate - value.NullValue;
        return Result<AnalysisResult>.Ok(new AnalysisResult(
            id,
            title,
            Sections(
                Table("Estimación", ["N", "Media", "Desv.Est.", "EE media"], []),
                Table(
                    "Prueba",
                    ["Hipótesis nula", "Estadístico t", "GL", "Valor p"],
                    [[$"μ = {N(value.NullValue)}", N(value.Statistic, 3), N(value.Df, 0), P(value.PValue)]]),
                Table(
                    $"Intervalo de confianza ({JsNumber.ToFixed(value.Confidence * 100, 1)} %)",
                    ["Inferior", "Media", "Superior"],
                    [[N(value.Lower), N(value.Estimate), N(value.Upper)]]),
                Text(
                    "Conclusión",
                    $"{(value.PValue < 0.05 ? "Se rechaza H₀ (p < 0,05)" : "No se rechaza H₀ (p ≥ 0,05)")}. Diferencia estimada: {N(effect)}"))));
    }

    private static readonly AnalysisDefinition OneSampleT = DefinitionFactory.Define(
        "one-sample-t",
        "t de 1 muestra",
        "Pruebas de hipótesis",
        "Contraste de la media de una población contra un valor de referencia.",
        [DefinitionFactory.Numeric("x", "Muestra")],
        [],
        [
            DefinitionFactory.Number("mu", "Media hipotética (H₀)", 0),
            DefinitionFactory.Number("confidence", "Nivel de confianza", 0.95),
            AlternativeParam()
        ],
        input => TTest(
            "one-sample-t",
            "t de 1 muestra",
            HypothesisTests.OneSampleT(
                input.Num("x"),
                input.Pnum("mu", 0),
                input.Pnum("confidence", 0.95),
                Alternatives.Parse(input.Pstr("alternative", "two-sided")))));

    private static readonly AnalysisDefinition OneSampleZ = DefinitionFactory.Define(
        "one-sample-z",
        "Z de 1 muestra",
        "Pruebas de hipótesis",
        "Contraste de la media con σ conocida.",
        [DefinitionFactory.Numeric("x", "Muestra")],
        [],
        [
            DefinitionFactory.Number("sigma", "σ conocida", 1),
            DefinitionFactory.Number("mu", "Media hipotética", 0)
        ],
        input =>
        {
            var result = HypothesisTests.OneSampleZ(input.Num("x"), input.Pnum("sigma", 1), input.Pnum("mu", 0));
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "one-sample-z",
                "Z de 1 muestra",
                Sections(
                    Table("Prueba", ["Estadístico Z", "Valor p"], [[N(value.Statistic, 3), P(value.PValue)]]),
                    Table(
                        "IC",
                        ["Inferior", "Media", "Superior"],
                        [[N(value.Lower), N(value.Estimate), N(value.Upper)]]))));
        });

    private static readonly AnalysisDefinition TwoSampleT = DefinitionFactory.Define(
        "two-sample-t",
        "t de 2 muestras",
        "Pruebas de hipótesis",
        "Comparación de medias de dos muestras independientes (Welch o varianzas agrupadas).",
        [
            DefinitionFactory.Numeric("x1", "Muestra 1"),
            DefinitionFactory.Numeric("x2", "Muestra 2")
        ],
        [],
        [
            DefinitionFactory.Select(
                "pooled",
                "Asumir varianzas iguales",
                "false",
                DefinitionFactory.Option("false", "No"),
                DefinitionFactory.Option("true", "Sí"))
        ],
        input =>
        {
            var result = HypothesisTests.TwoSampleT(
                input.Num("x1"),
                input.Num("x2"),
                input.Pbool("pooled"));
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "two-sample-t",
                "t de 2 muestras",
                Sections(
                    Table(
                        "Prueba",
                        ["Diferencia", "Estadístico t", "GL", "Valor p"],
                        [[N(value.Estimate), N(value.Statistic, 3), N(value.Df, 1), P(value.PValue)]]),
                    Table("IC de la diferencia", ["Inferior", "Superior"], [[N(value.Lower), N(value.Upper)]]))));
        });

    private static readonly AnalysisDefinition PairedT = DefinitionFactory.Define(
        "paired-t",
        "t pareada",
        "Pruebas de hipótesis",
        "Contraste de medias en observaciones pareadas.",
        [
            DefinitionFactory.Numeric("x1", "Muestra 1"),
            DefinitionFactory.Numeric("x2", "Muestra 2")
        ],
        [],
        [],
        input =>
        {
            var result = HypothesisTests.PairedT(input.Num("x1"), input.Num("x2"));
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "paired-t",
                "t pareada",
                Sections(
                    Table(
                        "Diferencia",
                        ["Media", "Estadístico t", "GL", "Valor p"],
                        [[N(value.Estimate), N(value.Statistic, 3), N(value.Df, 0), P(value.PValue)]]),
                    Table("IC", ["Inferior", "Superior"], [[N(value.Lower), N(value.Upper)]]))));
        });

    private static readonly AnalysisDefinition OneProportion = DefinitionFactory.Define(
        "one-proportion",
        "Proporción de 1 muestra",
        "Pruebas de hipótesis",
        "Contraste de una proporción con aproximación normal e intervalo de Wilson.",
        [],
        [],
        [
            DefinitionFactory.Integer("successes", "Éxitos", 10),
            DefinitionFactory.Integer("trials", "Ensayos", 40),
            DefinitionFactory.Number("p0", "Proporción nula", 0.5)
        ],
        input =>
        {
            var result = HypothesisTests.OneProportion(
                input.Pnum("successes", 10),
                input.Pnum("trials", 40),
                input.Pnum("p0", 0.5));
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "one-proportion",
                "Proporción de 1 muestra",
                Sections(
                    Table(
                        "Estimación",
                        ["Proporción", "IC inferior", "IC superior"],
                        [[N(value.Estimate), N(value.Lower), N(value.Upper)]]),
                    Table("Prueba", ["Z", "Valor p"], [[N(value.Z, 3), P(value.PValue)]]))));
        });

    private static readonly AnalysisDefinition TwoProportions = DefinitionFactory.Define(
        "two-proportions",
        "Proporción de 2 muestras",
        "Pruebas de hipótesis",
        "Comparación de dos proporciones independientes.",
        [],
        [],
        [
            DefinitionFactory.Integer("x1", "Éxitos muestra 1", 12),
            DefinitionFactory.Integer("n1", "Ensayos muestra 1", 50),
            DefinitionFactory.Integer("x2", "Éxitos muestra 2", 18),
            DefinitionFactory.Integer("n2", "Ensayos muestra 2", 50)
        ],
        input =>
        {
            var result = HypothesisTests.TwoProportions(
                input.Pnum("x1", 12),
                input.Pnum("n1", 50),
                input.Pnum("x2", 18),
                input.Pnum("n2", 50));
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "two-proportions",
                "Proporción de 2 muestras",
                Sections(
                    Table(
                        "Diferencia",
                        ["Estimación", "Z", "Valor p"],
                        [[N(value.Estimate), N(value.Z, 3), P(value.PValue)]]),
                    Table("IC de la diferencia", ["Inferior", "Superior"], [[N(value.Lower), N(value.Upper)]]))));
        });

    private static readonly AnalysisDefinition OneVariance = DefinitionFactory.Define(
        "one-variance",
        "Varianza de 1 muestra",
        "Pruebas de hipótesis",
        "Contraste de una varianza contra un valor de referencia.",
        [DefinitionFactory.Numeric("x", "Muestra")],
        [],
        [DefinitionFactory.Number("sigma0", "Desviación hipotética (σ₀)", 1)],
        input =>
        {
            var values = input.Num("x");
            var sampleVariance = NumericMath.Sd(values) * NumericMath.Sd(values);
            var nullValue = input.Pnum("sigma0", 1);
            var result = HypothesisTests.OneVariance(sampleVariance, values.Length, nullValue * nullValue);
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "one-variance",
                "Varianza de 1 muestra",
                Sections(
                    Table(
                        "Prueba",
                        ["Estadístico χ²", "GL", "Valor p"],
                        [[N(value.Statistic, 3), N(value.Df, 0), P(value.PValue)]]),
                    Table("IC de σ²", ["Inferior", "Superior"], [[N(value.Lower), N(value.Upper)]]))));
        });

    private static readonly AnalysisDefinition TwoVariances = DefinitionFactory.Define(
        "two-variances",
        "Varianza de 2 muestras",
        "Pruebas de hipótesis",
        "Comparación de dos varianzas mediante la prueba F.",
        [
            DefinitionFactory.Numeric("x1", "Muestra 1"),
            DefinitionFactory.Numeric("x2", "Muestra 2")
        ],
        [],
        [],
        input =>
        {
            var first = input.Num("x1");
            var second = input.Num("x2");
            var variance1 = NumericMath.Sd(first) * NumericMath.Sd(first);
            var variance2 = NumericMath.Sd(second) * NumericMath.Sd(second);
            var result = HypothesisTests.TwoVariances(variance1, first.Length, variance2, second.Length);
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "two-variances",
                "Varianza de 2 muestras",
                Sections(Table(
                    "Prueba F",
                    ["Estadístico F", "GL", "Valor p"],
                    [[N(value.Statistic, 3), N(value.Df, 0), P(value.PValue)]]))));
        });

    private static readonly AnalysisDefinition EquivalenceTost = DefinitionFactory.Define(
        "equivalence-tost",
        "Prueba de equivalencia (TOST)",
        "Pruebas de hipótesis",
        "Dos pruebas t de una cola para demostrar equivalencia con un margen.",
        [
            DefinitionFactory.Numeric("x1", "Muestra 1"),
            DefinitionFactory.Numeric("x2", "Muestra 2")
        ],
        [],
        [DefinitionFactory.Number("margin", "Margen de equivalencia", 1)],
        input =>
        {
            var result = HypothesisTests.TostTwoSample(
                input.Num("x1"),
                input.Num("x2"),
                input.Pnum("margin", 1));
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "equivalence-tost",
                "Prueba de equivalencia (TOST)",
                Sections(Table(
                    "Resultados",
                    ["Diferencia", "p (inferior)", "p (superior)", "p TOST"],
                    [[N(value.Estimate), N(value.LowerP, 4), N(value.UpperP, 4), P(value.PValue)]]))));
        });

    public static IReadOnlyList<AnalysisDefinition> All { get; } =
    [
        OneSampleT,
        OneSampleZ,
        TwoSampleT,
        PairedT,
        OneProportion,
        TwoProportions,
        OneVariance,
        TwoVariances,
        EquivalenceTost
    ];
}
