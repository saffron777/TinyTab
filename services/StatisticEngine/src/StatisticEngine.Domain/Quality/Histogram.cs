using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Quality;

/// <summary>Histograma con regla de Sturges (paridad: <c>spc.ts:324-338</c>).</summary>
public sealed record HistogramResult(double Lower, double Upper, double[] Counts, double[] Edges);

public static class Histogram
{
    /// <summary>Histograma del motor; <paramref name="bins"/> nulo aplica la regla de Sturges.</summary>
    public static HistogramResult Build(IReadOnlyList<double> values, int? bins = null)
    {
        var ordered = NumericMath.Sorted(values);
        var min = ordered.Length > 0 ? ordered[0] : 0;
        var max = ordered.Length > 0 ? ordered[^1] : 0;
        var count = bins ?? (int)Math.Max(5, Math.Ceiling(Math.Log2(Math.Max(2, ordered.Length)) + 1));

        // paridad: spc.ts:330 — `(max - min) / count || 1` (0 y NaN cuentan como falsy).
        var width = (max - min) / count;
        if (width == 0 || double.IsNaN(width)) width = 1;

        var edges = new double[count + 1];
        for (var i = 0; i <= count; i++) edges[i] = min + i * width;
        var counts = new double[count];
        for (var i = 0; i < ordered.Length; i++)
        {
            var index = Math.Min(count - 1, (int)Math.Floor((ordered[i] - min) / width));
            counts[index] += 1;
        }
        return new HistogramResult(min, max, counts, edges);
    }
}
