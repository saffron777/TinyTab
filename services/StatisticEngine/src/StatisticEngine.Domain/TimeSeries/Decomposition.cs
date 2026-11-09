using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.TimeSeries;

public sealed record DecompositionResult(double[] Trend, double[] Seasonal, double[] Remainder, int Period);

/// <summary>Descomposición aditiva y diferencias (paridad: <c>timeseries.ts:133-177</c>).</summary>
public static class Decomposition
{
    /// <summary>Diferencias de orden <paramref name="lag"/> (paridad: <c>timeseries.ts:133-135</c>).</summary>
    public static double[] Difference(IReadOnlyList<double> values, int lag = 1)
    {
        if (lag >= values.Count) return [];
        if (lag <= 0) return new double[values.Count];
        var output = new double[values.Count - lag];
        for (var i = lag; i < values.Count; i++) output[i - lag] = values[i] - values[i - lag];
        return output;
    }

    /// <summary>Descomposición aditiva con tendencia centrada (paridad: <c>timeseries.ts:144-177</c>).</summary>
    public static Result<DecompositionResult> Decompose(IReadOnlyList<double> values, int period)
    {
        if (period < 2 || values.Count < 2 * period)
        {
            return Result<DecompositionResult>.Err(DomainErrorCode.InsufficientData, "Serie demasiado corta para la estacionalidad indicada.");
        }

        var n = values.Count;
        // paridad: timeseries.ts:146 — la ventana de tendencia siempre es par.
        var centers = period % 2 == 0 ? period : period - 1;
        var trend = new double[n];
        for (var i = 0; i < n; i++)
        {
            var start = i - centers / 2;
            var end = start + centers;
            if (start >= 0 && end <= n)
            {
                var total = 0.0;
                for (var j = start; j < end; j++) total += values[j];
                trend[i] = total / centers;
            }
            else
            {
                trend[i] = double.NaN;
            }
        }

        var seasonal = new double[n];
        for (var i = 0; i < n; i++)
        {
            if (double.IsFinite(trend[i])) seasonal[i] = values[i] - trend[i];
        }

        var periodMeans = new double[period];
        for (var i = 0; i < period; i++)
        {
            var total = 0.0;
            var count = 0;
            for (var j = i; j < n; j += period)
            {
                if (!double.IsFinite(trend[j])) continue;
                total += values[j] - trend[j];
                count++;
            }
            periodMeans[i] = count > 0 ? total / count : 0;
        }

        var periodMean = NumericMath.Mean(periodMeans);
        for (var i = 0; i < n; i++) seasonal[i] = periodMeans[i % period] - periodMean;

        var center = NumericMath.Mean(values);
        var remainder = new double[n];
        for (var i = 0; i < n; i++)
        {
            var trendValue = trend[i];
            // paridad: timeseries.ts:175 — `trend[i] || mean(values)`: 0 y NaN usan la media.
            if (trendValue == 0 || double.IsNaN(trendValue)) trendValue = center;
            remainder[i] = values[i] - seasonal[i] - trendValue;
        }

        return Result<DecompositionResult>.Ok(new DecompositionResult(trend, seasonal, remainder, period));
    }
}
