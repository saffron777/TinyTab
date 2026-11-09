using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Power;

namespace StatisticEngine.Domain.Analysis.Analyses;

/// <summary>
/// Análisis de potencia y tamaño de muestra del catálogo
/// (port 1:1 de <c>registry-advanced.ts:565-657</c>).
/// </summary>
public static class PowerAnalyses
{
    public static IReadOnlyList<AnalysisDefinition> All { get; } =
    [
        new AnalysisDefinition(
            "power-t1",
            "Potencia y tamaño de muestra (t de 1 muestra)",
            "Potencia y tamaño de muestra",
            "Potencia para un efecto y n dados, y n necesario para una potencia objetivo.",
            [],
            [],
            [
                new AnalysisParamDefinition("effect", "Tamaño del efecto (d)", AnalysisParamKinds.Number, 0.5, [], false),
                new AnalysisParamDefinition("n", "Tamaño de muestra", AnalysisParamKinds.Integer, 20.0, [], false),
                new AnalysisParamDefinition("power", "Potencia objetivo", AnalysisParamKinds.Number, 0.8, [], false),
                new AnalysisParamDefinition("alpha", "α", AnalysisParamKinds.Number, 0.05, [], false)
            ],
            RunPowerOneSampleT),
        new AnalysisDefinition(
            "power-prop1",
            "Potencia y tamaño de muestra (proporción)",
            "Potencia y tamaño de muestra",
            "Potencia para una prueba de una proporción.",
            [],
            [],
            [
                new AnalysisParamDefinition("p0", "Proporción nula", AnalysisParamKinds.Number, 0.5, [], false),
                new AnalysisParamDefinition("p1", "Proporción alternativa", AnalysisParamKinds.Number, 0.6, [], false),
                new AnalysisParamDefinition("n", "Tamaño de muestra", AnalysisParamKinds.Integer, 100.0, [], false),
                new AnalysisParamDefinition("power", "Potencia objetivo", AnalysisParamKinds.Number, 0.8, [], false)
            ],
            RunPowerOneProportion),
        new AnalysisDefinition(
            "power-anova1",
            "Potencia y tamaño de muestra (ANOVA de 1 factor)",
            "Potencia y tamaño de muestra",
            "Aproximación para k grupos con efecto f de Cohen.",
            [],
            [],
            [
                new AnalysisParamDefinition("effect", "Tamaño del efecto (f)", AnalysisParamKinds.Number, 0.25, [], false),
                new AnalysisParamDefinition("groups", "Número de grupos", AnalysisParamKinds.Integer, 3.0, [], false),
                new AnalysisParamDefinition("n", "n por grupo", AnalysisParamKinds.Integer, 20.0, [], false)
            ],
            RunPowerOneWayAnova),
        new AnalysisDefinition(
            "power-t2",
            "Tamaño de muestra (t de 2 muestras)",
            "Potencia y tamaño de muestra",
            "n por grupo para detectar un efecto con potencia objetivo.",
            [],
            [],
            [
                new AnalysisParamDefinition("effect", "Tamaño del efecto (d)", AnalysisParamKinds.Number, 0.5, [], false),
                new AnalysisParamDefinition("power", "Potencia objetivo", AnalysisParamKinds.Number, 0.8, [], false)
            ],
            RunSampleSizeTwoSampleT)
    ];

    private static Result<AnalysisResult> RunPowerOneSampleT(AnalysisInput input)
    {
        var effect = input.Pnum("effect", 0.5);
        var alpha = input.Pnum("alpha", 0.05);
        var power = PowerAnalysis.PowerOneSampleT(effect, Math.Max(2, (int)Math.Floor(input.Pnum("n", 20))), alpha);
        var sample = PowerAnalysis.SampleSizeOneSampleT(effect, input.Pnum("power", 0.8), alpha);
        if (!power.IsOk || !sample.IsOk) return Result<AnalysisResult>.Err(DomainErrorCode.InvalidParameter, "Parámetros inválidos.");

        var sections = FormatHelpers.Sections(
            FormatHelpers.Table(
                "Potencia",
                ["Efecto", "n", "α", "Potencia"],
                [[
                    FormatHelpers.N(effect, 3),
                    JsNumber.ToString(power.Value.N),
                    FormatHelpers.N(alpha, 3),
                    FormatHelpers.N(power.Value.Power, 4)
                ]]),
            FormatHelpers.Table(
                "Tamaño de muestra necesario",
                ["Efecto", "Potencia objetivo", "n"],
                [[
                    FormatHelpers.N(effect, 3),
                    FormatHelpers.N(sample.Value.Power, 2),
                    JsNumber.ToString(sample.Value.N)
                ]]));

        var sizes = new double[] { 5, 10, 15, 20, 30, 40, 50, 75, 100 };
        var curve = new double[sizes.Length];
        var points = PowerAnalysis.OcCurve(effect, sizes, alpha);
        for (var index = 0; index < points.Length; index++) curve[index] = points[index].Power;
        return Result.Ok(new AnalysisResult(
            "power-t1",
            "Potencia (t de 1 muestra)",
            sections,
            Charts: [new LineChart("Curva de potencia", [new LineSeries("Potencia", curve)], "n")]));
    }

    private static Result<AnalysisResult> RunPowerOneProportion(AnalysisInput input)
    {
        var p0 = input.Pnum("p0", 0.5);
        var p1 = input.Pnum("p1", 0.6);
        var power = PowerAnalysis.PowerOneProportion(p0, p1, Math.Max(1, (int)Math.Floor(input.Pnum("n", 100))));
        var sample = PowerAnalysis.SampleSizeOneProportion(p0, p1, input.Pnum("power", 0.8));
        if (!power.IsOk || !sample.IsOk) return Result<AnalysisResult>.Err(DomainErrorCode.InvalidParameter, "Parámetros inválidos.");

        var sections = FormatHelpers.Sections(
            FormatHelpers.Table(
                "Potencia",
                ["p₀", "p₁", "n", "Potencia"],
                [[
                    FormatHelpers.N(p0, 3),
                    FormatHelpers.N(p1, 3),
                    JsNumber.ToString(power.Value.N),
                    FormatHelpers.N(power.Value.Power, 4)
                ]]),
            FormatHelpers.Table(
                "Tamaño de muestra necesario",
                ["p₀", "p₁", "Potencia", "n"],
                [[
                    FormatHelpers.N(p0, 3),
                    FormatHelpers.N(p1, 3),
                    FormatHelpers.N(sample.Value.Power, 2),
                    JsNumber.ToString(sample.Value.N)
                ]]));
        return Result.Ok(new AnalysisResult(
            "power-prop1",
            "Potencia (proporción de 1 muestra)",
            sections));
    }

    private static Result<AnalysisResult> RunPowerOneWayAnova(AnalysisInput input)
    {
        var result = PowerAnalysis.PowerOneWayAnova(
            input.Pnum("effect", 0.25),
            Math.Max(2, (int)Math.Floor(input.Pnum("n", 20))),
            Math.Max(2, (int)Math.Floor(input.Pnum("groups", 3))));
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);

        var sections = FormatHelpers.Sections(
            FormatHelpers.Table(
                "Resultado",
                ["Efecto", "Grupos", "n por grupo", "Potencia"],
                [[
                    FormatHelpers.N(result.Value.Effect, 3),
                    // paridad: registry-advanced.ts:638 — el TS imprime pnum('groups') sin redondear.
                    JsNumber.ToString(input.Pnum("groups", 3)),
                    JsNumber.ToString(result.Value.N),
                    FormatHelpers.N(result.Value.Power, 4)
                ]]));
        return Result.Ok(new AnalysisResult(
            "power-anova1",
            "Potencia (ANOVA de 1 factor)",
            sections));
    }

    private static Result<AnalysisResult> RunSampleSizeTwoSampleT(AnalysisInput input)
    {
        var result = PowerAnalysis.SampleSizeTwoSampleT(input.Pnum("effect", 0.5), input.Pnum("power", 0.8));
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);

        var sections = FormatHelpers.Sections(
            FormatHelpers.Table(
                "Resultado",
                ["Efecto", "Potencia", "n por grupo"],
                [[
                    FormatHelpers.N(result.Value.Effect, 3),
                    FormatHelpers.N(result.Value.Power, 2),
                    JsNumber.ToString(result.Value.N)
                ]]));
        return Result.Ok(new AnalysisResult(
            "power-t2",
            "Tamaño de muestra (t de 2 muestras)",
            sections));
    }
}
