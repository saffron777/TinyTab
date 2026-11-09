using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Quality;
using static StatisticEngine.Domain.Analysis.FormatHelpers;

namespace StatisticEngine.Domain.Analysis.Analyses;

/// <summary>Cartas de control (paridad: <c>registry-advanced.ts:15-168</c>).</summary>
public static class ControlChartAnalyses
{
    private static IReadOnlyList<ControlPointSpec> Points(ControlChartResult chart) =>
        chart.Points.Select(point => new ControlPointSpec(point.Index, point.Value)).ToArray();

    private static int[] Violations(ControlChartResult chart) =>
        chart.Points.Where(point => point.OutOfControl).Select(point => point.Index).ToArray();

    private static ChartSpec ControlChartOf(ControlChartResult chart, string title, string yLabel) =>
        new ControlChart(title, Points(chart), chart.CenterLine, chart.LowerLimit, chart.UpperLimit, Violations(chart), yLabel);

    private static IReadOnlyList<object?> Limits(ControlChartResult chart) =>
        [N(chart.CenterLine), N(chart.UpperLimit), N(chart.LowerLimit)];

    private static readonly AnalysisDefinition XbarR = DefinitionFactory.Define(
        "chart-xbar-r",
        "Carta X̄-R",
        "Control de calidad",
        "Cartas de medias y rangos para subgrupos de tamaño constante.",
        [DefinitionFactory.Numeric("x", "Mediciones (por subgrupos consecutivos)")],
        [],
        [DefinitionFactory.Integer("size", "Tamaño del subgrupo", 5)],
        input =>
        {
            var values = input.Num("x");
            var size = Math.Max(2, (int)Math.Floor(input.Pnum("size", 5)));
            var groups = new List<SubgroupPoint>();
            for (var index = 0; index + size <= values.Length; index += size)
            {
                groups.Add(new SubgroupPoint(values[index..(index + size)]));
            }

            var result = ControlCharts.XbarRChart(groups);
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "chart-xbar-r",
                "Carta X̄-R",
                Sections(
                    Table("Límites X̄", ["Línea central", "LSC", "LIC"], [Limits(value.Xbar)]),
                    Table("Límites R", ["Línea central", "LSC", "LIC"], [Limits(value.Range)]),
                    Table(
                        "Detección de causas especiales",
                        ["Carta", "Puntos", "Violaciones X̄", "Violaciones R"],
                        [["X̄-R", JsNumber.ToString(groups.Count), JsNumber.ToString(value.Xbar.Violations), JsNumber.ToString(value.Range.Violations)]])),
                Charts:
                [
                    ControlChartOf(value.Xbar, "Carta X̄", "Media"),
                    ControlChartOf(value.Range, "Carta R", "Rango")
                ]));
        });

    private static readonly AnalysisDefinition Individuals = DefinitionFactory.Define(
        "chart-imr",
        "Carta I-MR",
        "Control de calidad",
        "Carta de individuos y rango móvil.",
        [DefinitionFactory.Numeric("x", "Observaciones")],
        [],
        [],
        input =>
        {
            var result = ControlCharts.IndividualsChart(input.Num("x"));
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "chart-imr",
                "Carta I-MR",
                Sections(
                    Table("Carta I", ["Línea central", "LSC", "LIC"], [Limits(value.Individuals)]),
                    Table("Carta MR", ["Línea central", "LSC", "LIC"], [Limits(value.MovingRange)])),
                Charts:
                [
                    ControlChartOf(value.Individuals, "Carta I", "Valor"),
                    ControlChartOf(value.MovingRange, "Carta MR", "Rango móvil")
                ]));
        });

    private static readonly AnalysisDefinition P = DefinitionFactory.Define(
        "chart-p",
        "Carta P",
        "Control de calidad",
        "Proporción de defectuosos con subgrupos de distinto tamaño.",
        [
            DefinitionFactory.Numeric("defective", "Defectuosos"),
            DefinitionFactory.Numeric("sample", "Tamaño de muestra")
        ],
        [],
        [],
        input =>
        {
            var result = ControlCharts.PChart(input.Num("defective"), input.Num("sample"));
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "chart-p",
                "Carta P",
                Sections(
                    Table("Límites", ["Línea central", "LSC", "LIC"], [Limits(value)]),
                    Table(
                        "Violaciones",
                        ["Puntos fuera", "Total"],
                        [[JsNumber.ToString(value.Violations), JsNumber.ToString(value.Points.Length)]])),
                Charts: [ControlChartOf(value, "Carta P", "Proporción")]));
        });

    private static readonly AnalysisDefinition C = DefinitionFactory.Define(
        "chart-c",
        "Carta C",
        "Control de calidad",
        "Número de defectos por unidad constante.",
        [DefinitionFactory.Numeric("defects", "Defectos")],
        [],
        [],
        input =>
        {
            var result = ControlCharts.CChart(input.Num("defects"));
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "chart-c",
                "Carta C",
                Sections(Table("Límites", ["Línea central", "LSC", "LIC"], [Limits(value)])),
                Charts: [ControlChartOf(value, "Carta C", "Defectos")]));
        });

    private static readonly AnalysisDefinition U = DefinitionFactory.Define(
        "chart-u",
        "Carta U",
        "Control de calidad",
        "Defectos por unidad cuando el tamaño de la unidad varía.",
        [
            DefinitionFactory.Numeric("defects", "Defectos"),
            DefinitionFactory.Numeric("units", "Unidades inspeccionadas")
        ],
        [],
        [],
        input =>
        {
            var result = ControlCharts.UChart(input.Num("defects"), input.Num("units"));
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "chart-u",
                "Carta U",
                Sections(Table("Límites", ["Línea central", "LSC", "LIC"], [Limits(value)])),
                Charts: [ControlChartOf(value, "Carta U", "Defectos/unidad")]));
        });

    private static readonly AnalysisDefinition Ewma = DefinitionFactory.Define(
        "chart-ewma",
        "Carta EWMA",
        "Control de calidad",
        "Media móvil exponencialmente ponderada.",
        [DefinitionFactory.Numeric("x", "Observaciones")],
        [],
        [DefinitionFactory.Number("lambda", "Peso λ", 0.2)],
        input =>
        {
            var result = ControlCharts.EwmaChart(input.Num("x"), input.Pnum("lambda", 0.2));
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "chart-ewma",
                "Carta EWMA",
                Sections(Table("Límites", ["Línea central", "LSC", "LIC"], [Limits(value)])),
                Charts: [ControlChartOf(value, "Carta EWMA", "EWMA")]));
        });

    private static readonly AnalysisDefinition Cusum = DefinitionFactory.Define(
        "chart-cusum",
        "Carta CUSUM",
        "Control de calidad",
        "Sumas acumuladas tabulares (k = 0,5).",
        [DefinitionFactory.Numeric("x", "Observaciones")],
        [],
        [DefinitionFactory.Number("k", "Valor de referencia k", 0.5)],
        input =>
        {
            var result = ControlCharts.CusumChart(input.Num("x"), null, input.Pnum("k", 0.5));
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "chart-cusum",
                "Carta CUSUM",
                Sections(Table(
                    "Resumen",
                    ["Puntos", "Violaciones"],
                    [[JsNumber.ToString(value.Points.Length), JsNumber.ToString(value.Violations)]]))));
        });

    public static IReadOnlyList<AnalysisDefinition> All { get; } =
    [
        XbarR,
        Individuals,
        P,
        C,
        U,
        Ewma,
        Cusum
    ];
}
