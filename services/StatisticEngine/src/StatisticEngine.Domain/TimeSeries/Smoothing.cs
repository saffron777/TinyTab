using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.TimeSeries;

public sealed record MovingAverageResult(double[] Fitted, double[] Residuals);

public sealed record SimpleSmoothingResult(double[] Fitted, double Forecast);

public sealed record DoubleSmoothingResult(double[] Fitted, double Forecast);

public sealed record WintersResult(double[] Fitted, double[] Forecast, double[] Seasonal);

/// <summary>
/// Suavizados exponenciales y media móvil. Paridad 1:1 con <c>timeseries.ts:53-131</c>,
/// incluidos los defectos conocidos (Falta de validación de la longitud en SES y de
/// alpha/beta/gamma en Winters).
/// </summary>
public static class Smoothing
{
    /// <summary>Media móvil centrada al final (paridad: <c>timeseries.ts:53-66</c>).</summary>
    public static Result<MovingAverageResult> MovingAverage(IReadOnlyList<double> values, int window)
    {
        if (window < 2 || window > values.Count) return Result<MovingAverageResult>.Err(DomainErrorCode.InvalidParameter, "Ventana inválida.");

        var fitted = new double[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            if (i < window - 1)
            {
                fitted[i] = double.NaN;
                continue;
            }
            var total = 0.0;
            for (var j = i - window + 1; j <= i; j++) total += values[j];
            fitted[i] = total / window;
        }
        var residuals = new double[values.Count];
        for (var i = 0; i < values.Count; i++) residuals[i] = values[i] - fitted[i];
        return Result<MovingAverageResult>.Ok(new MovingAverageResult(fitted, residuals));
    }

    /// <summary>
    /// Suavizado exponencial simple (paridad: <c>timeseries.ts:68-78</c>). Defecto
    /// reproducido: no se valida la longitud de la serie.
    /// </summary>
    public static Result<SimpleSmoothingResult> SimpleExponentialSmoothing(IReadOnlyList<double> values, double alpha = 0.2)
    {
        if (!(alpha > 0 && alpha < 1)) return Result<SimpleSmoothingResult>.Err(DomainErrorCode.InvalidParameter, "α debe estar entre 0 y 1.");

        var fitted = new double[values.Count];
        var level = values.Count > 0 ? values[0] : double.NaN;
        if (fitted.Length > 0) fitted[0] = level;
        for (var i = 1; i < values.Count; i++)
        {
            level = alpha * values[i] + (1 - alpha) * level;
            fitted[i] = level;
        }
        return Result<SimpleSmoothingResult>.Ok(new SimpleSmoothingResult(fitted, level));
    }

    /// <summary>Suavizado doble de Holt (paridad: <c>timeseries.ts:80-97</c>).</summary>
    public static Result<DoubleSmoothingResult> DoubleExponentialSmoothing(
        IReadOnlyList<double> values,
        double alpha = 0.2,
        double beta = 0.1)
    {
        if (values.Count < 2) return Result<DoubleSmoothingResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos 2 observaciones.");
        if (!(alpha > 0 && alpha < 1) || !(beta > 0 && beta < 1))
        {
            return Result<DoubleSmoothingResult>.Err(DomainErrorCode.InvalidParameter, "α y β deben estar entre 0 y 1.");
        }

        var level = values[0];
        var trend = values[1] - values[0];
        var fitted = new double[values.Count];
        fitted[0] = level;
        for (var i = 1; i < values.Count; i++)
        {
            var previousLevel = level;
            level = alpha * values[i] + (1 - alpha) * (level + trend);
            trend = beta * (level - previousLevel) + (1 - beta) * trend;
            fitted[i] = level;
        }
        return Result<DoubleSmoothingResult>.Ok(new DoubleSmoothingResult(fitted, level + trend));
    }

    /// <summary>
    /// Suavizado de Holt-Winters aditivo (paridad: <c>timeseries.ts:99-131</c>). Defecto
    /// reproducido: no se validan alpha, beta ni gamma.
    /// </summary>
    public static Result<WintersResult> WintersSmoothing(
        IReadOnlyList<double> values,
        int period,
        double alpha = 0.2,
        double beta = 0.1,
        double gamma = 0.1)
    {
        if (values.Count < 2 * period) return Result<WintersResult>.Err(DomainErrorCode.InsufficientData, "La serie debe cubrir al menos 2 ciclos.");

        var overallMean = NumericMath.Mean(values);
        var seasonal = new double[period];
        for (var i = 0; i < period; i++)
        {
            var total = 0.0;
            var count = 0;
            for (var j = i; j < values.Count; j += period)
            {
                total += values[j];
                count++;
            }
            seasonal[i] = total / count - overallMean;
        }

        var level = overallMean;
        var trend = (values[period] - values[0]) / period;
        var fitted = new double[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            var seasonIndex = i % period;
            fitted[i] = level + trend + seasonal[seasonIndex];
            var previousLevel = level;
            level = alpha * (values[i] - seasonal[seasonIndex]) + (1 - alpha) * (level + trend);
            trend = beta * (level - previousLevel) + (1 - beta) * trend;
            seasonal[seasonIndex] = gamma * (values[i] - level) + (1 - gamma) * seasonal[seasonIndex];
        }

        var forecast = new double[period];
        for (var h = 0; h < period; h++) forecast[h] = level + (h + 1) * trend + seasonal[h];
        return Result<WintersResult>.Ok(new WintersResult(fitted, forecast, seasonal));
    }
}
