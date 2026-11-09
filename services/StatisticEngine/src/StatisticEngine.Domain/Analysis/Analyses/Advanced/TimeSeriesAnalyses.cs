using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.TimeSeries;

namespace StatisticEngine.Domain.Analysis.Analyses;

/// <summary>
/// Análisis de series temporales del catálogo (port 1:1 de <c>registry-advanced.ts:266-402</c>).
/// </summary>
public static class TimeSeriesAnalyses
{
    public static IReadOnlyList<AnalysisDefinition> All { get; } =
    [
        new AnalysisDefinition(
            "timeseries-acf",
            "Función de autocorrelación (ACF)",
            "Series temporales",
            "Autocorrelaciones y banda de confianza.",
            [new ColumnRequirement("x", "Serie", true, false)],
            [],
            [new AnalysisParamDefinition("lags", "Número de rezagos", AnalysisParamKinds.Integer, 20.0, [], false)],
            RunAcf),
        new AnalysisDefinition(
            "timeseries-pacf",
            "Autocorrelación parcial (PACF)",
            "Series temporales",
            "PACF por Durbin-Levinson.",
            [new ColumnRequirement("x", "Serie", true, false)],
            [],
            [new AnalysisParamDefinition("lags", "Número de rezagos", AnalysisParamKinds.Integer, 20.0, [], false)],
            RunPacf),
        new AnalysisDefinition(
            "timeseries-moving-average",
            "Media móvil",
            "Series temporales",
            "Suavizado por media móvil centrada.",
            [new ColumnRequirement("x", "Serie", true, false)],
            [],
            [new AnalysisParamDefinition("window", "Ventana", AnalysisParamKinds.Integer, 3.0, [], false)],
            RunMovingAverage),
        new AnalysisDefinition(
            "timeseries-ses",
            "Suavizado exponencial simple",
            "Series temporales",
            "Suavizado exponencial simple con pronóstico a un paso.",
            [new ColumnRequirement("x", "Serie", true, false)],
            [],
            [new AnalysisParamDefinition("alpha", "α", AnalysisParamKinds.Number, 0.2, [], false)],
            RunSimpleExponential),
        new AnalysisDefinition(
            "timeseries-des",
            "Suavizado exponencial doble",
            "Series temporales",
            "Suavizado de Holt (nivel y tendencia).",
            [new ColumnRequirement("x", "Serie", true, false)],
            [],
            [
                new AnalysisParamDefinition("alpha", "α", AnalysisParamKinds.Number, 0.2, [], false),
                new AnalysisParamDefinition("beta", "β", AnalysisParamKinds.Number, 0.1, [], false)
            ],
            RunDoubleExponential),
        new AnalysisDefinition(
            "timeseries-winters",
            "Suavizado de Winters",
            "Series temporales",
            "Suavizado estacional (nivel, tendencia y estacionalidad).",
            [new ColumnRequirement("x", "Serie", true, false)],
            [],
            [
                new AnalysisParamDefinition("period", "Periodo estacional", AnalysisParamKinds.Integer, 12.0, [], false),
                new AnalysisParamDefinition("alpha", "α", AnalysisParamKinds.Number, 0.2, [], false),
                new AnalysisParamDefinition("beta", "β", AnalysisParamKinds.Number, 0.1, [], false),
                new AnalysisParamDefinition("gamma", "γ", AnalysisParamKinds.Number, 0.1, [], false)
            ],
            RunWinters),
        new AnalysisDefinition(
            "timeseries-decompose",
            "Descomposición estacional",
            "Series temporales",
            "Tendencia por media móvil centrada y patrón estacional.",
            [new ColumnRequirement("x", "Serie", true, false)],
            [],
            [new AnalysisParamDefinition("period", "Periodo", AnalysisParamKinds.Integer, 12.0, [], false)],
            RunDecompose),
        new AnalysisDefinition(
            "timeseries-ar",
            "Modelo AR y pronóstico",
            "Series temporales",
            "Ajuste AR(p) por Yule-Walker con selección AIC y Ljung-Box.",
            [new ColumnRequirement("x", "Serie", true, false)],
            [],
            [
                new AnalysisParamDefinition("order", "Orden máximo", AnalysisParamKinds.Integer, 3.0, [], false),
                new AnalysisParamDefinition("horizon", "Horizonte de pronóstico", AnalysisParamKinds.Integer, 5.0, [], false)
            ],
            RunArModel)
    ];

    private static Result<AnalysisResult> RunAcf(AnalysisInput input)
    {
        var result = Autocorrelations.Autocorrelation(input.Num("x"), (int)Math.Floor(input.Pnum("lags", 20)));
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;
        var rows = new List<IReadOnlyList<object?>>(value.Lags.Length);
        for (var index = 0; index < value.Lags.Length; index++)
        {
            rows.Add([JsNumber.ToString(value.Lags[index]), FormatHelpers.N(value.Values[index], 4)]);
        }
        var sections = FormatHelpers.Sections(
            FormatHelpers.Table("ACF", ["Rezago", "Autocorrelación"], rows));
        return Result.Ok(new AnalysisResult(
            "timeseries-acf",
            "Función de autocorrelación",
            sections,
            Charts: [new LineChart("ACF", [new LineSeries("ACF", value.Values)], "Rezago")]));
    }

    private static Result<AnalysisResult> RunPacf(AnalysisInput input)
    {
        var result = Autocorrelations.PartialAutocorrelation(input.Num("x"), (int)Math.Floor(input.Pnum("lags", 20)));
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;
        var rows = new List<IReadOnlyList<object?>>(value.Lags.Length);
        for (var index = 0; index < value.Lags.Length; index++)
        {
            rows.Add([JsNumber.ToString(value.Lags[index]), FormatHelpers.N(value.Values[index], 4)]);
        }
        var sections = FormatHelpers.Sections(
            FormatHelpers.Table("PACF", ["Rezago", "PACF"], rows));
        return Result.Ok(new AnalysisResult(
            "timeseries-pacf",
            "Autocorrelación parcial",
            sections,
            Charts: [new LineChart("PACF", [new LineSeries("PACF", value.Values)], "Rezago")]));
    }

    private static Result<AnalysisResult> RunMovingAverage(AnalysisInput input)
    {
        var values = input.Num("x");
        var result = Smoothing.MovingAverage(values, Math.Max(2, (int)Math.Floor(input.Pnum("window", 3))));
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;
        var rows = new List<IReadOnlyList<object?>>(values.Length);
        for (var index = 0; index < values.Length; index++)
        {
            rows.Add([JsNumber.ToString(index + 1), FormatHelpers.N(values[index]), FormatHelpers.N(value.Fitted[index])]);
        }
        // paridad: registry-advanced.ts:309 — el pronóstico descarta los valores no finitos.
        var fitted = new List<double>(value.Fitted.Length);
        foreach (var item in value.Fitted)
        {
            if (double.IsFinite(item)) fitted.Add(item);
        }
        var sections = FormatHelpers.Sections(
            FormatHelpers.Table("Ajuste", ["t", "Valor", "Media móvil"], rows));
        return Result.Ok(new AnalysisResult(
            "timeseries-moving-average",
            "Media móvil",
            sections,
            Charts: [new TimeSeriesChart("Serie y media móvil", values, [.. fitted])]));
    }

    private static Result<AnalysisResult> RunSimpleExponential(AnalysisInput input)
    {
        var values = input.Num("x");
        var alpha = input.Pnum("alpha", 0.2);
        var result = Smoothing.SimpleExponentialSmoothing(values, alpha);
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;
        var sections = FormatHelpers.Sections(
            FormatHelpers.Table(
                "Resumen",
                ["α", "Pronóstico siguiente"],
                [[FormatHelpers.N(alpha, 2), FormatHelpers.N(value.Forecast)]]));
        return Result.Ok(new AnalysisResult(
            "timeseries-ses",
            "Suavizado exponencial simple",
            sections,
            Charts: [new TimeSeriesChart("Serie y suavizado", values, value.Fitted)]));
    }

    private static Result<AnalysisResult> RunDoubleExponential(AnalysisInput input)
    {
        var values = input.Num("x");
        var result = Smoothing.DoubleExponentialSmoothing(values, input.Pnum("alpha", 0.2), input.Pnum("beta", 0.1));
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;
        var sections = FormatHelpers.Sections(
            FormatHelpers.Table("Pronóstico", ["Siguiente valor"], [[FormatHelpers.N(value.Forecast)]]));
        return Result.Ok(new AnalysisResult(
            "timeseries-des",
            "Suavizado exponencial doble",
            sections,
            Charts: [new TimeSeriesChart("Serie y suavizado", values, value.Fitted)]));
    }

    private static Result<AnalysisResult> RunWinters(AnalysisInput input)
    {
        var values = input.Num("x");
        var result = Smoothing.WintersSmoothing(
            values,
            Math.Max(2, (int)Math.Floor(input.Pnum("period", 12))),
            input.Pnum("alpha", 0.2),
            input.Pnum("beta", 0.1),
            input.Pnum("gamma", 0.1));
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;

        var headers = new List<string>(value.Forecast.Length);
        for (var index = 0; index < value.Forecast.Length; index++) headers.Add($"t+{JsNumber.ToString(index + 1)}");
        var forecastRow = new List<object?>(value.Forecast.Length);
        foreach (var item in value.Forecast) forecastRow.Add(FormatHelpers.N(item));
        var sections = FormatHelpers.Sections(
            FormatHelpers.Table("Pronóstico", headers, [forecastRow]));

        // paridad: registry-advanced.ts:359 — el gráfico concatena suavizado y pronóstico.
        var combined = new double[value.Fitted.Length + value.Forecast.Length];
        for (var index = 0; index < value.Fitted.Length; index++) combined[index] = value.Fitted[index];
        for (var index = 0; index < value.Forecast.Length; index++) combined[value.Fitted.Length + index] = value.Forecast[index];
        return Result.Ok(new AnalysisResult(
            "timeseries-winters",
            "Suavizado de Winters",
            sections,
            Charts: [new TimeSeriesChart("Serie y suavizado", values, combined)]));
    }

    private static Result<AnalysisResult> RunDecompose(AnalysisInput input)
    {
        var values = input.Num("x");
        var result = Decomposition.Decompose(values, Math.Max(2, (int)Math.Floor(input.Pnum("period", 12))));
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;
        var rows = new List<IReadOnlyList<object?>>(value.Period);
        for (var index = 0; index < value.Period; index++)
        {
            rows.Add([JsNumber.ToString(index + 1), FormatHelpers.N(value.Seasonal[index])]);
        }
        var sections = FormatHelpers.Sections(
            FormatHelpers.Table("Patrón estacional", ["Posición", "Efecto"], rows));
        return Result.Ok(new AnalysisResult(
            "timeseries-decompose",
            "Descomposición estacional",
            sections,
            Charts: [new TimeSeriesChart("Serie original", values)]));
    }

    private static Result<AnalysisResult> RunArModel(AnalysisInput input)
    {
        var values = input.Num("x");
        var result = ArModel.FitAr(
            values,
            (int)Math.Floor(input.Pnum("order", 3)),
            (int)Math.Floor(input.Pnum("horizon", 5)));
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;

        var coefficientHeaders = new List<string>(value.Coefficients.Length);
        var coefficientRow = new List<object?>(value.Coefficients.Length);
        for (var index = 0; index < value.Coefficients.Length; index++)
        {
            coefficientHeaders.Add($"φ{JsNumber.ToString(index + 1)}");
            coefficientRow.Add(FormatHelpers.N(value.Coefficients[index], 4));
        }

        var forecastHeaders = new List<string>(value.Forecast.Length);
        var forecastRow = new List<object?>(value.Forecast.Length);
        for (var index = 0; index < value.Forecast.Length; index++)
        {
            forecastHeaders.Add($"t+{JsNumber.ToString(index + 1)}");
            forecastRow.Add(FormatHelpers.N(value.Forecast[index]));
        }

        var sections = FormatHelpers.Sections(
            FormatHelpers.Table(
                "Modelo",
                ["Orden", "Intercepto", "AIC", "Varianza residual"],
                [[
                    JsNumber.ToString(value.Order),
                    FormatHelpers.N(value.Intercept),
                    FormatHelpers.N(value.Aic, 2),
                    FormatHelpers.N(value.ResidualVariance)
                ]]),
            FormatHelpers.Table("Coeficientes AR", coefficientHeaders, [coefficientRow]),
            FormatHelpers.Table(
                "Ljung-Box",
                ["Estadístico", "GL", "Valor p"],
                [[
                    FormatHelpers.N(value.LjungBox.Statistic, 3),
                    JsNumber.ToString(value.LjungBox.Df),
                    FormatHelpers.P(value.LjungBox.PValue)
                ]]),
            FormatHelpers.Table("Pronóstico", forecastHeaders, [forecastRow]));
        return Result.Ok(new AnalysisResult(
            "timeseries-ar",
            "Modelo AR y pronóstico",
            sections,
            Charts: [new TimeSeriesChart("Serie y pronóstico", values, value.Forecast)]));
    }
}
