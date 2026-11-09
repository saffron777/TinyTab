using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses;

/// <summary>
/// Análisis no paramétricos del catálogo (port 1:1 de <c>registry-advanced.ts:659-752</c>).
/// </summary>
public static class NonParametricAnalyses
{
    public static IReadOnlyList<AnalysisDefinition> All { get; } =
    [
        new AnalysisDefinition(
            "nonparam-sign",
            "Prueba de los signos",
            "No paramétricos",
            "Contraste de la mediana mediante la distribución binomial.",
            [new ColumnRequirement("x", "Muestra", true, false)],
            [],
            [new AnalysisParamDefinition("median", "Mediana nula", AnalysisParamKinds.Number, 0.0, [], false)],
            RunSignTest),
        new AnalysisDefinition(
            "nonparam-wilcoxon",
            "Wilcoxon de rangos con signo",
            "No paramétricos",
            "Prueba no paramétrica para muestras pareadas o una muestra.",
            [new ColumnRequirement("x", "Muestra", true, false)],
            [],
            [new AnalysisParamDefinition("median", "Mediana nula", AnalysisParamKinds.Number, 0.0, [], false)],
            RunWilcoxon),
        new AnalysisDefinition(
            "nonparam-mann-whitney",
            "Mann-Whitney",
            "No paramétricos",
            "Comparación de dos muestras independientes por rangos.",
            [
                new ColumnRequirement("x1", "Muestra 1", true, false),
                new ColumnRequirement("x2", "Muestra 2", true, false)
            ],
            [],
            [],
            RunMannWhitney),
        new AnalysisDefinition(
            "nonparam-kruskal-wallis",
            "Kruskal-Wallis",
            "No paramétricos",
            "Comparación de k muestras independientes por rangos.",
            [new ColumnRequirement("y", "Respuesta", true, false)],
            [new ColumnRequirement("factor", "Factor", true, false)],
            [],
            RunKruskalWallis),
        new AnalysisDefinition(
            "nonparam-runs",
            "Prueba de rachas",
            "No paramétricos",
            "Aleatoriedad de una secuencia alrededor de la mediana.",
            [new ColumnRequirement("x", "Secuencia", true, false)],
            [],
            [],
            RunRunsTest),
        new AnalysisDefinition(
            "nonparam-spearman",
            "Correlación de Spearman",
            "No paramétricos",
            "Correlación de rangos entre dos variables.",
            [
                new ColumnRequirement("x", "X", true, false),
                new ColumnRequirement("y", "Y", true, false)
            ],
            [],
            [],
            RunSpearman)
    ];

    private static Result<AnalysisResult> RunSignTest(AnalysisInput input)
    {
        var result = HypothesisTests.SignTest(input.Num("x"), input.Pnum("median", 0));
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;
        var sections = FormatHelpers.Sections(
            FormatHelpers.Table(
                "Resultados",
                ["Positivos", "Negativos", "Valor p"],
                [[
                    JsNumber.ToString(value.Positives),
                    JsNumber.ToString(value.Negatives),
                    FormatHelpers.P(value.PValue)
                ]]));
        return Result.Ok(new AnalysisResult("nonparam-sign", "Prueba de los signos", sections));
    }

    private static Result<AnalysisResult> RunWilcoxon(AnalysisInput input)
    {
        var result = HypothesisTests.WilcoxonSignedRank(input.Num("x"), input.Pnum("median", 0));
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;
        var sections = FormatHelpers.Sections(
            FormatHelpers.Table(
                "Resultado",
                ["W+", "n", "Valor p"],
                [[
                    FormatHelpers.N(value.Statistic, 1),
                    JsNumber.ToString(value.N),
                    FormatHelpers.P(value.PValue)
                ]]));
        return Result.Ok(new AnalysisResult("nonparam-wilcoxon", "Wilcoxon de rangos con signo", sections));
    }

    private static Result<AnalysisResult> RunMannWhitney(AnalysisInput input)
    {
        var result = HypothesisTests.MannWhitney(input.Num("x1"), input.Num("x2"));
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;
        var sections = FormatHelpers.Sections(
            FormatHelpers.Table(
                "Resultado",
                ["U", "Valor p"],
                [[FormatHelpers.N(value.Statistic, 1), FormatHelpers.P(value.PValue)]]));
        return Result.Ok(new AnalysisResult("nonparam-mann-whitney", "Mann-Whitney", sections));
    }

    private static Result<AnalysisResult> RunKruskalWallis(AnalysisInput input)
    {
        var response = input.Num("y");
        var factors = input.ColText("factor");
        // paridad: registry-advanced.ts:715-716 — niveles de texto en orden de aparición.
        var levels = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var factor in factors)
        {
            if (seen.Add(factor)) levels.Add(factor);
        }
        var groups = new List<IReadOnlyList<double>>(levels.Count);
        foreach (var level in levels)
        {
            var group = new List<double>();
            for (var index = 0; index < response.Length; index++)
            {
                if (index < factors.Length && string.Equals(factors[index], level, StringComparison.Ordinal))
                {
                    group.Add(response[index]);
                }
            }
            groups.Add(group);
        }

        var result = HypothesisTests.KruskalWallis(groups);
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;
        var sections = FormatHelpers.Sections(
            FormatHelpers.Table(
                "Resultado",
                ["Estadístico H", "GL", "Valor p"],
                [[
                    FormatHelpers.N(value.Statistic, 3),
                    JsNumber.ToString(value.Df),
                    FormatHelpers.P(value.PValue)
                ]]));
        return Result.Ok(new AnalysisResult("nonparam-kruskal-wallis", "Kruskal-Wallis", sections));
    }

    private static Result<AnalysisResult> RunRunsTest(AnalysisInput input)
    {
        var result = HypothesisTests.RunsTest(input.Num("x"));
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;
        var sections = FormatHelpers.Sections(
            FormatHelpers.Table(
                "Resultado",
                ["Rachas", "Z", "Valor p"],
                [[
                    JsNumber.ToString(value.Runs),
                    FormatHelpers.N(value.Z, 3),
                    FormatHelpers.P(value.PValue)
                ]]));
        return Result.Ok(new AnalysisResult("nonparam-runs", "Prueba de rachas", sections));
    }

    private static Result<AnalysisResult> RunSpearman(AnalysisInput input)
    {
        var result = HypothesisTests.SpearmanCorrelation(input.Num("x"), input.Num("y"));
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;
        var sections = FormatHelpers.Sections(
            FormatHelpers.Table(
                "Resultado",
                ["ρ", "Valor p"],
                [[FormatHelpers.N(value.Rho, 4), FormatHelpers.P(value.PValue)]]));
        return Result.Ok(new AnalysisResult("nonparam-spearman", "Correlación de Spearman", sections));
    }
}
