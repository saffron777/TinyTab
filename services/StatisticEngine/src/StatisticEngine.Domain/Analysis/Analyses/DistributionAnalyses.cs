using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Distributions;
using StatisticEngine.Domain.Numerics;
using static StatisticEngine.Domain.Analysis.FormatHelpers;

namespace StatisticEngine.Domain.Analysis.Analyses;

/// <summary>Distribución de probabilidad (paridad: <c>registry-basic.ts:194-249</c>).</summary>
public static class DistributionAnalyses
{
    private static readonly AnalysisDefinition ProbabilityDistribution = DefinitionFactory.Define(
        "probability-distribution",
        "Distribución de probabilidad",
        "Distribuciones",
        "Funciones de densidad, acumulada e inversa para 21 distribuciones.",
        [],
        [],
        [
            DefinitionFactory.Select(
                "distribution",
                "Distribución",
                "normal",
                [.. DistributionCatalog.All.Select(distribution => DefinitionFactory.Option(distribution.Id, distribution.Label))]),
            DefinitionFactory.Number("x", "Valor de x", 1),
            DefinitionFactory.Number("p", "Probabilidad acumulada", 0.95)
        ],
        input =>
        {
            var id = input.Pstr("distribution", "normal");
            var distribution = DistributionCatalog.All.FirstOrDefault(item => item.Id == id);
            if (distribution is null)
            {
                return Result<AnalysisResult>.Err(DomainErrorCode.InvalidInput, $"Distribución desconocida: {id}");
            }

            var values = DistributionCatalog.DefaultParams(distribution).Values
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach (var parameter in distribution.Parameters)
            {
                var configured = input.Pnum(parameter.Key);
                if (double.IsFinite(configured)) values[parameter.Key] = configured;
            }

            var parameters = new DistParams(values);
            var validation = distribution.Validate(parameters);
            if (validation.IsError)
            {
                return Result<AnalysisResult>.Err(validation.Error.Code, validation.Error.Message);
            }

            var x = input.Pnum("x", 1);
            var probability = input.Pnum("p", 0.95);
            var support = distribution.Support(parameters);
            var step = distribution.Kind == Domain.Distributions.DistributionKind.Continuous ? Math.Abs(support.Lower) : 0;
            var start = double.IsFinite(step) ? Math.Max(step, -20) : -20;

            var chartX = new List<double>();
            for (var index = 0; index <= 60; index++)
            {
                var candidate = double.IsFinite(support.Lower)
                    ? support.Lower + index / 60.0 * (double.IsFinite(support.Upper) ? support.Upper - support.Lower : 20)
                    : start + index;
                if (double.IsFinite(candidate)) chartX.Add(candidate);
            }

            var resultRows = new List<IReadOnlyList<object?>>
            {
                new object?[] { "PDF / PMF", N(distribution.Pdf(x, parameters)) },
                new object?[] { "CDF", N(distribution.Cdf(x, parameters)) },
                new object?[] { "Inversa de CDF", N(distribution.InvCdf(probability, parameters)) },
                new object?[] { "Media teórica", N(distribution.Mean(parameters)) },
                new object?[] { "Desv. estándar teórica", N(distribution.Sd(parameters)) }
            };

            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "probability-distribution",
                $"Distribución {distribution.Label}",
                Sections(
                    Table(
                        "Parámetros",
                        ["Parámetro", "Valor"],
                        distribution.Parameters.Select(parameter => (IReadOnlyList<object?>)
                        [
                            parameter.Label,
                            N(values.TryGetValue(parameter.Key, out var configured) ? configured : double.NaN)
                        ])),
                    Table("Resultados", ["Función", "Valor"], resultRows)),
                Charts:
                [
                    new LineChart(
                        $"Función de densidad — {distribution.Label}",
                        [new LineSeries("f(x)", chartX.Select(value => distribution.Pdf(value, parameters)).ToArray())])
                ]));
        });

    public static IReadOnlyList<AnalysisDefinition> All { get; } = [ProbabilityDistribution];
}
