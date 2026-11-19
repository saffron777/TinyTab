using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Comparación de pronósticos con origen móvil (port de <c>series_models._backtest</c>).</summary>
internal static class BacktestAnalyses
{
    private static readonly (string Key, string Label)[] Methods =
    [
        ("naive", "Ingenuo (último valor)"),
        ("snaive", "Ingenuo estacional"),
        ("drift", "Con deriva (extrapola la pendiente)"),
        ("mean", "Media histórica"),
        ("ses", "Suavizamiento exponencial simple"),
        ("holt", "Holt (tendencia)"),
        ("ets", "ETS automático"),
        ("arima", "ARIMA automático (más lento)")
    ];

    public static readonly AnalysisDefinition Backtest = new(
        "backtest",
        "Comparar métodos de pronóstico (origen móvil)",
        "Modelos y pronósticos",
        "Evalúa ingenuo, deriva, suavizamientos, ETS y ARIMA con validación de origen móvil; MAE, RMSE, MAPE y MASE por horizonte.",
        [],
        [],
        [
            new(
                "methods",
                "Métodos",
                AnalysisParamKinds.Multiselect,
                new[] { "naive", "snaive", "drift", "ses", "holt", "ets" },
                Methods.Select(item => new AnalysisParamOption(item.Key, item.Label)).ToArray(),
                false),
            new("horizon", "Horizonte (periodos hacia adelante)", AnalysisParamKinds.Integer, 6.0, [], false, Minimum: 1, Maximum: 100),
            new("origins", "Orígenes de pronóstico", AnalysisParamKinds.Integer, 6.0, [], false, Minimum: 1, Maximum: 24),
            new("step", "Separación entre orígenes", AnalysisParamKinds.Integer, 3.0, [], false, Minimum: 1, Maximum: 100),
            new("period", "Periodo estacional (0 = según la fecha)", AnalysisParamKinds.Integer, 0.0, [], false, Minimum: 0, Maximum: 400)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Modelos y pronósticos",
        Heavy: true,
        Roles:
        [
            new ColumnRole("series", "Serie", ColumnKinds.Numeric),
            new ColumnRole("time", "Columna de tiempo (opcional)", ColumnKinds.DateTime, Optional: true, Help: "Sin ella se usa el orden de las filas.")
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var chosen = input.Plist("methods");
        if (chosen.Count == 0) chosen = ["naive", "snaive", "drift", "ses", "holt", "ets"];
        if (chosen.Any(method => method is not ("naive" or "snaive")))
        {
            return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "Por ahora la comparación solo admite los métodos ingenuo y ingenuo estacional.");
        }
        var frame = input.AsFrame();
        var seriesColumn = ExtendedHelpers.FirstRole(input, "series", "Serie");
        var timeColumn = ExtendedHelpers.FirstRole(input, "time", "Columna de tiempo");
        var loaded = SeriesLoader.Load(frame, seriesColumn, timeColumn, input.Pint("period", 0), minimum: 24);
        if (loaded.IsError) return Result.Err<AnalysisResult>(loaded.Error.Code, loaded.Error.Message);
        var series = loaded.Value;
        var y = series.Values;
        var n = y.Length;
        var horizon = input.Pint("horizon", 6);
        var origins = input.Pint("origins", 6);
        var step = Math.Max(1, input.Pint("step", 3));
        var lastOrigin = n - horizon;
        var firstOrigin = lastOrigin - (origins - 1) * step;
        if (firstOrigin < Math.Max(15, series.Period > 1 ? 2 * series.Period : 15))
        {
            return ExtendedHelpers.Invalid("No hay datos suficientes para tantos orígenes y ese horizonte: reduzca los orígenes o el horizonte.");
        }
        var cuts = Enumerable.Range(0, origins).Select(index => firstOrigin + index * step).ToArray();
        var errors = new Dictionary<string, List<double[]>>(StringComparer.Ordinal);
        var lastForecasts = new Dictionary<string, double[]>(StringComparer.Ordinal);
        foreach (var method in chosen)
        {
            errors[method] = [];
            foreach (var cutPoint in cuts)
            {
                var forecast = Forecast(method, y, cutPoint, horizon, series.Period);
                errors[method].Add(Enumerable.Range(0, horizon).Select(offset => y[cutPoint + offset] - forecast[offset]).ToArray());
                if (cutPoint == cuts[^1]) lastForecasts[method] = forecast;
            }
        }
        var train0 = y[..cuts[0]];
        var scale = MeanAbsoluteNthDifference(train0, series.Period > 1 ? series.Period : 1);
        var rows = new List<(string Key, string Label, double Mae, double Rmse, double Mape, double Mase)>();
        var byHorizon = new Dictionary<string, double[]>(StringComparer.Ordinal);
        foreach (var method in chosen)
        {
            var errorsMatrix = errors[method];
            var flat = errorsMatrix.SelectMany(value => value).ToArray();
            var actuals = cuts.SelectMany(cut => Enumerable.Range(0, horizon).Select(offset => y[cut + offset])).ToArray();
            var nonzero = actuals.Select((value, index) => (value, index)).Where(pair => Math.Abs(pair.value) > 1e-12).ToArray();
            var mae = flat.Select(Math.Abs).Average();
            var rmse = Math.Sqrt(flat.Sum(value => value * value) / flat.Length);
            var mape = nonzero.Length > 0
                ? 100 * nonzero.Average(pair => Math.Abs(flat[pair.index] / pair.value))
                : double.NaN;
            var mase = scale > 0 ? mae / scale : double.NaN;
            rows.Add((method, Methods.First(item => item.Key == method).Label, mae, rmse, mape, mase));
            byHorizon[method] = Enumerable.Range(0, horizon)
                .Select(offset => Math.Sqrt(errorsMatrix.Average(values => values[offset] * values[offset])))
                .ToArray();
        }
        rows.Sort((left, right) => left.Rmse.CompareTo(right.Rmse));

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Mejor método", rows[0].Label),
                new TileItem("RMSE del mejor", ModelHelpers.N(rows[0].Rmse)),
                new TileItem("Orígenes", JsNumber.ToString(origins)),
                new TileItem("Horizonte", JsNumber.ToString(horizon))
            ]),
            new TableSection(
                "Error de pronóstico por método (menor RMSE primero)",
                ["Método", "MAE", "RMSE", "MAPE %", "MASE"],
                rows.Select(row => (IReadOnlyList<string>)new[]
                {
                    row.Label,
                    ModelHelpers.N(row.Mae),
                    ModelHelpers.N(row.Rmse),
                    ModelHelpers.N(row.Mape),
                    ModelHelpers.N(row.Mase)
                }).ToArray(),
                Note: "Evaluación con origen móvil: se ajusta con los datos hasta cada origen y se pronostican los siguientes periodos. MASE < 1 supera al ingenuo (estacional) del entrenamiento.")
        };

        var rmseSeries = chosen.Select(method => new LineSeries(
            Methods.First(item => item.Key == method).Label,
            byHorizon[method])).ToList();
        var cut = cuts[^1];
        var keep = Math.Max(0, cut - 3 * horizon);
        var points = Enumerable.Range(keep, cut + horizon - keep).Select(index => index + 1.0).ToArray();
        var actualSeries = new List<XYSeries>
        {
            new("Datos", points, y[keep..(cut + horizon)])
        };
        actualSeries.AddRange(chosen.Select(method => new XYSeries(
            Methods.First(item => item.Key == method).Label,
            Enumerable.Range(cut + 1, horizon).Select(value => (double)value).ToArray(),
            lastForecasts[method])));

        return Result.Ok(new AnalysisResult(
            "backtest",
            $"Comparación de pronósticos de {seriesColumn}",
            sections,
            Subtitle: $"{origins} orígenes de pronóstico · horizonte {horizon} · n = {n}",
            Charts:
            [
                new LineChart("RMSE según el horizonte de pronóstico", rmseSeries, "Periodos hacia adelante"),
                new SeriesChart("Pronósticos del último origen frente a lo ocurrido", actualSeries, "Tiempo", seriesColumn)
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["best"] = rows[0].Label,
                ["ranking"] = rows.Select(row => row.Label).ToArray(),
                ["rmse"] = rows.ToDictionary(row => row.Label, row => (object?)row.Rmse, StringComparer.Ordinal)
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double[] Forecast(string method, double[] y, int cut, int horizon, int period)
    {
        var forecast = new double[horizon];
        if (method == "naive")
        {
            for (var step = 0; step < horizon; step++) forecast[step] = y[cut - 1];
        }
        else
        {
            for (var step = 0; step < horizon; step++) forecast[step] = y[cut - period + step % period];
        }
        return forecast;
    }

    private static double MeanAbsoluteNthDifference(double[] values, int order)
    {
        var current = (double[])values.Clone();
        for (var step = 0; step < order; step++)
        {
            if (current.Length < 2) return double.NaN;
            var next = new double[current.Length - 1];
            for (var index = 0; index < next.Length; index++) next[index] = current[index + 1] - current[index];
            current = next;
        }
        return current.Select(Math.Abs).Average();
    }
}
