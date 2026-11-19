using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Supervivencia paramétrica con censura (Weibull por máxima verosimilitud).</summary>
internal static class ParametricSurvivalAnalyses
{
    public static readonly AnalysisDefinition Parametric = new(
        "parametric",
        "Supervivencia paramétrica (Weibull, exponencial, log-normal, log-logística)",
        "Supervivencia paramétrica",
        "Ajusta un modelo paramétrico de supervivencia por máxima verosimilitud con censura y lo compara con Kaplan-Meier.",
        [],
        [],
        [
            new(
                "distribution",
                "Distribución",
                AnalysisParamKinds.Select,
                "weibull",
                [
                    new("weibull", "Weibull"), new("expon", "Exponencial"),
                    new("lognorm", "Log-normal"), new("loglogistic", "Log-logística")
                ],
                false),
            ModelHelpers.Alpha()
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Supervivencia paramétrica",
        Roles:
        [
            new ColumnRole("time", "Tiempo hasta el evento", ColumnKinds.Numeric),
            new ColumnRole("event", "Evento (1 = ocurrió, 0 = censurado)", ColumnKinds.Numeric)
        ],
        Outputs: [new AnalysisOutputSpec("fitted", "Supervivencia ajustada")]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        if (input.Pstr("distribution", "weibull") != "weibull")
        {
            return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "Por ahora la supervivencia paramétrica solo ajusta la distribución Weibull.");
        }
        var frame = input.AsFrame();
        var timeName = ExtendedHelpers.FirstRole(input, "time", "Tiempo hasta el evento");
        var eventName = ExtendedHelpers.FirstRole(input, "event", "Evento (1 = ocurrió, 0 = censurado)");
        var complete = frame.CompleteCases([timeName, eventName]);
        var times = complete.Numeric(timeName);
        var events = complete.Numeric(eventName);
        var n = times.Length;
        var eventCount = events.Count(value => value > 0);
        if (n < 10) return ExtendedHelpers.Invalid("Se necesitan al menos 10 observaciones.");
        var logs = times.Select(value => Math.Log(value)).ToArray();
        var parameters = Maximize(times, events);
        var shape = parameters[0];
        var scale = parameters[1];
        var logLikelihood = LogLikelihood(times, events, shape, scale);
        var median = scale * Math.Pow(Math.Log(2), 1 / shape);
        var mean = scale * Math.Exp(SpecialFunctions.LogGamma(1 + 1 / shape));
        var grid = Enumerable.Range(0, 200).Select(index => 1e-6 + (times.Max() - 1e-6) * index / 199.0).ToArray();
        var survival = grid.Select(time => Math.Exp(-Math.Pow(time / scale, shape))).ToArray();
        var fitted = new DatasetResult(
            "Supervivencia ajustada",
            [
                DataColumn.Numeric("tiempo", grid),
                DataColumn.Numeric("supervivencia", survival)
            ]);
        return Result.Ok(new AnalysisResult(
            "parametric",
            "Supervivencia paramétrica: Weibull",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Observaciones", JsNumber.ToString(n)),
                    new TileItem("Eventos", JsNumber.ToString(eventCount)),
                    new TileItem("Mediana estimada", ModelHelpers.N(median)),
                    new TileItem("Media estimada", ModelHelpers.N(mean))
                ]),
                new TableSection(
                    "Parámetros",
                    ["Parámetro", "Estimación"],
                    [
                        new[] { "forma (k)", ModelHelpers.N(shape) },
                        new[] { "escala (λ)", ModelHelpers.N(scale) }
                    ],
                    Note: "Estimados maximizando la verosimilitud con censura.")
            ],
            Subtitle: $"n = {n} · {eventCount} eventos · máxima verosimilitud con censura",
            Charts:
            [
                new LineChart("Supervivencia ajustada", [new LineSeries("Weibull", survival)], "Tiempo")
            ],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal) { ["fitted"] = fitted },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["events"] = eventCount,
                ["distribution"] = "weibull",
                ["median"] = median,
                ["mean"] = mean,
                ["loglikelihood"] = logLikelihood
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double LogLikelihood(double[] times, double[] events, double shape, double scale)
    {
        var total = 0.0;
        for (var index = 0; index < times.Length; index++)
        {
            var ratio = times[index] / scale;
            if (events[index] > 0) total += Math.Log(shape) - Math.Log(scale) + (shape - 1) * Math.Log(ratio);
            total -= Math.Pow(ratio, shape);
        }
        return total;
    }

    private static double[] Maximize(double[] times, double[] events)
    {
        double Objective(double[] values)
        {
            if (values[0] <= -20 || values[1] <= -20) return 1e12;
            return -LogLikelihood(times, events, Math.Exp(values[0]), Math.Exp(values[1]));
        }
        var start = new[] { 0.0, Math.Log(times.Average()) };
        var simplex = new double[3][];
        simplex[0] = start;
        for (var index = 0; index < 2; index++)
        {
            simplex[index + 1] = (double[])start.Clone();
            simplex[index + 1][index] = start[index] != 0 ? start[index] * 1.05 : 0.00025;
        }
        for (var iteration = 0; iteration < 400; iteration++)
        {
            simplex = simplex.OrderBy(Objective).ToArray();
            var spread = Math.Max(Math.Abs(Objective(simplex[1]) - Objective(simplex[0])), Math.Abs(Objective(simplex[2]) - Objective(simplex[0])));
            var distance = Math.Max(
                Math.Max(Math.Abs(simplex[1][0] - simplex[0][0]), Math.Abs(simplex[1][1] - simplex[0][1])),
                Math.Max(Math.Abs(simplex[2][0] - simplex[0][0]), Math.Abs(simplex[2][1] - simplex[0][1])));
            if (spread <= 1e-4 && distance <= 1e-4) break;
            var centroid = new[]
            {
                (simplex[0][0] + simplex[1][0]) / 2,
                (simplex[0][1] + simplex[1][1]) / 2
            };
            var reflected = new[] { centroid[0] + (centroid[0] - simplex[2][0]), centroid[1] + (centroid[1] - simplex[2][1]) };
            var reflectedValue = Objective(reflected);
            if (reflectedValue < Objective(simplex[0]))
            {
                var expanded = new[] { centroid[0] + 2 * (reflected[0] - centroid[0]), centroid[1] + 2 * (reflected[1] - centroid[1]) };
                simplex[2] = Objective(expanded) < reflectedValue ? expanded : reflected;
            }
            else if (reflectedValue < Objective(simplex[1]))
            {
                simplex[2] = reflected;
            }
            else
            {
                var contracted = reflectedValue < Objective(simplex[2])
                    ? new[] { centroid[0] + 0.5 * (reflected[0] - centroid[0]), centroid[1] + 0.5 * (reflected[1] - centroid[1]) }
                    : new[] { centroid[0] + 0.5 * (simplex[2][0] - centroid[0]), centroid[1] + 0.5 * (simplex[2][1] - centroid[1]) };
                if (Objective(contracted) < Math.Min(reflectedValue, Objective(simplex[2])))
                {
                    simplex[2] = contracted;
                }
                else
                {
                    for (var index = 1; index < 3; index++)
                    {
                        simplex[index] = [simplex[0][0] + 0.5 * (simplex[index][0] - simplex[0][0]), simplex[0][1] + 0.5 * (simplex[index][1] - simplex[0][1])];
                    }
                }
            }
        }
        simplex = simplex.OrderBy(Objective).ToArray();
        return [Math.Exp(simplex[0][0]), Math.Exp(simplex[0][1])];
    }
}
