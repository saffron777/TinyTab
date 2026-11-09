using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Reliability;

/// <summary>Ajuste Weibull de 2 parámetros (paridad: <c>reliability.ts:5-11</c>).</summary>
public sealed record WeibullFitResult(double Shape, double Scale, double LogLikelihood, double Aic, int N);

/// <summary>Ajuste lognormal (paridad: <c>reliability.ts:56-61</c>).</summary>
public sealed record LogNormalFitResult(double Location, double Scale, double LogLikelihood, int N);

/// <summary>Kaplan-Meier con Greenwood (paridad: <c>reliability.ts:74-82</c>).</summary>
public sealed record KaplanMeierResult(
    double[] Times,
    double[] Survival,
    double[] StandardError,
    double[] AtRisk,
    double[] Events,
    double Median);

/// <summary>Punto del gráfico de probabilidad Weibull (paridad: <c>reliability.ts:114-122</c>).</summary>
public sealed record ProbabilityPlotPoint(double X, double Y, double Rank);

/// <summary>
/// Fiabilidad y supervivencia. Paridad 1:1 con <c>reliability.ts</c>: búsqueda Newton
/// numérica del MLE de Weibull (200 iteraciones, pasos con tope ±0.5) y Kaplan-Meier.
/// </summary>
public static class ReliabilityAnalysis
{
    /// <summary>Ajuste Weibull(2p) por máxima verosimilitud (paridad: <c>reliability.ts:23-54</c>).</summary>
    public static Result<WeibullFitResult> WeibullFit(IReadOnlyList<double> data)
    {
        var positive = Positive(data);
        if (positive.Count < 3) return Result<WeibullFitResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos 3 tiempos positivos.");

        var meanPositive = NumericMath.Mean(positive);
        var cv = NumericMath.Sd(positive) / meanPositive;
        var shape = JsMath.Max(0.2, JsMath.Min(10, 1.2 / JsMath.Max(cv, 0.05)));
        var scale = meanPositive / JsMath.Max(0.1, Math.Exp(SpecialFunctions.LogGamma(1 + 1 / shape)));
        var best = WeibullLogLikelihood(positive, shape, scale);

        for (var iteration = 0; iteration < 200; iteration++)
        {
            var hShape = shape * 1e-4;
            var hScale = scale * 1e-4;
            var dShape = (WeibullLogLikelihood(positive, shape + hShape, scale) - WeibullLogLikelihood(positive, shape - hShape, scale)) / (2 * hShape);
            var dScale = (WeibullLogLikelihood(positive, shape, scale + hScale) - WeibullLogLikelihood(positive, shape, scale - hScale)) / (2 * hScale);
            var d2Shape = (WeibullLogLikelihood(positive, shape + hShape, scale) - 2 * best + WeibullLogLikelihood(positive, shape - hShape, scale)) / (hShape * hShape);
            var d2Scale = (WeibullLogLikelihood(positive, shape, scale + hScale) - 2 * best + WeibullLogLikelihood(positive, shape, scale - hScale)) / (hScale * hScale);
            var dMixed = (WeibullLogLikelihood(positive, shape + hShape, scale + hScale)
                - WeibullLogLikelihood(positive, shape + hShape, scale - hScale)
                - WeibullLogLikelihood(positive, shape - hShape, scale + hScale)
                + WeibullLogLikelihood(positive, shape - hShape, scale - hScale)) / (4 * hShape * hScale);
            var det = d2Shape * d2Scale - dMixed * dMixed;
            // paridad: reliability.ts:40 — `!(Math.abs(det) > 1e-12)`.
            if (!(Math.Abs(det) > 1e-12)) break;
            var stepShape = (d2Scale * dShape - dMixed * dScale) / det;
            var stepScale = (-dMixed * dShape + d2Shape * dScale) / det;
            var nextShape = JsMath.Max(1e-3, shape - JsMath.Clamp(stepShape, -0.5, 0.5));
            var nextScale = JsMath.Max(1e-3, scale - JsMath.Clamp(stepScale, -0.5, 0.5));
            var next = WeibullLogLikelihood(positive, nextShape, nextScale);
            if (!(next > best)) break;
            shape = nextShape;
            scale = nextScale;
            var improvement = next - best;
            best = next;
            if (improvement < 1e-10) break;
        }

        return Result<WeibullFitResult>.Ok(new WeibullFitResult(shape, scale, best, -2 * best + 4, positive.Count));
    }

    /// <summary>Ajuste lognormal por momentos de los logaritmos (paridad: <c>reliability.ts:63-72</c>).</summary>
    public static Result<LogNormalFitResult> LognormalFit(IReadOnlyList<double> data)
    {
        var positive = Positive(data);
        if (positive.Count < 2) return Result<LogNormalFitResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos 2 tiempos positivos.");

        var logs = new double[positive.Count];
        for (var i = 0; i < positive.Count; i++) logs[i] = Math.Log(positive[i]);
        var location = NumericMath.Mean(logs);
        var scale = NumericMath.Sd(logs);
        if (!(scale > 0)) return Result<LogNormalFitResult>.Err(DomainErrorCode.InvalidInput, "Los datos no tienen variabilidad.");

        var logLikelihood = 0.0;
        for (var i = 0; i < positive.Count; i++)
        {
            var logValue = Math.Log(positive[i]);
            logLikelihood += -logValue - Math.Log(scale) - 0.5 * Math.Log(2 * Math.PI)
                - (logValue - location) * (logValue - location) / (2 * scale * scale);
        }
        return Result<LogNormalFitResult>.Ok(new LogNormalFitResult(location, scale, logLikelihood, positive.Count));
    }

    /// <summary>Kaplan-Meier con error estándar de Greenwood (paridad: <c>reliability.ts:84-112</c>).</summary>
    public static Result<KaplanMeierResult> KaplanMeier(IReadOnlyList<double> times, IReadOnlyList<bool> events)
    {
        var n = Math.Min(times.Count, events.Count);
        if (n < 2) return Result<KaplanMeierResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos 2 observaciones.");

        var rows = new (double Time, bool Event)[n];
        for (var i = 0; i < n; i++) rows[i] = (times[i], events[i]);
        var ordered = rows.OrderBy(row => row.Time).ToArray();

        // paridad: reliability.ts:88 — Set conserva el orden de inserción (tiempos ascendentes).
        var uniqueTimes = new List<double>();
        var seen = new HashSet<double>();
        for (var i = 0; i < ordered.Length; i++)
        {
            if (seen.Add(ordered[i].Time)) uniqueTimes.Add(ordered[i].Time);
        }

        var outputTimes = new List<double> { 0 };
        var outputSurvival = new List<double> { 1 };
        var outputSe = new List<double> { 0 };
        var outputRisk = new List<double> { n };
        var outputEvents = new List<double> { 0 };
        var survival = 1.0;
        var varianceSum = 0.0;
        var median = double.PositiveInfinity;

        foreach (var time in uniqueTimes)
        {
            var atRisk = 0;
            var count = 0;
            for (var i = 0; i < ordered.Length; i++)
            {
                if (ordered[i].Time >= time) atRisk++;
                if (ordered[i].Time == time && ordered[i].Event) count++;
            }
            if (count > 0)
            {
                survival *= (atRisk - count) / (double)atRisk;
                var adjustedRisk = atRisk - count;
                // paridad: reliability.ts:102 — `(atRisk - events || 1)`.
                if (adjustedRisk == 0) adjustedRisk = 1;
                varianceSum += count / (atRisk * (double)adjustedRisk);
                outputTimes.Add(time);
                outputSurvival.Add(survival);
                outputSe.Add(survival * Math.Sqrt(varianceSum));
                outputRisk.Add(atRisk);
                outputEvents.Add(count);
                if (double.IsPositiveInfinity(median) && survival <= 0.5) median = time;
            }
        }

        return Result<KaplanMeierResult>.Ok(new KaplanMeierResult(
            [.. outputTimes],
            [.. outputSurvival],
            [.. outputSe],
            [.. outputRisk],
            [.. outputEvents],
            median));
    }

    /// <summary>Puntos para un gráfico de probabilidad Weibull (paridad: <c>reliability.ts:114-122</c>).</summary>
    public static ProbabilityPlotPoint[] ProbabilityPlotPoints(IReadOnlyList<double> data)
    {
        var positive = Positive(data);
        positive.Sort();
        var n = positive.Count;
        var points = new ProbabilityPlotPoint[n];
        for (var i = 0; i < n; i++)
        {
            var rank = (i + 1 - 0.3) / (n + 0.4);
            points[i] = new ProbabilityPlotPoint(
                Math.Log(positive[i]),
                Math.Log(-Math.Log(1 - rank)),
                rank);
        }
        return points;
    }

    /// <summary>Cuantil de una Weibull ajustada (paridad: <c>reliability.ts:124-127</c>).</summary>
    public static double WeibullQuantile(double probability, double shape, double scale) =>
        scale * Math.Pow(-Math.Log(1 - probability), 1 / shape);

    /// <summary>Fiabilidad en un tiempo dado (paridad: <c>reliability.ts:129-131</c>).</summary>
    public static double ReliabilityAt(double time, double shape, double scale) =>
        Math.Exp(-Math.Pow(time / scale, shape));

    /// <summary>Log-verosimilitud Weibull (paridad: <c>reliability.ts:13-21</c>).</summary>
    private static double WeibullLogLikelihood(List<double> data, double shape, double scale)
    {
        if (!(shape > 0) || !(scale > 0)) return double.NegativeInfinity;
        var total = 0.0;
        for (var i = 0; i < data.Count; i++)
        {
            var value = data[i];
            if (value <= 0) return double.NegativeInfinity;
            total += Math.Log(shape) - Math.Log(scale)
                + (shape - 1) * (Math.Log(value) - Math.Log(scale))
                - Math.Pow(value / scale, shape);
        }
        return total;
    }

    private static List<double> Positive(IReadOnlyList<double> data)
    {
        var output = new List<double>();
        for (var i = 0; i < data.Count; i++)
        {
            if (data[i] > 0) output.Add(data[i]);
        }
        return output;
    }
}
