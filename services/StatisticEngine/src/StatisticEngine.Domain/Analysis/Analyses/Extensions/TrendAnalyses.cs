using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Análisis de tendencia y pronóstico (port de <c>series_explore._trend</c>).</summary>
internal static class TrendAnalyses
{
    private static readonly (string Key, string Label)[] Models =
    [
        ("linear", "Lineal: y = a + b·t"),
        ("quadratic", "Cuadrático: y = a + b·t + c·t²"),
        ("exponential", "Crecimiento exponencial: y = a·e^(b·t)")
    ];

    public static readonly AnalysisDefinition Trend = new(
        "trend",
        "Análisis de tendencia y pronóstico",
        "Series temporales",
        "Ajusta una tendencia lineal, cuadrática o exponencial y la proyecta con intervalos de predicción.",
        [],
        [],
        [
            new(
                "model",
                "Modelo",
                AnalysisParamKinds.Select,
                "linear",
                Models.Select(item => new AnalysisParamOption(item.Key, item.Label)).ToArray(),
                false),
            new("horizon", "Periodos a pronosticar", AnalysisParamKinds.Integer, 12.0, [], false, Minimum: 0, Maximum: 500),
            new(
                "period",
                "Periodo estacional (0 = según la fecha)",
                AnalysisParamKinds.Integer,
                0.0,
                [],
                false,
                Minimum: 0,
                Maximum: 400),
            ModelHelpers.Alpha()
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Componentes, tendencia y filtros",
        Roles:
        [
            new ColumnRole("series", "Serie", ColumnKinds.Numeric),
            new ColumnRole("time", "Columna de tiempo (opcional)", ColumnKinds.DateTime, Optional: true, Help: "Sin ella se usa el orden de las filas.")
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var seriesColumn = ExtendedHelpers.FirstRole(input, "series", "Serie");
        var timeColumn = ExtendedHelpers.FirstRole(input, "time", "Columna de tiempo");
        var loaded = SeriesLoader.Load(frame, seriesColumn, timeColumn, input.Pint("period", 0));
        if (loaded.IsError) return Result.Err<AnalysisResult>(loaded.Error.Code, loaded.Error.Message);
        var series = loaded.Value;
        var model = input.Pstr("model", "linear");
        var horizon = input.Pint("horizon", 12);
        var alpha = input.Pnum("alpha", 0.05);
        if (model == "exponential" && series.Values.Any(value => value <= 0))
        {
            return ExtendedHelpers.Invalid("El modelo exponencial necesita valores estrictamente positivos.");
        }
        var n = series.Values.Length;
        var t = Enumerable.Range(1, n).Select(value => (double)value).ToArray();
        var target = model == "exponential" ? series.Values.Select(value => Math.Log(value)).ToArray() : series.Values;
        var design = t.Select(value => model == "quadratic" ? new[] { 1.0, value, value * value } : new[] { 1.0, value }).ToArray();
        var fitResult = LinearModel.Fit(design, target);
        if (fitResult.IsError) return Result.Err<AnalysisResult>(fitResult.Error.Code, fitResult.Error.Message);
        var fit = fitResult.Value;
        var fitted = fit.Fitted.Select(value => model == "exponential" ? Math.Exp(value) : value).ToArray();
        var residuals = series.Values.Select((value, index) => value - fitted[index]).ToArray();
        var mae = residuals.Select(Math.Abs).Average();
        var rmse = Math.Sqrt(residuals.Sum(value => value * value) / n);
        var mape = Enumerable.Range(0, n).Where(index => Math.Abs(series.Values[index]) > 1e-12)
            .Select(index => Math.Abs(residuals[index] / series.Values[index])).Average() * 100;
        var dw = LinearModel.DurbinWatson(residuals);
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("R²", ModelHelpers.N(fit.R2)),
                new TileItem("MAPE %", ModelHelpers.N(mape)),
                new TileItem("Durbin-Watson", ModelHelpers.N(dw)),
                new TileItem("Observaciones", JsNumber.ToString(n))
            ])
        };
        if (series.Dropped > 0)
        {
            sections.Add(new AlertSection("Aviso", "info", $"Se omitieron {series.Dropped} valor(es) faltante(s) en los extremos de la serie."));
        }
        var coefficientNames = new List<string> { "a (constante)", "b (lineal)" };
        if (model == "quadratic") coefficientNames.Add("c (cuadrático)");
        var coefficientRows = new List<IReadOnlyList<string>>();
        for (var index = 0; index < fit.Coefficients.Length; index++)
        {
            coefficientRows.Add([
                coefficientNames[index],
                ModelHelpers.N(fit.Coefficients[index]),
                ModelHelpers.N(fit.StandardErrors[index]),
                ModelHelpers.N(fit.TValues[index]),
                ModelHelpers.P(fit.PValues[index])
            ]);
        }
        sections.Add(new TableSection(
            "Coeficientes" + (model == "exponential" ? " (en escala logarítmica)" : string.Empty),
            ["Parámetro", "Estimación", "Error est.", "t", "Valor p"],
            coefficientRows));
        sections.Add(new TableSection(
            "Exactitud del ajuste",
            ["Medida", "MAE (MAD)", "RMSE", "MAPE %"],
            [["En la muestra", ModelHelpers.N(mae), ModelHelpers.N(rmse), ModelHelpers.N(mape)]]));
        if (Math.Abs(dw - 2) > 0.7)
        {
            sections.Add(new AlertSection(
                "Aviso",
                "warning",
                "Durbin-Watson lejos de 2: los residuos están autocorrelacionados; los intervalos de predicción de una tendencia simple serán demasiado estrechos. Considere ARIMA o suavizamiento."));
        }

        var forecastRows = new List<IReadOnlyList<string>>();
        var forecastValues = Array.Empty<double>();
        var forecastLow = Array.Empty<double>();
        var forecastHigh = Array.Empty<double>();
        var forecastLabels = new List<string>();
        if (horizon > 0)
        {
            forecastValues = new double[horizon];
            forecastLow = new double[horizon];
            forecastHigh = new double[horizon];
            var critical = InferenceDistributions.TQuantile(1 - alpha / 2, n - fit.Parameters);
            for (var step = 0; step < horizon; step++)
            {
                var value = n + step + 1.0;
                var row = model == "quadratic" ? new[] { 1.0, value, value * value } : new[] { 1.0, value };
                var prediction = row.Select((item, index) => item * fit.Coefficients[index]).Sum();
                var variance = fit.Sigma * fit.Sigma;
                for (var i = 0; i < row.Length; i++)
                {
                    for (var j = 0; j < row.Length; j++) variance += row[i] * fit.Covariance[i][j] * row[j];
                }
                var half = critical * Math.Sqrt(Math.Max(0, variance));
                if (model == "exponential")
                {
                    forecastValues[step] = Math.Exp(prediction);
                    forecastLow[step] = Math.Exp(prediction - half);
                    forecastHigh[step] = Math.Exp(prediction + half);
                }
                else
                {
                    forecastValues[step] = prediction;
                    forecastLow[step] = prediction - half;
                    forecastHigh[step] = prediction + half;
                }
            }
            forecastLabels = FutureLabels(series, horizon);
            for (var step = 0; step < horizon; step++)
            {
                forecastRows.Add([
                    forecastLabels[step],
                    ModelHelpers.N(forecastValues[step]),
                    ModelHelpers.N(forecastLow[step]),
                    ModelHelpers.N(forecastHigh[step])
                ]);
            }
            sections.Add(new TableSection(
                "Pronósticos",
                ["Periodo", "Pronóstico", $"PI {SigmaText.General(100 * (1 - alpha))} % inf.", "PI sup."],
                forecastRows));
        }

        var historyPoints = new List<ScatterPoint>(n);
        for (var index = 0; index < n; index++) historyPoints.Add(new ScatterPoint(index + 1, series.Values[index]));
        var seriesList = new List<XYSeries>
        {
            new("Serie", historyPoints.Select(point => point.X).ToArray(), series.Values),
            new("Tendencia", historyPoints.Select(point => point.X).ToArray(), fitted)
        };
        if (horizon > 0)
        {
            seriesList.Add(new XYSeries("Pronóstico", Enumerable.Range(n + 1, horizon).Select(value => (double)value).ToArray(), forecastValues));
        }

        return Result.Ok(new AnalysisResult(
            "trend",
            $"Análisis de tendencia de {seriesColumn}",
            sections,
            Subtitle: $"modelo {Models.First(item => item.Key == model).Label.Split(':')[0].ToLowerInvariant()} · n = {n}",
            Charts:
            [
                new SeriesChart("Serie, tendencia y pronóstico", seriesList, "Tiempo", seriesColumn)
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["r2"] = fit.R2,
                ["mape"] = mape,
                ["coefficients"] = fit.Coefficients
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static List<string> FutureLabels(LoadedSeries series, int horizon)
    {
        var labels = new List<string>();
        if (series.Dated && series.LastDate is { } last)
        {
            for (var step = 1; step <= horizon; step++)
            {
                var date = series.Frequency switch
                {
                    "yearly" => last.AddYears(step),
                    "quarterly" => last.AddMonths(3 * step),
                    "monthly" => last.AddMonths(step),
                    "weekly" => last.AddDays(7 * step),
                    "daily" => last.AddDays(step),
                    _ => last.AddHours(step)
                };
                labels.Add(date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        else
        {
            for (var step = 1; step <= horizon; step++) labels.Add(JsNumber.ToString(series.Values.Length + step));
        }
        return labels;
    }
}
