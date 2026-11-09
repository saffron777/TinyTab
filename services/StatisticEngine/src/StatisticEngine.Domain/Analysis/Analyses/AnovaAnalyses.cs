using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Numerics;
using static StatisticEngine.Domain.Analysis.FormatHelpers;

namespace StatisticEngine.Domain.Analysis.Analyses;

/// <summary>ANOVA de uno y dos factores y homogeneidad de varianzas (paridad: <c>registry-basic.ts:505-593</c>).</summary>
public static class AnovaAnalyses
{
    private static (string[] Levels, double[][] Groups) GroupByFactor(double[] response, string[] factors)
    {
        var levels = new List<string>();
        foreach (var factor in factors)
        {
            if (!levels.Contains(factor)) levels.Add(factor);
        }

        var groups = new double[levels.Count][];
        for (var levelIndex = 0; levelIndex < levels.Count; levelIndex++)
        {
            var level = levels[levelIndex];
            var values = new List<double>();
            for (var index = 0; index < response.Length; index++)
            {
                if (index < factors.Length && factors[index] == level) values.Add(response[index]);
            }
            groups[levelIndex] = [.. values];
        }

        return ([.. levels], groups);
    }

    private static readonly AnalysisDefinition OneWay = DefinitionFactory.Define(
        "anova-one-way",
        "ANOVA de un factor",
        "ANOVA",
        "Comparación de medias entre niveles de un factor, con comparaciones múltiples.",
        [DefinitionFactory.Numeric("y", "Respuesta")],
        [DefinitionFactory.Text("factor", "Factor")],
        [
            DefinitionFactory.Select(
                "posthoc",
                "Comparaciones por pares (Holm)",
                "true",
                DefinitionFactory.Option("true", "Sí"),
                DefinitionFactory.Option("false", "No"))
        ],
        input =>
        {
            var (levels, groups) = GroupByFactor(input.Num("y"), input.ColText("factor"));
            if (levels.Length < 2)
            {
                return Result<AnalysisResult>.Err(DomainErrorCode.InvalidInput, "El factor necesita al menos dos niveles.");
            }

            var result = Anova.OneWayAnova(groups, levels);
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;

            var posthocResult = input.Pbool("posthoc", true)
                ? MultipleComparisons.PairwiseComparisons(groups, levels)
                : (Result<PairwiseComparisonsResult>?)null;

            var posthocSection = posthocResult is { IsOk: true } posthoc
                ? Table(
                    "Comparaciones por pares (Holm)",
                    ["Grupo A", "Grupo B", "Diferencia", "EE", "t", "Valor p", "Sig."],
                    posthoc.Value.Comparisons.Select(comparison => (IReadOnlyList<object?>)
                    [
                        comparison.First,
                        comparison.Second,
                        N(comparison.Difference),
                        N(comparison.StandardError),
                        N(comparison.T, 3),
                        P(comparison.PValue),
                        comparison.Significant ? "Sí" : "No"
                    ]))
                : null;

            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "anova-one-way",
                "ANOVA de un factor",
                Sections(
                    Table(
                        "Estadísticas por grupo",
                        ["Grupo", "N", "Media", "Desv.Est."],
                        value.Groups.Select(group => (IReadOnlyList<object?>)
                        [
                            group.Label,
                            JsNumber.ToString(group.N),
                            N(group.Mean),
                            N(group.Sd)
                        ])),
                    Table(
                        "Tabla ANOVA",
                        ["Fuente", "GL", "SC", "MC", "F", "Valor p"],
                        value.Table.Select(row => (IReadOnlyList<object?>)
                        [
                            row.Source,
                            JsNumber.ToString(row.Df),
                            N(row.Ss, 3),
                            N(row.Ms, 3),
                            N(row.F, 3),
                            P(row.P)
                        ])),
                    Text("R²", $"R² = {N(value.RSquared, 4)} ({JsNumber.ToFixed(value.RSquared * 100, 2)} %)"),
                    posthocSection),
                Charts:
                [
                    new BoxPlotChart(
                        "Diagrama de caja por grupo",
                        value.Groups.Select((group, index) => new BoxPlotGroup(group.Label, groups[index])).ToArray())
                ]));
        });

    private static readonly AnalysisDefinition TwoWay = DefinitionFactory.Define(
        "anova-two-way",
        "ANOVA de dos factores",
        "ANOVA",
        "Modelo aditivo de dos factores (sin interacción).",
        [DefinitionFactory.Numeric("y", "Respuesta")],
        [
            DefinitionFactory.Text("a", "Factor A"),
            DefinitionFactory.Text("b", "Factor B")
        ],
        [],
        input =>
        {
            var result = Anova.TwoWayAnova(input.Num("y"), input.ColText("a"), input.ColText("b"));
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "anova-two-way",
                "ANOVA de dos factores",
                Sections(
                    Table(
                        "Tabla ANOVA",
                        ["Fuente", "GL", "SC", "MC", "F", "Valor p"],
                        value.Table.Select(row => (IReadOnlyList<object?>)
                        [
                            row.Source,
                            JsNumber.ToString(row.Df),
                            N(row.Ss, 3),
                            N(row.Ms, 3),
                            N(row.F, 3),
                            P(row.P)
                        ])),
                    Text("R²", $"R² = {N(value.RSquared, 4)}"))));
        });

    private static readonly AnalysisDefinition EqualVariances = DefinitionFactory.Define(
        "equal-variances",
        "Prueba de igualdad de varianzas",
        "ANOVA",
        "Pruebas de Levene y Bartlett sobre los grupos de un factor.",
        [DefinitionFactory.Numeric("y", "Respuesta")],
        [DefinitionFactory.Text("factor", "Factor")],
        [],
        input =>
        {
            var (levels, groups) = GroupByFactor(input.Num("y"), input.ColText("factor"));
            if (levels.Length < 2)
            {
                return Result<AnalysisResult>.Err(DomainErrorCode.InvalidInput, "El factor necesita al menos dos niveles.");
            }

            var levene = VarianceEquality.LeveneTest(groups);
            var bartlett = VarianceEquality.BartlettTest(groups);

            AnalysisSection leveneSection = levene.IsOk
                ? Table(
                    "Levene",
                    ["Estadístico", "GL", "Valor p"],
                    [[N(levene.Value.Statistic, 3), JsNumber.ToString(levene.Value.Df), P(levene.Value.PValue)]])
                : Text("Levene", levene.Error.Message);

            AnalysisSection bartlettSection = bartlett.IsOk
                ? Table(
                    "Bartlett",
                    ["Estadístico", "GL", "Valor p"],
                    [[N(bartlett.Value.Statistic, 3), JsNumber.ToString(bartlett.Value.Df), P(bartlett.Value.PValue)]])
                : Text("Bartlett", bartlett.Error.Message);

            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "equal-variances",
                "Prueba de igualdad de varianzas",
                Sections(leveneSection, bartlettSection)));
        });

    public static IReadOnlyList<AnalysisDefinition> All { get; } = [OneWay, TwoWay, EqualVariances];
}
