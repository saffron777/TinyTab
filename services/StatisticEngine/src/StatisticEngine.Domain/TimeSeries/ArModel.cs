using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.TimeSeries;

/// <summary>Ajuste AR(p) (paridad: <c>timeseries.ts:179-188</c>).</summary>
public sealed record ArFitResult(
    int Order,
    double Intercept,
    double[] Coefficients,
    double ResidualVariance,
    double Aic,
    double[] Forecast,
    double[] StandardErrors,
    LjungBoxResult LjungBox);

/// <summary>
/// Ajuste AR(p) por Yule-Walker con selección por AIC y pronóstico recursivo.
/// Paridad 1:1 con <c>timeseries.ts:190-246</c>, incluidos los dos defectos del motor:
/// <c>maxLag = min(maxOrder + 1, floor(n / 3))</c> en la ACF y
/// <c>standardErrors = sqrt(var * (1 + 0.1·h))</c> (heurístico).
/// </summary>
public static class ArModel
{
    public static Result<ArFitResult> FitAr(IReadOnlyList<double> values, int maxOrder = 5, int horizon = 5)
    {
        var n = values.Count;
        if (n < 12) return Result<ArFitResult>.Err(DomainErrorCode.InsufficientData, "Se recomiendan al menos 12 observaciones.");

        var center = NumericMath.Mean(values);
        var centered = new double[n];
        for (var i = 0; i < n; i++) centered[i] = values[i] - center;

        // paridad: timeseries.ts:196 — ACF truncada a min(maxOrder + 1, floor(n / 3)).
        var acf = Autocorrelations.Autocorrelation(values, Math.Min(maxOrder + 1, (int)Math.Floor(n / 3.0)));
        if (acf.IsError) return Result<ArFitResult>.Err(acf.Error.Code, acf.Error.Message);
        var rho = acf.Value.Values;

        ArCandidate? best = null;
        for (var order = 1; order <= Math.Min(maxOrder, rho.Length - 2); order++)
        {
            var matrix = new double[order][];
            var rhs = new double[order];
            for (var i = 0; i < order; i++)
            {
                matrix[i] = new double[order];
                for (var j = 0; j < order; j++) matrix[i][j] = rho[Math.Abs(i - j)];
                rhs[i] = rho[i + 1];
            }
            var solved = SolveSmall(matrix, rhs);
            if (solved is null) continue;

            var variance = rho[0];
            for (var i = 0; i < order; i++) variance -= solved[i] * rho[i + 1];
            var sumSquares = 0.0;
            for (var i = 0; i < centered.Length; i++) sumSquares += centered[i] * centered[i];
            variance *= n / (double)(n - order) * (sumSquares / n);
            var aic = n * Math.Log(Math.Max(variance, 1e-300)) + 2.0 * (order + 2);
            if (best is null || aic < best.Aic) best = new ArCandidate(order, solved, variance, aic);
        }
        if (best is null) return Result<ArFitResult>.Err(DomainErrorCode.NonConvergence, "No se pudo ajustar un modelo AR.");

        var history = new List<double>(centered);
        var forecast = new double[horizon];
        var standardErrors = new double[horizon];
        for (var h = 1; h <= horizon; h++)
        {
            var value = 0.0;
            for (var i = 0; i < best.Order; i++)
            {
                var index = history.Count - 1 - i;
                // paridad: timeseries.ts:224 — índice negativo => undefined => 0.
                value += best.Coefficients[i] * (index >= 0 ? history[index] : 0);
            }
            history.Add(value);
            forecast[h - 1] = value + center;
            // paridad: timeseries.ts:228 — error estándar heurístico del motor.
            standardErrors[h - 1] = Math.Sqrt(best.Variance * (1 + h * 0.1));
        }

        var residuals = new List<double>();
        for (var i = best.Order; i < centered.Length; i++)
        {
            var predicted = 0.0;
            for (var j = 0; j < best.Order; j++) predicted += best.Coefficients[j] * centered[i - j - 1];
            residuals.Add(centered[i] - predicted);
        }

        return Result<ArFitResult>.Ok(new ArFitResult(
            best.Order,
            center,
            best.Coefficients,
            best.Variance,
            best.Aic,
            forecast,
            standardErrors,
            LjungBox.Compute(residuals, Math.Min(10, residuals.Count - 1))));
    }

    /// <summary>Resolución del sistema de Yule-Walker con pivoteo parcial (timeseries.ts:316-347).</summary>
    private static double[]? SolveSmall(double[][] matrix, double[] vector)
    {
        var n = matrix.Length;
        var a = new double[n][];
        for (var i = 0; i < n; i++) a[i] = (double[])matrix[i].Clone();
        var b = (double[])vector.Clone();

        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var row = col + 1; row < n; row++)
            {
                if (Math.Abs(a[row][col]) > Math.Abs(a[pivot][col])) pivot = row;
            }
            if (Math.Abs(a[pivot][col]) < 1e-12) return null;
            if (pivot != col)
            {
                (a[pivot], a[col]) = (a[col], a[pivot]);
                (b[pivot], b[col]) = (b[col], b[pivot]);
            }
            for (var row = col + 1; row < n; row++)
            {
                var factor = a[row][col] / a[col][col];
                for (var j = col; j < n; j++) a[row][j] -= factor * a[col][j];
                b[row] -= factor * b[col];
            }
        }

        var solution = new double[n];
        for (var row = n - 1; row >= 0; row--)
        {
            var acc = b[row];
            for (var j = row + 1; j < n; j++) acc -= a[row][j] * solution[j];
            solution[row] = acc / a[row][row];
        }
        return solution;
    }

    private sealed record ArCandidate(int Order, double[] Coefficients, double Variance, double Aic);
}
