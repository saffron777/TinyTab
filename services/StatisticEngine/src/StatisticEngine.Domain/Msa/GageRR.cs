using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Msa;

/// <summary>Una medición del estudio Gage R&R: pieza, operador, repetición y valor.</summary>
public sealed record GageRow(string Part, string Operator, double Trial, double Value);

/// <summary>Resultado del estudio Gage R&R cruzado por ANOVA.</summary>
public sealed record GageRROutput(
    double Parts,
    double Operators,
    double Trials,
    double Repeatability,
    double Reproducibility,
    double Interaction,
    double PartVariation,
    double TotalVariation,
    double Grr,
    double PercentContribution,
    double PercentStudy,
    double PercentTolerance,
    double Ndc,
    AnovaTableRow[] Table);

/// <summary>Estadísticos por operador del estudio.</summary>
public sealed record GageOperatorStatistic(string Operator, double Mean, double Sd, double N);

/// <summary>
/// Gage R&R cruzado por ANOVA (port 1:1 de <c>msa.ts</c>). La columna <c>p</c> de la tabla
/// ANOVA es el percentil 95 de F (bisección con objetivo 0,05), no un p-valor: se reproduce
/// el defecto del motor original.
/// </summary>
public static class GageRR
{
    public static Result<GageRROutput> GageRRStudy(GageRow[] rows, double? tolerance = null)
    {
        if (rows.Length < 8)
        {
            return Result<GageRROutput>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos 8 mediciones.");
        }

        var parts = Unique(rows, static row => row.Part);
        var operators = Unique(rows, static row => row.Operator);
        var trials = Unique(rows, static row => row.Trial);
        double p = parts.Count;
        double o = operators.Count;
        double r = trials.Count;
        if (p < 2 || o < 2 || r < 2)
        {
            return Result<GageRROutput>.Err(DomainErrorCode.InvalidInput, "Se requieren al menos 2 piezas, 2 operadores y 2 repeticiones.");
        }

        var expected = p * o * r;
        if (rows.Length != expected)
        {
            return Result<GageRROutput>.Err(DomainErrorCode.InvalidInput, $"El diseño debe ser balanceado y completo ({expected} mediciones).");
        }

        var values = new double[rows.Length];
        for (var i = 0; i < rows.Length; i++) values[i] = rows[i].Value;
        var total = NumericMath.Mean(values);
        var n = values.Length;

        var ssTotal = 0.0;
        foreach (var value in values) ssTotal += Math.Pow(value - total, 2);

        var partTotals = Accumulate(rows, static row => row.Part);
        var operatorTotals = Accumulate(rows, static row => row.Operator);
        var cellTotals = Accumulate(rows, static row => $"{row.Part}|{row.Operator}");

        // paridad: msa.ts:123-127 — sumas de cuadrados por totales de subgrupo.
        var partCorrection = SumOfSquares(partTotals);
        var operatorCorrection = SumOfSquares(operatorTotals);
        var cellCorrection = SumOfSquares(cellTotals);
        var ssParts = partCorrection - n * total * total;
        var ssOperators = operatorCorrection - n * total * total;
        var ssCells = cellCorrection - n * total * total;
        var ssInteraction = Math.Max(0, ssCells - ssParts - ssOperators);
        var ssRepeatability = Math.Max(0, ssTotal - ssCells);

        var dfParts = p - 1;
        var dfOperators = o - 1;
        var dfInteraction = dfParts * dfOperators;
        var dfRepeatability = p * o * (r - 1);
        var dfTotal = n - 1;

        var msParts = ssParts / dfParts;
        var msOperators = ssOperators / dfOperators;
        var msInteraction = dfInteraction > 0 ? ssInteraction / dfInteraction : double.NaN;
        var msRepeatability = ssRepeatability / dfRepeatability;

        var fParts = msParts / msInteraction;
        var fOperators = msOperators / msInteraction;
        var fInteraction = msInteraction / msRepeatability;

        var varRepeat = msRepeatability;
        var varInteraction = Math.Max(0, (msInteraction - msRepeatability) / r);
        var varOperator = Math.Max(0, (msOperators - msInteraction) / (p * r));
        var varPart = Math.Max(0, (msParts - msInteraction) / (o * r));

        var grr = Math.Sqrt(varRepeat + varInteraction + varOperator);
        var totalVariation = Math.Sqrt(grr * grr + varPart);
        var toleranceValue = tolerance ?? totalVariation * 6;

        return Result<GageRROutput>.Ok(new GageRROutput(
            p,
            o,
            r,
            Math.Sqrt(varRepeat),
            Math.Sqrt(varOperator + varInteraction),
            Math.Sqrt(varInteraction),
            Math.Sqrt(varPart),
            totalVariation,
            grr,
            totalVariation > 0 ? grr * grr / (totalVariation * totalVariation) * 100 : double.NaN,
            totalVariation > 0 ? grr / totalVariation * 100 : double.NaN,
            toleranceValue > 0 ? 6 * grr / toleranceValue * 100 : double.NaN,
            grr > 0 ? 1.41 * Math.Sqrt(varPart) / grr : double.NaN,
            [
                new AnovaTableRow("Pieza", dfParts, ssParts, msParts, fParts, FUpper(fParts, dfParts, dfInteraction)),
                new AnovaTableRow("Operador", dfOperators, ssOperators, msOperators, fOperators, FUpper(fOperators, dfOperators, dfInteraction)),
                new AnovaTableRow("Pieza × Operador", dfInteraction, ssInteraction, msInteraction, fInteraction, FUpper(fInteraction, dfInteraction, dfRepeatability)),
                new AnovaTableRow("Repetibilidad", dfRepeatability, ssRepeatability, msRepeatability, double.NaN, double.NaN),
                new AnovaTableRow("Total", dfTotal, ssTotal, double.NaN, double.NaN, double.NaN)
            ]));
    }

    /// <summary>Media, desviación estándar y tamaño por operador, en orden de aparición.</summary>
    public static GageOperatorStatistic[] GageStatisticsByOperator(GageRow[] rows)
    {
        var map = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        var ordered = new List<string>();
        foreach (var row in rows)
        {
            if (!map.TryGetValue(row.Operator, out var values))
            {
                values = [];
                map[row.Operator] = values;
                ordered.Add(row.Operator);
            }
            values.Add(row.Value);
        }

        var output = new GageOperatorStatistic[ordered.Count];
        for (var index = 0; index < ordered.Count; index++)
        {
            var operatorName = ordered[index];
            var values = map[operatorName];
            output[index] = new GageOperatorStatistic(
                operatorName,
                NumericMath.Mean(values),
                NumericMath.Sd(values),
                values.Count);
        }
        return output;
    }

    private sealed class Accumulator
    {
        public double Sum;
        public double Count;
    }

    private static List<Accumulator> Accumulate(GageRow[] rows, Func<GageRow, string> key)
    {
        var map = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        var ordered = new List<Accumulator>();
        foreach (var row in rows)
        {
            var name = key(row);
            if (!map.TryGetValue(name, out var accumulator))
            {
                accumulator = new Accumulator();
                map[name] = accumulator;
                ordered.Add(accumulator);
            }
            accumulator.Sum += row.Value;
            accumulator.Count += 1;
        }
        return ordered;
    }

    private static double SumOfSquares(List<Accumulator> totals)
    {
        var acc = 0.0;
        foreach (var item in totals) acc += item.Sum * item.Sum / item.Count;
        return acc;
    }

    private static List<double> Unique(GageRow[] rows, Func<GageRow, double> selector)
    {
        var seen = new List<double>();
        foreach (var row in rows)
        {
            var value = selector(row);
            if (!seen.Contains(value)) seen.Add(value);
        }
        return seen;
    }

    private static List<string> Unique(GageRow[] rows, Func<GageRow, string> selector)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var values = new List<string>();
        foreach (var row in rows)
        {
            var value = selector(row);
            if (seen.Add(value)) values.Add(value);
        }
        return values;
    }

    /// <summary>
    /// Percentil 95 de F por bisección con objetivo 0,05, idéntico a <c>fUpper</c> de
    /// <c>msa.ts</c>. No es un p-valor: este es el defecto que se reproduce.
    /// </summary>
    private static double FUpper(double f, double df1, double df2)
    {
        if (!(f >= 0) || !(df2 > 0)) return double.NaN;
        var lo = 0.0;
        var hi = 1.0;
        for (var i = 0; i < 200 && hi < 1e6; i++) hi *= 2;
        for (var i = 0; i < 200; i++)
        {
            var mid = (lo + hi) / 2;
            if (FCdfLocal(mid, df1, df2) < 1 - 0.05) lo = mid;
            else hi = mid;
        }
        return hi;
    }

    /// <summary>CDF de F local de <c>msa.ts</c> (betacf con tope de 300 iteraciones).</summary>
    private static double FCdfLocal(double f, double df1, double df2)
    {
        var x = df1 * f / (df1 * f + df2);
        var a = df1 / 2;
        var b = df2 / 2;
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        var logBeta = Lgamma(a + b) + a * Math.Log(x) + b * Math.Log(1 - x) - Lgamma(a) - Lgamma(b);
        var front = Math.Exp(logBeta);
        if (x < (a + 1) / (a + b + 2)) return front * BetaContinuedFraction(a, b, x) / a;
        return 1 - front * BetaContinuedFraction(b, a, 1 - x) / b;
    }

    private static double BetaContinuedFraction(double a, double b, double x)
    {
        var qab = a + b;
        var qap = a + 1;
        var qam = a - 1;
        var c = 1.0;
        var d = 1 - qab * x / qap;
        if (Math.Abs(d) < 1e-300) d = 1e-300;
        d = 1 / d;
        var h = d;
        for (var m = 1; m < 300; m++)
        {
            var m2 = 2 * m;
            var aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1 + aa * d;
            if (Math.Abs(d) < 1e-300) d = 1e-300;
            c = 1 + aa / c;
            if (Math.Abs(c) < 1e-300) c = 1e-300;
            d = 1 / d;
            h *= d * c;
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1 + aa * d;
            if (Math.Abs(d) < 1e-300) d = 1e-300;
            c = 1 + aa / c;
            if (Math.Abs(c) < 1e-300) c = 1e-300;
            d = 1 / d;
            var delta = d * c;
            h *= delta;
            if (Math.Abs(delta - 1) < 1e-16) break;
        }
        return h;
    }

    private static double Lgamma(double x)
    {
        if (x < 0.5) return Math.Log(Math.PI / Math.Sin(Math.PI * x)) - Lgamma(1 - x);
        var z = x - 1;
        var a = LanczosCoefficients[0];
        var t = z + 7.5;
        for (var i = 1; i < LanczosCoefficients.Length; i++) a += LanczosCoefficients[i] / (z + i);
        return 0.5 * Math.Log(2 * Math.PI) + (z + 0.5) * Math.Log(t) - t + Math.Log(a);
    }

    private static readonly double[] LanczosCoefficients =
    [
        0.99999999999980993,
        676.5203681218851,
        -1259.1392167224028,
        771.32342877765313,
        -176.61502916214059,
        12.507343278686905,
        -0.13857109526572012,
        9.9843695780195716e-6,
        1.5056327351493116e-7
    ];
}
