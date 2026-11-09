using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Inference;

/// <summary>Comparación por pares de medias de grupos.</summary>
public sealed record AnovaPairwiseComparison(
    string First,
    string Second,
    double Difference,
    double StandardError,
    double T,
    double PValue,
    bool Significant);

public sealed record PairwiseComparisonsResult(AnovaPairwiseComparison[] Comparisons, string Method);

/// <summary>Intervalo de confianza para la media de un grupo.</summary>
public sealed record AnovaConfidenceInterval(string Label, double Lower, double Upper);

/// <summary>
/// Comparaciones múltiples y intervalos por grupo (port 1:1 de <c>anova.ts</c>).
/// </summary>
public static class MultipleComparisons
{
    private static readonly Comparer<double> PValueOrder = Comparer<double>.Create(static (a, b) =>
    {
        // Mismo comparador que `(a, b) => a.pValue - b.pValue` de JS: NaN no reordena.
        if (double.IsNaN(a) || double.IsNaN(b)) return 0;
        if (a < b) return -1;
        return a > b ? 1 : 0;
    });

    /// <summary>Comparaciones por pares con ajuste de Holm (por defecto) o Fisher (LSD).</summary>
    public static Result<PairwiseComparisonsResult> PairwiseComparisons(
        double[][] groups,
        string[] labels,
        double alpha = 0.05,
        string method = "holm")
    {
        if (groups.Length < 2)
        {
            return Result<PairwiseComparisonsResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos dos grupos.");
        }

        var anova = Anova.OneWayAnova(groups, labels);
        if (!anova.IsOk)
        {
            return Result<PairwiseComparisonsResult>.Err(anova.Error.Code, anova.Error.Message);
        }

        var mse = anova.Value.Table[1].Ms;
        var dfError = anova.Value.Table[1].Df;
        var raw = new List<AnovaPairwiseComparison>();
        for (var i = 0; i < groups.Length - 1; i++)
        {
            for (var j = i + 1; j < groups.Length; j++)
            {
                var first = groups[i];
                var second = groups[j];
                var difference = NumericMath.Mean(first) - NumericMath.Mean(second);
                var standardError = Math.Sqrt(mse * (1.0 / first.Length + 1.0 / second.Length));
                var t = difference / standardError;
                // paridad: anova.ts:204 — p bilateral con dos llamadas a tCdf, como el motor.
                var pValue = 2 * Math.Min(
                    InferenceDistributions.TCdf(t, dfError),
                    1 - InferenceDistributions.TCdf(t, dfError));
                raw.Add(new AnovaPairwiseComparison(
                    i < labels.Length ? labels[i] : $"G{i + 1}",
                    j < labels.Length ? labels[j] : $"G{j + 1}",
                    difference,
                    standardError,
                    t,
                    pValue,
                    false));
            }
        }

        var isHolm = string.Equals(method, "holm", StringComparison.Ordinal);
        var ordered = Enumerable.Range(0, raw.Count)
            .OrderBy(index => raw[index].PValue, PValueOrder)
            .ToArray();
        var count = ordered.Length;
        var adjusted = new double[raw.Count];
        var previous = 0.0;
        for (var position = 0; position < ordered.Length; position++)
        {
            var index = ordered[position];
            var factor = isHolm ? count - position : count;
            var value = Math.Min(1, Math.Max(previous, raw[index].PValue * factor));
            previous = value;
            adjusted[index] = value;
        }

        var comparisons = new AnovaPairwiseComparison[raw.Count];
        for (var index = 0; index < raw.Count; index++)
        {
            var pValue = adjusted[index];
            comparisons[index] = raw[index] with { PValue = pValue, Significant = pValue < alpha };
        }

        return Result<PairwiseComparisonsResult>.Ok(new PairwiseComparisonsResult(
            comparisons,
            isHolm ? "Holm" : "Fisher (LSD)"));
    }

    /// <summary>Intervalos de confianza para la media de cada grupo (t de Student).</summary>
    public static AnovaConfidenceInterval[] GroupConfidenceIntervals(
        double[][] groups,
        string[] labels,
        double confidence = 0.95)
    {
        var output = new AnovaConfidenceInterval[groups.Length];
        for (var index = 0; index < groups.Length; index++)
        {
            var values = groups[index];
            var n = values.Length;
            var center = NumericMath.Mean(values);
            var standardError = NumericMath.Sd(values) / Math.Sqrt(n);
            var critical = InferenceDistributions.TQuantile(1 - (1 - confidence) / 2, Math.Max(1, n - 1));
            output[index] = new AnovaConfidenceInterval(
                index < labels.Length ? labels[index] : $"G{index + 1}",
                center - critical * standardError,
                center + critical * standardError);
        }
        return output;
    }
}
