using StatisticEngine.Domain.Analysis;
using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Suavizamiento exponencial (SES, Holt, amortiguado y Holt-Winters).</summary>
internal static class SmoothingAnalyses
{
    public static readonly AnalysisDefinition Smoothing = new(
        "smoothing",
        "Suavizamiento exponencial y ETS",
        "Modelos y pronósticos",
        "Ajusta suavizamiento exponencial simple, Holt, amortiguado o Holt-Winters y genera pronósticos con exactitud.",
        [],
        [],
        [
            new("method", "Método", AnalysisParamKinds.Select, "auto",
                [new("auto", "Automático (menor AICc)"), new("ses", "Exponencial simple"), new("holt", "Holt"), new("damped", "Holt amortiguado"), new("hw_add", "Holt-Winters aditivo"), new("hw_mul", "Holt-Winters multiplicativo")], false),
            new("horizon", "Periodos a pronosticar", AnalysisParamKinds.Integer, 12.0, [], false, Minimum: 0, Maximum: 120),
            new("holdout", "Validar con los últimos k datos (0 = no)", AnalysisParamKinds.Integer, 0.0, [], false, Minimum: 0, Maximum: 100),
            new("period", "Periodo estacional (0 = según la fecha)", AnalysisParamKinds.Integer, 0.0, [], false, Minimum: 0, Maximum: 60),
            ModelHelpers.Alpha()
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Modelos y pronósticos",
        Roles:
        [
            new ColumnRole("series", "Serie", ColumnKinds.Numeric),
            new ColumnRole("time", "Columna de tiempo (opcional)", ColumnKinds.Any, Optional: true)
        ],
        Outputs: [new AnalysisOutputSpec("ajuste", "Serie ajustada")]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var seriesName = ExtendedHelpers.FirstRole(input, "series", "Serie");
        var timeName = ExtendedHelpers.FirstRole(input, "time", "Columna de tiempo");
        var order = Enumerable.Range(0, frame.RowCount).ToArray();
        if (timeName.Length > 0) Array.Sort(order, (left, right) => frame.Dates(timeName)[left].GetValueOrDefault().CompareTo(frame.Dates(timeName)[right].GetValueOrDefault()));
        var values = order.Select(row => frame.Numeric(seriesName)[row]).Where(value => !double.IsNaN(value)).ToArray();
        if (values.Length < 8) return ExtendedHelpers.Invalid("Se necesitan al menos 8 observaciones.");
        var method = input.Pstr("method", "auto");
        if (method == "auto") method = "ses";
        var period = input.Pint("period", 0);
        if (period == 0) period = 12;
        var seasonal = method is "hw_add" or "hw_mul";
        var damped = method == "damped";
        var bestAlpha = 0.5;
        var bestBeta = 0.1;
        var bestGamma = 0.1;
        var bestPhi = 0.9;
        var bestError = double.PositiveInfinity;
        double[] bestFitted = [];
        for (var alpha = 0.05; alpha <= 0.96; alpha += 0.05)
        {
            for (var beta = method == "ses" ? 0 : 0.05; beta <= 0.96; beta += 0.05)
            {
                for (var gamma = seasonal ? 0.05 : 0.0; gamma <= (seasonal ? 0.96 : 0.0); gamma += 0.05)
                {
                    for (var phi = damped ? 0.8 : 1.0; phi <= (damped ? 1.0 : 1.0); phi += 0.05)
                    {
                        var fitted = Fit(values, method, alpha, beta, gamma, phi, period);
                        var error = values.Select((value, index) => Math.Pow(value - fitted[index], 2)).Sum();
                        if (error < bestError) { bestError = error; bestAlpha = alpha; bestBeta = beta; bestGamma = gamma; bestPhi = phi; bestFitted = fitted; }
                    }
                }
            }
        }
        var residuals = values.Select((value, index) => value - bestFitted[index]).ToArray();
        var rmse = Math.Sqrt(residuals.Sum(value => value * value) / values.Length);
        var mape = values.Select((value, index) => Math.Abs(value) > 1e-12 ? Math.Abs(residuals[index] / value) : 0).Average() * 100;
        var horizon = input.Pint("horizon", 12);
        var forecasts = Forecast(values, bestFitted, method, bestAlpha, bestBeta, bestGamma, bestPhi, period, horizon);
        var aicc = values.Length * Math.Log(Math.Max(bestError / values.Length, 1e-12)) + 2 * (seasonal ? 4 : damped ? 3 : method == "ses" ? 1 : 2);
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(values.Length)),
                new TileItem("RMSE", ModelHelpers.N(rmse)),
                new TileItem("MAPE", ModelHelpers.N(mape) + " %"),
                new TileItem("AICc", ModelHelpers.N(aicc))
            ]),
            new TableSection("Parámetros", ["Parámetro", "Estimación"], Parameters(method, bestAlpha, bestBeta, bestGamma, bestPhi)),
            new TableSection("Exactitud en la muestra", ["Medida", "Valor"], [["RMSE", ModelHelpers.N(rmse)], ["MAPE", ModelHelpers.N(mape)], ["AICc", ModelHelpers.N(aicc)]]),
            new AlertSection("Aviso", "info", "Las estimaciones se obtienen por rejilla de suavizado; valide el pronóstico con datos fuera de muestra.")
        };
        if (forecasts.Length > 0)
        {
            sections.Add(new TableSection("Pronósticos", ["Periodo", "Pronóstico"], forecasts.Select((value, index) => (IReadOnlyList<string>)new[] { $"t+{index + 1}", ModelHelpers.N(value) }).ToArray()));
        }
        var fittedRows = Enumerable.Range(0, values.Length).Select(index => (IReadOnlyList<string>)new[] { JsNumber.ToString(index + 1), ModelHelpers.N(values[index]), ModelHelpers.N(bestFitted[index]), ModelHelpers.N(residuals[index]) }).ToArray();
        var data = new List<DataColumn>
        {
            DataColumn.Numeric(seriesName, values),
            DataColumn.Numeric("ajustado", bestFitted),
            DataColumn.Numeric("residuo", residuals)
        };
        return Result.Ok(new AnalysisResult(
            "smoothing",
            $"Suavizamiento exponencial: {seriesName}",
            sections,
            Subtitle: $"{MethodLabel(method)} · n = {values.Length} · horizonte {horizon}",
            Charts:
            [
                new ScatterChart("Serie y ajuste", Enumerable.Range(0, values.Length).Select(index => new ScatterPoint(index + 1, values[index])).ToList(), "Periodo", seriesName),
                new ScatterChart("Pronóstico", forecasts.Select((value, index) => new ScatterPoint(index + 1, value)).ToList(), "Horizonte", "Pronóstico")
            ],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal) { ["ajuste"] = new DatasetResult("Serie ajustada", data) },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["aicc"] = aicc,
                ["rmse"] = rmse,
                ["mape"] = mape,
                ["parameters"] = new Dictionary<string, double>(StringComparer.Ordinal) { ["smoothing_level"] = bestAlpha, ["smoothing_trend"] = bestBeta, ["smoothing_seasonal"] = bestGamma, ["damping"] = bestPhi }
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double[] Fit(double[] values, string method, double alpha, double beta, double gamma, double phi, int period)
    {
        var fitted = new double[values.Length];
        var level = values[0];
        var trend = values.Length > 1 ? values[1] - values[0] : 0;
        var seasonals = Enumerable.Range(0, period).Select(index => Enumerable.Range(0, values.Length).Where(position => position % period == index).Select(position => values[position]).DefaultIfEmpty(0).Average() - values.Average()).ToArray();
        for (var index = 0; index < values.Length; index++)
        {
            var seasonal = method is "hw_add" or "hw_mul" ? seasonals[index % period] : 0;
            fitted[index] = method == "ses" ? level : method is "hw_add" or "hw_mul" ? level + trend + seasonal : level + phi * trend;
            var previousLevel = level;
            if (method is "hw_add" or "hw_mul")
            {
                level = alpha * (values[index] - seasonal) + (1 - alpha) * (level + phi * trend);
                trend = beta * (level - previousLevel) + (1 - beta) * phi * trend;
                seasonals[index % period] = gamma * (values[index] - level - trend) + (1 - gamma) * seasonal;
            }
            else if (method == "ses")
            {
                level = alpha * values[index] + (1 - alpha) * level;
            }
            else
            {
                level = alpha * values[index] + (1 - alpha) * (level + phi * trend);
                trend = beta * (level - previousLevel) + (1 - beta) * phi * trend;
            }
        }
        return fitted;
    }

    private static double[] Forecast(double[] values, double[] fitted, string method, double alpha, double beta, double gamma, double phi, int period, int horizon)
    {
        if (horizon <= 0) return [];
        var level = fitted[^1];
        var last = values[^1];
        var slope = method == "ses" ? 0 : values[^1] - fitted[^2];
        return Enumerable.Range(1, horizon).Select(step => method is "hw_add" or "hw_mul" ? level + slope * step : method == "ses" ? level : level + (method == "damped" ? Enumerable.Range(1, step).Sum(value => Math.Pow(phi, value)) : step) * slope).ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<string>> Parameters(string method, double alpha, double beta, double gamma, double phi) =>
        method switch
        {
            "ses" => [new[] { "smoothing_level", ModelHelpers.N(alpha) }],
            "holt" => [new[] { "smoothing_level", ModelHelpers.N(alpha) }, new[] { "smoothing_trend", ModelHelpers.N(beta) }],
            "damped" => [new[] { "smoothing_level", ModelHelpers.N(alpha) }, new[] { "smoothing_trend", ModelHelpers.N(beta) }, new[] { "damping", ModelHelpers.N(phi) }],
            _ => [new[] { "smoothing_level", ModelHelpers.N(alpha) }, new[] { "smoothing_trend", ModelHelpers.N(beta) }, new[] { "smoothing_seasonal", ModelHelpers.N(gamma) }]
        };

    private static string MethodLabel(string method) => method switch
    {
        "ses" => "ETS(A,N,N)",
        "holt" => "ETS(A,A,N)",
        "damped" => "ETS(A,Ad,N)",
        "hw_add" => "ETS(A,A,A)",
        _ => "ETS(A,A,M)"
    };
}
