using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Inference;

/// <summary>Resultado de una prueba de igualdad de varianzas (Levene o Bartlett).</summary>
public sealed record VarianceEqualityResult(string Test, double Statistic, double Df, double PValue);

/// <summary>
/// Pruebas de homogeneidad de varianzas (port 1:1 de <c>anova.ts</c>): Levene sobre
/// desviaciones absolutas respecto a la mediana y Bartlett sobre log-varianzas.
/// </summary>
public static class VarianceEquality
{
    /// <summary>Prueba de Levene: ANOVA de un factor sobre las desviaciones absolutas a la mediana.</summary>
    public static Result<VarianceEqualityResult> LeveneTest(double[][] groups)
    {
        if (groups.Length < 2)
        {
            return Result<VarianceEqualityResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos dos grupos.");
        }

        var medians = new double[groups.Length];
        for (var i = 0; i < groups.Length; i++) medians[i] = Median(groups[i]);

        var transformed = new double[groups.Length][];
        var labels = new string[groups.Length];
        for (var i = 0; i < groups.Length; i++)
        {
            var values = groups[i];
            var deviations = new double[values.Length];
            for (var j = 0; j < values.Length; j++) deviations[j] = Math.Abs(values[j] - medians[i]);
            transformed[i] = deviations;
            labels[i] = $"G{i + 1}";
        }

        var anova = Anova.OneWayAnova(transformed, labels);
        if (!anova.IsOk)
        {
            return Result<VarianceEqualityResult>.Err(anova.Error.Code, anova.Error.Message);
        }

        var row = anova.Value.Table[0];
        return Result<VarianceEqualityResult>.Ok(new VarianceEqualityResult("Levene", row.F, row.Df, row.P));
    }

    /// <summary>Prueba de Bartlett para varianzas iguales entre grupos.</summary>
    public static Result<VarianceEqualityResult> BartlettTest(double[][] groups)
    {
        if (groups.Length < 2)
        {
            return Result<VarianceEqualityResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos dos grupos.");
        }

        var k = groups.Length;
        var variances = new double[k];
        var sizes = new double[k];
        for (var i = 0; i < k; i++)
        {
            variances[i] = Math.Pow(NumericMath.Sd(groups[i]), 2);
            sizes[i] = groups[i].Length;
        }

        foreach (var variance in variances)
        {
            if (!(variance > 0))
            {
                return Result<VarianceEqualityResult>.Err(DomainErrorCode.InvalidInput, "Bartlett requiere varianzas positivas en todos los grupos.");
            }
        }

        var total = 0.0;
        for (var i = 0; i < k; i++) total += sizes[i];

        var weightedVariances = 0.0;
        var weightedLogs = 0.0;
        var reciprocalSizes = 0.0;
        for (var i = 0; i < k; i++)
        {
            weightedVariances += (sizes[i] - 1) * variances[i];
            weightedLogs += (sizes[i] - 1) * Math.Log(variances[i]);
            reciprocalSizes += 1 / (sizes[i] - 1);
        }

        var pooled = weightedVariances / (total - k);
        var numerator = (total - k) * Math.Log(pooled) - weightedLogs;
        // Ojo: 1 / (3 * (k - 1)) con enteros sería división entera (0) y anularía el ajuste.
        var denominator = 1 + 1.0 / (3.0 * (k - 1)) * (reciprocalSizes - 1 / (total - k));
        var statistic = numerator / denominator;
        return Result<VarianceEqualityResult>.Ok(new VarianceEqualityResult(
            "Bartlett",
            statistic,
            k - 1,
            SpecialFunctions.Gammq((k - 1) / 2.0, statistic / 2)));
    }

    /// <summary>
    /// Mediana con el criterio literal de <c>anova.ts</c>: para tamaños pares promedia las dos
    /// observaciones centrales y para impares toma la central.
    /// </summary>
    private static double Median(double[] values)
    {
        var ordered = NumericMath.Sorted(values);
        if (ordered.Length == 0) return double.NaN;
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2
            : ordered[middle];
    }
}
