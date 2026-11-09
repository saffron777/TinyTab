using StatisticEngine.Domain.Msa;
using StatisticEngine.Domain.Numerics;
using static StatisticEngine.Domain.Analysis.FormatHelpers;

namespace StatisticEngine.Domain.Analysis.Analyses;

/// <summary>Gage R&amp;R cruzado (paridad: <c>registry-advanced.ts:203-230</c>).</summary>
public static class MsaAnalyses
{
    private static readonly AnalysisDefinition GageRRDefinition = DefinitionFactory.Define(
        "gage-rr",
        "Gage R&R (cruzado)",
        "Sistemas de medida",
        "Estudio de repetibilidad y reproducibilidad por ANOVA.",
        [DefinitionFactory.Numeric("value", "Medición")],
        [
            DefinitionFactory.Text("part", "Pieza"),
            DefinitionFactory.Text("operator", "Operador")
        ],
        [DefinitionFactory.Param("trial", "Columna de repetición (opcional)", AnalysisParamKinds.Number, string.Empty, true)],
        input =>
        {
            var values = input.Num("value");
            var parts = input.ColText("part");
            var operators = input.ColText("operator");
            var rows = new GageRow[values.Length];
            for (var index = 0; index < values.Length; index++)
            {
                rows[index] = new GageRow(
                    Part: index < parts.Length ? parts[index] : JsNumber.ToString(index),
                    Operator: index < operators.Length ? operators[index] : "A",
                    Trial: index,
                    Value: values[index]);
            }

            var result = GageRR.GageRRStudy(rows);
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "gage-rr",
                "Gage R&R (cruzado)",
                Sections(
                    Table(
                        "Resumen",
                        [
                            "Repetibilidad (EV)",
                            "Reproducibilidad (AV)",
                            "GRR",
                            "Variación total",
                            "% Contribución",
                            "% Estudio",
                            "nDC"
                        ],
                        [[
                            N(value.Repeatability),
                            N(value.Reproducibility),
                            N(value.Grr),
                            N(value.TotalVariation),
                            N(value.PercentContribution, 2),
                            N(value.PercentStudy, 2),
                            N(value.Ndc, 2)
                        ]]),
                    Table(
                        "ANOVA",
                        ["Fuente", "GL", "SC", "MC", "F", "Valor p"],
                        value.Table.Select(row => (IReadOnlyList<object?>)
                        [
                            row.Source,
                            JsNumber.ToString(row.Df),
                            N(row.Ss, 3),
                            N(row.Ms, 3),
                            N(row.F, 3),
                            P(row.P)
                        ])))));
        });

    public static IReadOnlyList<AnalysisDefinition> All { get; } = [GageRRDefinition];
}
