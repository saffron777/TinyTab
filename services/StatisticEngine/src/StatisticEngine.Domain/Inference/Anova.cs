using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Inference;

/// <summary>Resumen descriptivo de un grupo del ANOVA de un factor.</summary>
public sealed record AnovaGroupSummary(string Label, double N, double Mean, double Sd);

/// <summary>
/// Fila de una tabla ANOVA. En las filas sin contraste (Error y Total) <c>F</c> y <c>P</c>
/// valen NaN, igual que en el motor TypeScript.
/// </summary>
public sealed record AnovaTableRow(string Source, double Df, double Ss, double Ms, double F, double P);

public sealed record OneWayAnovaResult(
    AnovaGroupSummary[] Groups,
    AnovaTableRow[] Table,
    double TotalSS,
    double RSquared,
    double PooledSD);

public sealed record TwoWayAnovaResult(AnovaTableRow[] Table, double RSquared);

/// <summary>
/// ANOVA de uno y dos factores (port 1:1 de <c>anova.ts</c>). El modelo de dos factores es
/// aditivo (sin interacción), admite celdas desbalanceadas y usa <c>dfError = n - a - b + 1</c>.
/// </summary>
public static class Anova
{
    /// <summary>ANOVA de un factor con grupos independientes.</summary>
    public static Result<OneWayAnovaResult> OneWayAnova(double[][] groups, string[] labels)
    {
        var k = groups.Length;
        if (k < 2)
        {
            return Result<OneWayAnovaResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos dos grupos.");
        }

        var summaries = new AnovaGroupSummary[k];
        for (var i = 0; i < k; i++)
        {
            var values = groups[i];
            // paridad: anova.ts:33-38 — una etiqueta ausente cae en "Grupo N".
            summaries[i] = new AnovaGroupSummary(
                i < labels.Length ? labels[i] : $"Grupo {i + 1}",
                values.Length,
                NumericMath.Mean(values),
                NumericMath.Sd(values));
        }

        foreach (var summary in summaries)
        {
            if (summary.N < 2)
            {
                return Result<OneWayAnovaResult>.Err(DomainErrorCode.InsufficientData, "Cada grupo necesita al menos 2 observaciones.");
            }
        }

        var totalCount = 0;
        foreach (var values in groups) totalCount += values.Length;
        var all = new double[totalCount];
        var position = 0;
        foreach (var values in groups)
        {
            foreach (var value in values) all[position++] = value;
        }

        var grandMean = NumericMath.Mean(all);
        var ssBetween = 0.0;
        var ssWithin = 0.0;
        for (var i = 0; i < k; i++)
        {
            var values = groups[i];
            var summary = summaries[i];
            // paridad: anova.ts:44-49 — suma de cuadrados entre grupos dentro de la misma pasada.
            ssBetween += summary.N * Math.Pow(summary.Mean - grandMean, 2);
            foreach (var value in values) ssWithin += Math.Pow(value - summary.Mean, 2);
        }

        var totalSS = ssBetween + ssWithin;
        double dfBetween = k - 1;
        double dfWithin = all.Length - k;
        var msBetween = ssBetween / dfBetween;
        var msWithin = dfWithin > 0 ? ssWithin / dfWithin : double.NaN;
        var f = msBetween / msWithin;
        var p = InferenceDistributions.FCdfUpper(f, dfBetween, dfWithin);
        var table = new[]
        {
            new AnovaTableRow("Factor", dfBetween, ssBetween, msBetween, f, p),
            new AnovaTableRow("Error", dfWithin, ssWithin, msWithin, double.NaN, double.NaN),
            new AnovaTableRow("Total", dfBetween + dfWithin, totalSS, double.NaN, double.NaN, double.NaN)
        };
        return Result<OneWayAnovaResult>.Ok(new OneWayAnovaResult(
            summaries,
            table,
            totalSS,
            totalSS > 0 ? ssBetween / totalSS : double.NaN,
            Math.Sqrt(msWithin)));
    }

    /// <summary>
    /// ANOVA de dos factores aditiva (sin término de interacción). Admite celdas desbalanceadas:
    /// las sumas de cuadrados se calculan sobre las medias marginales.
    /// </summary>
    public static Result<TwoWayAnovaResult> TwoWayAnova(double[] response, string[] factorA, string[] factorB)
    {
        var n = Math.Min(response.Length, Math.Min(factorA.Length, factorB.Length));
        if (n < 4)
        {
            return Result<TwoWayAnovaResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos 4 observaciones.");
        }

        var levelsA = UniqueLevels(factorA, n);
        var levelsB = UniqueLevels(factorB, n);
        if (levelsA.Count < 2 || levelsB.Count < 2)
        {
            return Result<TwoWayAnovaResult>.Err(DomainErrorCode.InvalidInput, "Cada factor necesita al menos dos niveles.");
        }

        var grand = NumericMath.Mean(NumericMath.Slice(response, n));
        var rowTotals = Accumulate(factorA, response, n);
        var columnTotals = Accumulate(factorB, response, n);

        var ssA = 0.0;
        foreach (var item in rowTotals) ssA += item.Count * Math.Pow(item.Sum / item.Count - grand, 2);
        var ssB = 0.0;
        foreach (var item in columnTotals) ssB += item.Count * Math.Pow(item.Sum / item.Count - grand, 2);

        var ssTotal = 0.0;
        for (var i = 0; i < n; i++) ssTotal += Math.Pow(response[i] - grand, 2);

        // paridad: anova.ts:117 — el error absorbe todo lo que la suma aditiva no explica.
        var ssError = Math.Max(0, ssTotal - ssA - ssB);
        double dfA = levelsA.Count - 1;
        double dfB = levelsB.Count - 1;
        double dfError = n - levelsA.Count - levelsB.Count + 1;
        var msA = ssA / dfA;
        var msB = ssB / dfB;
        var msError = dfError > 0 ? ssError / dfError : double.NaN;
        var fA = msA / msError;
        var fB = msB / msError;
        var table = new[]
        {
            new AnovaTableRow("Factor A", dfA, ssA, msA, fA, InferenceDistributions.FCdfUpper(fA, dfA, dfError)),
            new AnovaTableRow("Factor B", dfB, ssB, msB, fB, InferenceDistributions.FCdfUpper(fB, dfB, dfError)),
            new AnovaTableRow("Error", dfError, ssError, msError, double.NaN, double.NaN),
            new AnovaTableRow("Total", n - 1, ssTotal, double.NaN, double.NaN, double.NaN)
        };
        return Result<TwoWayAnovaResult>.Ok(new TwoWayAnovaResult(
            table,
            ssTotal > 0 ? (ssA + ssB) / ssTotal : double.NaN));
    }

    private sealed class GroupAccumulator
    {
        public double Sum;
        public double Count;
    }

    /// <summary>Acumula suma y conteo por clave conservando el orden de inserción del <c>Map</c> de JS.</summary>
    private static List<GroupAccumulator> Accumulate(string[] keys, double[] values, int n)
    {
        var map = new Dictionary<string, GroupAccumulator>(StringComparer.Ordinal);
        var ordered = new List<GroupAccumulator>();
        for (var i = 0; i < n; i++)
        {
            var key = keys[i];
            if (!map.TryGetValue(key, out var accumulator))
            {
                accumulator = new GroupAccumulator();
                map[key] = accumulator;
                ordered.Add(accumulator);
            }
            accumulator.Sum += values[i];
            accumulator.Count += 1;
        }
        return ordered;
    }

    private static List<string> UniqueLevels(string[] factor, int n)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var levels = new List<string>();
        for (var i = 0; i < n; i++)
        {
            if (seen.Add(factor[i])) levels.Add(factor[i]);
        }
        return levels;
    }
}
