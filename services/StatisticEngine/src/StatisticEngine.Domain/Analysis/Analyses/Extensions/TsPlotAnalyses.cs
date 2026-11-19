using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Gráfico de la serie y estadísticos móviles (port de <c>series_explore._ts_plot</c>).</summary>
internal static class TsPlotAnalyses
{
    public static readonly AnalysisDefinition TsPlot = new(
        "ts-plot",
        "Gráfico de la serie y estadísticos móviles",
        "Series temporales",
        "La serie con media y desviación móviles, tendencia lineal, promedios por posición del ciclo y gráfico de rezago.",
        [],
        [],
        [
            new(
                "window",
                "Ventana móvil (vacío = automática)",
                AnalysisParamKinds.Integer,
                null,
                [],
                true,
                Minimum: 2,
                Maximum: 1000),
            new(
                "period",
                "Periodo estacional (0 = según la fecha)",
                AnalysisParamKinds.Integer,
                0.0,
                [],
                false,
                Minimum: 0,
                Maximum: 400,
                Help: "Observaciones por ciclo: 12 mensual, 4 trimestral, 7 diario, 24 por hora.")
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Exploración",
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
        var n = series.Values.Length;
        var window = input.Params.ContainsKey("window") ? input.Pint("window", 0) : Math.Max(3, n / 10);
        window = Math.Max(2, Math.Min(window, n / 2));
        var mean = new double[n];
        var deviation = new double[n];
        for (var index = 0; index < n; index++)
        {
            var half = window / 2;
            var start = Math.Max(0, index - half);
            var end = Math.Min(n - 1, index + half);
            var count = end - start + 1;
            if (count < window) { mean[index] = double.NaN; deviation[index] = double.NaN; continue; }
            var slice = series.Values[start..(end + 1)];
            mean[index] = slice.Average();
            deviation[index] = NumericMath.Sd(slice);
        }
        var t = Enumerable.Range(1, n).Select(value => (double)value).ToArray();
        var trendFit = LinearModel.Fit(t.Select(value => new[] { 1.0, value }).ToArray(), series.Values);
        if (trendFit.IsError) return Result.Err<AnalysisResult>(trendFit.Error.Code, trendFit.Error.Message);
        var trend = trendFit.Value;
        var lag1 = n > 1 ? Correlation(series.Values[..^1], series.Values[1..]) : double.NaN;

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("Media", ModelHelpers.N(series.Values.Average())),
                new TileItem("Desv. estándar", ModelHelpers.N(NumericMath.Sd(series.Values))),
                new TileItem("Tendencia por periodo", ModelHelpers.N(trend.Coefficients[1])),
                new TileItem("Autocorrelación lag 1", ModelHelpers.N(lag1))
            ])
        };
        if (series.Dropped > 0)
        {
            sections.Add(new AlertSection("Aviso", "info", $"Se omitieron {series.Dropped} valor(es) faltante(s) en los extremos de la serie."));
        }
        sections.Add(new TableSection(
            "Tendencia lineal",
            ["Pendiente por periodo", "Error est.", "Valor p", "R²"],
            [[
                ModelHelpers.N(trend.Coefficients[1]),
                ModelHelpers.N(trend.StandardErrors[1]),
                ModelHelpers.P(trend.PValues[1]),
                ModelHelpers.N(trend.R2)
            ]],
            Note: "Regresión de la serie sobre el número de observación."));

        var points = new List<ScatterPoint>(n);
        for (var index = 0; index < n; index++) points.Add(new ScatterPoint(index + 1, series.Values[index]));
        var tiled = new List<XYSeries>
        {
            new("Serie", points.Select(point => point.X).ToArray(), series.Values)
        };
        var meanPositions = Enumerable.Range(0, n).Where(index => !double.IsNaN(mean[index])).ToArray();
        tiled.Add(new XYSeries(
            $"Media móvil ({window})",
            meanPositions.Select(index => index + 1.0).ToArray(),
            meanPositions.Select(index => mean[index]).ToArray()));
        var seasonalPoints = new List<IntervalPoint>();
        if (series.Period > 1 && n >= 2 * series.Period)
        {
            for (var position = 0; position < series.Period; position++)
            {
                var values = Enumerable.Range(0, n).Where(index => index % series.Period == position).Select(index => series.Values[index]).ToArray();
                if (values.Length < 2) continue;
                var error = InferenceDistributions.TQuantile(0.975, values.Length - 1) * NumericMath.Sd(values) / Math.Sqrt(values.Length);
                seasonalPoints.Add(new IntervalPoint(JsNumber.ToString(position + 1), values.Average(), values.Average() - error, values.Average() + error));
            }
        }
        var lagPoints = Enumerable.Range(1, n - 1).Select(index => new ScatterPoint(series.Values[index - 1], series.Values[index])).ToList();

        return Result.Ok(new AnalysisResult(
            "ts-plot",
            $"Serie {seriesColumn}",
            sections,
            Subtitle: $"n = {n} · ventana móvil de {window}" + (series.Period > 1 ? $" · periodo {series.Period}" : string.Empty),
            Charts:
            [
                new SeriesChart("Serie con media y desviación móviles", tiled, "Tiempo", seriesColumn),
                new IntervalChart("Promedios por posición del ciclo", seasonalPoints, "Media"),
                new ScatterChart("Serie frente a su rezago", lagPoints, $"{seriesColumn}(t−1)", seriesColumn)
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["trend_slope"] = trend.Coefficients[1],
                ["trend_p"] = trend.PValues[1],
                ["lag1"] = lag1
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double Correlation(double[] x, double[] y)
    {
        var meanX = x.Average();
        var meanY = y.Average();
        var numerator = x.Select((value, index) => (value - meanX) * (y[index] - meanY)).Sum();
        var denominator = Math.Sqrt(x.Sum(value => Math.Pow(value - meanX, 2)) * y.Sum(value => Math.Pow(value - meanY, 2)));
        return denominator > 0 ? numerator / denominator : double.NaN;
    }
}
