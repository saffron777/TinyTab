using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.TimeSeries;

/// <summary>ACF/PACF (paridad: <c>timeseries.ts:5-9</c>).</summary>
public sealed record AcfResult(int[] Lags, double[] Values, double Confidence);

/// <summary>
/// Autocorrelación y autocorrelación parcial. Paridad 1:1 con <c>timeseries.ts:11-51</c>,
/// incluidos los mensajes de error y el recorte por <c>n - 2</c>.
/// </summary>
public static class Autocorrelations
{
    /// <summary>ACF con corrección de media global (paridad: <c>timeseries.ts:11-27</c>).</summary>
    public static Result<AcfResult> Autocorrelation(IReadOnlyList<double> values, int maxLag = 20)
    {
        var n = values.Count;
        if (n < 4) return Result<AcfResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos 4 observaciones.");

        var center = NumericMath.Mean(values);
        var denominator = 0.0;
        for (var i = 0; i < n; i++)
        {
            var delta = values[i] - center;
            denominator += delta * delta;
        }
        if (denominator == 0) return Result<AcfResult>.Err(DomainErrorCode.InvalidInput, "La serie es constante.");

        var upperLag = Math.Min(maxLag, n - 2);
        var lags = new int[upperLag + 1];
        var acf = new double[upperLag + 1];
        for (var lag = 0; lag <= upperLag; lag++)
        {
            var numerator = 0.0;
            for (var i = lag; i < n; i++) numerator += (values[i] - center) * (values[i - lag] - center);
            lags[lag] = lag;
            acf[lag] = numerator / denominator;
        }
        return Result<AcfResult>.Ok(new AcfResult(lags, acf, 1.96 / Math.Sqrt(n)));
    }

    /// <summary>
    /// PACF por Durbin-Levinson (paridad: <c>timeseries.ts:29-51</c>). Defecto reproducido:
    /// <c>phi[lag - 1]</c> siempre está fuera de rango porque <c>phi</c> sólo tiene
    /// <c>lag - 1</c> filas; el motor TypeScript lee <c>undefined</c>, lo que convierte la
    /// PACF en NaN a partir del lag 2.
    /// </summary>
    public static Result<AcfResult> PartialAutocorrelation(IReadOnlyList<double> values, int maxLag = 20)
    {
        var acfResult = Autocorrelation(values, maxLag);
        if (acfResult.IsError) return Result<AcfResult>.Err(acfResult.Error.Code, acfResult.Error.Message);

        var rho = acfResult.Value.Values;
        var lags = acfResult.Value.Lags;
        var pacf = new List<double> { 1 };
        for (var lag = 1; lag < rho.Length; lag++)
        {
            // paridad: timeseries.ts:38 — phi[(lag - 1)] queda fuera del arreglo y JS lo
            // resuelve como undefined; Read lo convierte en NaN como haría la coerción numérica.
            double[] previous = [];
            var numerator = rho[lag];
            for (var j = 1; j < lag; j++) numerator -= Read(previous, j - 1) * rho[lag - j];
            var denominator = 1.0;
            for (var j = 1; j < lag; j++) denominator -= Read(previous, j - 1) * rho[j];
            var current = numerator / denominator;
            var row = new double[lag];
            for (var j = 0; j < lag - 1; j++) row[j] = Read(previous, j) - current * Read(previous, lag - 2 - j);
            row[lag - 1] = current;
            pacf.Add(current);
        }
        return Result<AcfResult>.Ok(new AcfResult(lags, [.. pacf], acfResult.Value.Confidence));
    }

    /// <summary>Lectura de un índice inexistente como <c>undefined</c> (NaN en aritmética).</summary>
    private static double Read(double[] values, int index) =>
        index >= 0 && index < values.Length ? values[index] : double.NaN;
}
