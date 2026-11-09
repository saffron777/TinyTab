using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Quality;

/// <summary>Punto de una carta de control (paridad: <c>spc.ts:9-17</c>).</summary>
public sealed record ControlPoint(
    int Index,
    double Value,
    double CenterLine,
    double LowerLimit,
    double UpperLimit,
    bool OutOfControl,
    string[] Rules);

/// <summary>Resultado de una carta de control (paridad: <c>spc.ts:19-27</c>).</summary>
public sealed record ControlChartResult(
    string Chart,
    double CenterLine,
    double LowerLimit,
    double UpperLimit,
    double Sigma,
    ControlPoint[] Points,
    int Violations);

/// <summary>Subgrupo racional de la carta X̄-R / X̄-S (paridad: <c>spc.ts:29-31</c>).</summary>
public sealed record SubgroupPoint(double[] Values);

public sealed record XbarRResult(ControlChartResult Xbar, ControlChartResult Range);

public sealed record XbarSResult(ControlChartResult Xbar, ControlChartResult S);

public sealed record IndividualsResult(ControlChartResult Individuals, ControlChartResult MovingRange);

/// <summary>
/// Cartas de control del motor TypeScript. Se reproducen 1:1 las fórmulas, los límites y
/// las decisiones de `err(...)` de <c>spc.ts</c>, incluidos los defectos conocidos del motor.
/// </summary>
public static class ControlCharts
{
    /// <summary>Carta X̄-R (paridad: <c>spc.ts:118-141</c>).</summary>
    public static Result<XbarRResult> XbarRChart(IReadOnlyList<SubgroupPoint> subgroups)
    {
        if (subgroups.Count < 1) return Result<XbarRResult>.Err(DomainErrorCode.InsufficientData, "No hay subgrupos.");
        if (!TrySubgroupSize(subgroups, out var n)) return Result<XbarRResult>.Err(DomainErrorCode.InvalidInput, "Todos los subgrupos deben tener el mismo tamaño.");
        if (n < 2) return Result<XbarRResult>.Err(DomainErrorCode.InvalidInput, "El tamaño de subgrupo debe ser al menos 2.");

        var constants = ChartConstants.For(n);
        var means = new double[subgroups.Count];
        var ranges = new double[subgroups.Count];
        for (var i = 0; i < subgroups.Count; i++)
        {
            var values = subgroups[i].Values;
            means[i] = NumericMath.Mean(values);
            ranges[i] = JsMath.MaxOf(values) - JsMath.MinOf(values);
        }
        var xbarbar = NumericMath.Mean(means);
        var rbar = NumericMath.Mean(ranges);
        var sigma = rbar / constants.D2;

        // paridad: spc.ts:139 — `(constants.d3 * sigma) || NaN`: un 0 se convierte en NaN.
        var rangeSigma = constants.D3 * sigma;
        if (rangeSigma == 0) rangeSigma = double.NaN;

        return Result<XbarRResult>.Ok(new XbarRResult(
            BuildChart("X\u0304", means, xbarbar, sigma, xbarbar - constants.A2 * rbar, xbarbar + constants.A2 * rbar),
            BuildChart("R", ranges, rbar, rangeSigma, constants.D3Chart * rbar, constants.D4 * rbar)));
    }

    /// <summary>Carta X̄-S (paridad: <c>spc.ts:143-159</c>).</summary>
    public static Result<XbarSResult> XbarSChart(IReadOnlyList<SubgroupPoint> subgroups)
    {
        if (subgroups.Count < 1) return Result<XbarSResult>.Err(DomainErrorCode.InsufficientData, "No hay subgrupos.");
        if (!TrySubgroupSize(subgroups, out var n)) return Result<XbarSResult>.Err(DomainErrorCode.InvalidInput, "Todos los subgrupos deben tener el mismo tamaño.");
        if (n < 2) return Result<XbarSResult>.Err(DomainErrorCode.InvalidInput, "El tamaño de subgrupo debe ser al menos 2.");

        var constants = ChartConstants.For(n);
        var means = new double[subgroups.Count];
        var deviations = new double[subgroups.Count];
        for (var i = 0; i < subgroups.Count; i++)
        {
            var values = subgroups[i].Values;
            means[i] = NumericMath.Mean(values);
            deviations[i] = NumericMath.Sd(values);
        }
        var xbarbar = NumericMath.Mean(means);
        var sbar = NumericMath.Mean(deviations);
        var sigma = sbar / Math.Sqrt(1 - 45 / (97.0 * n) + 1 / (9.0 * n * n));
        var halfWidth = constants.A2 * sbar * (3 / (constants.A2 * Math.Sqrt(n)));

        return Result<XbarSResult>.Ok(new XbarSResult(
            BuildChart("X\u0304", means, xbarbar, sigma, xbarbar - halfWidth, xbarbar + halfWidth),
            BuildChart("S", deviations, sbar, sigma, constants.B3 * sbar, constants.B4 * sbar)));
    }

    /// <summary>Carta I-MR (paridad: <c>spc.ts:161-171</c>).</summary>
    public static Result<IndividualsResult> IndividualsChart(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return Result<IndividualsResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos 2 observaciones.");

        var ranges = new double[values.Count - 1];
        for (var i = 1; i < values.Count; i++) ranges[i - 1] = Math.Abs(values[i] - values[i - 1]);
        var mrbar = NumericMath.Mean(ranges);
        var sigma = mrbar / 1.128;
        var center = NumericMath.Mean(values);

        return Result<IndividualsResult>.Ok(new IndividualsResult(
            BuildChart("I", [.. values], center, sigma, center - 3 * sigma, center + 3 * sigma),
            BuildChart("MR", ranges, mrbar, mrbar / 1.128, 0, 3.267 * mrbar)));
    }

    /// <summary>Carta P (paridad: <c>spc.ts:173-180</c>).</summary>
    public static Result<ControlChartResult> PChart(IReadOnlyList<double> defectives, IReadOnlyList<double> samples)
    {
        if (defectives.Count != samples.Count || defectives.Count < 2) return Result<ControlChartResult>.Err(DomainErrorCode.InvalidInput, "Datos incompatibles.");

        var proportions = new double[defectives.Count];
        for (var i = 0; i < defectives.Count; i++) proportions[i] = defectives[i] / samples[i];
        var pbar = NumericMath.Sum(defectives) / NumericMath.Sum(samples);
        var nbar = NumericMath.Mean(samples);
        var sigma = Math.Sqrt(pbar * (1 - pbar) / nbar);
        return Result<ControlChartResult>.Ok(BuildChart(
            "P", proportions, pbar, sigma, JsMath.Max(0, pbar - 3 * sigma), JsMath.Min(1, pbar + 3 * sigma)));
    }

    /// <summary>Carta NP (paridad: <c>spc.ts:182-188</c>).</summary>
    public static Result<ControlChartResult> NpChart(IReadOnlyList<double> defectives, double sampleSize)
    {
        if (defectives.Count < 2) return Result<ControlChartResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos 2 muestras.");

        var pbar = NumericMath.Sum(defectives) / (sampleSize * defectives.Count);
        var npbar = sampleSize * pbar;
        var sigma = Math.Sqrt(sampleSize * pbar * (1 - pbar));
        return Result<ControlChartResult>.Ok(BuildChart(
            "NP", [.. defectives], npbar, sigma, JsMath.Max(0, npbar - 3 * sigma), npbar + 3 * sigma));
    }

    /// <summary>Carta C (paridad: <c>spc.ts:190-195</c>).</summary>
    public static Result<ControlChartResult> CChart(IReadOnlyList<double> defects)
    {
        if (defects.Count < 2) return Result<ControlChartResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos 2 muestras.");

        var cbar = NumericMath.Mean(defects);
        var sigma = Math.Sqrt(cbar);
        return Result<ControlChartResult>.Ok(BuildChart(
            "C", [.. defects], cbar, sigma, JsMath.Max(0, cbar - 3 * sigma), cbar + 3 * sigma));
    }

    /// <summary>Carta U (paridad: <c>spc.ts:197-204</c>).</summary>
    public static Result<ControlChartResult> UChart(IReadOnlyList<double> defects, IReadOnlyList<double> units)
    {
        if (defects.Count != units.Count || defects.Count < 2) return Result<ControlChartResult>.Err(DomainErrorCode.InvalidInput, "Datos incompatibles.");

        var rates = new double[defects.Count];
        for (var i = 0; i < defects.Count; i++) rates[i] = defects[i] / units[i];
        var ubar = NumericMath.Sum(defects) / NumericMath.Sum(units);
        var nbar = NumericMath.Mean(units);
        var sigma = Math.Sqrt(ubar / nbar);
        return Result<ControlChartResult>.Ok(BuildChart(
            "U", rates, ubar, sigma, JsMath.Max(0, ubar - 3 * sigma), ubar + 3 * sigma));
    }

    /// <summary>
    /// Carta EWMA (paridad: <c>spc.ts:206-219</c>). Defecto reproducido: el ancho de los
    /// límites usa el factor de varianza del primer punto para todos los índices.
    /// </summary>
    public static Result<ControlChartResult> EwmaChart(IReadOnlyList<double> values, double lambda = 0.2, double? sigmaKnown = null)
    {
        if (values.Count < 2) return Result<ControlChartResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos 2 observaciones.");
        if (!(lambda > 0 && lambda <= 1)) return Result<ControlChartResult>.Err(DomainErrorCode.InvalidParameter, "λ debe estar en (0, 1].");

        var center = NumericMath.Mean(values);
        var sigma = sigmaKnown ?? NumericMath.Sd(values);
        var smoothing = new double[values.Count];
        var previous = center;
        for (var i = 0; i < values.Count; i++)
        {
            previous = lambda * values[i] + (1 - lambda) * previous;
            smoothing[i] = previous;
        }
        var limitWidth = 3 * sigma * Math.Sqrt(lambda / (2 - lambda) * (1 - Math.Pow(1 - lambda, 2)));
        return Result<ControlChartResult>.Ok(BuildChart(
            "EWMA", smoothing, center, sigma, center - limitWidth, center + limitWidth));
    }

    /// <summary>
    /// Carta CUSUM (paridad: <c>spc.ts:221-237</c>). Defecto reproducido: límites fijos
    /// 0..5, centro 0 y sigma 1; no existe el parámetro <c>h</c>.
    /// </summary>
    public static Result<ControlChartResult> CusumChart(IReadOnlyList<double> values, double? target = null, double k = 0.5)
    {
        if (values.Count < 3) return Result<ControlChartResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos 3 observaciones.");

        var center = target ?? NumericMath.Mean(values);
        var sigma = NumericMath.Sd(values);
        var positive = new double[values.Count];
        var negative = new double[values.Count];
        var cPlus = 0.0;
        var cMinus = 0.0;
        for (var i = 0; i < values.Count; i++)
        {
            cPlus = Math.Max(0, cPlus + (values[i] - center) / sigma - k);
            cMinus = Math.Min(0, cMinus + (values[i] - center) / sigma + k);
            positive[i] = cPlus;
            negative[i] = cMinus;
        }
        var combined = new double[values.Count];
        for (var i = 0; i < values.Count; i++) combined[i] = positive[i] + Math.Abs(negative[i]);
        return Result<ControlChartResult>.Ok(BuildChart("CUSUM", combined, 0, 1, 0, 5));
    }

    private static bool TrySubgroupSize(IReadOnlyList<SubgroupPoint> subgroups, out int size)
    {
        size = subgroups[0].Values.Length;
        for (var i = 1; i < subgroups.Count; i++)
        {
            if (subgroups[i].Values.Length != size) return false;
        }
        return true;
    }

    /// <summary>Construye la carta evaluando las reglas de Nelson (paridad: <c>spc.ts:89-116</c>).</summary>
    private static ControlChartResult BuildChart(
        string chart,
        double[] values,
        double center,
        double sigma,
        double lower,
        double upper)
    {
        var rules = NelsonRules.EvaluateRules(values, center, sigma);
        var points = new ControlPoint[values.Length];
        var violations = 0;
        for (var i = 0; i < values.Length; i++)
        {
            var outOfControl = values[i] < lower || values[i] > upper || rules[i].Length > 0;
            if (outOfControl) violations++;
            points[i] = new ControlPoint(i + 1, values[i], center, lower, upper, outOfControl, rules[i]);
        }
        return new ControlChartResult(chart, center, lower, upper, sigma, points, violations);
    }
}
