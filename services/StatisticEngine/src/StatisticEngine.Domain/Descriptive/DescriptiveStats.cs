using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Descriptive;

/// <summary>Estadística descriptiva de una muestra (mismas claves que <c>DescriptiveResult</c>).</summary>
public sealed record DescriptiveResult(
    double N,
    double Missing,
    double Mean,
    double SeMean,
    double Sd,
    double Variance,
    double Cv,
    double Min,
    double Q1,
    double Median,
    double Q3,
    double Max,
    double Iqr,
    double Range,
    double Skewness,
    double Kurtosis,
    double Sum,
    double SumSquares);

/// <summary>Entrada de <see cref="DescriptiveStats.DescribeByGroup"/>: etiqueta y valores.</summary>
public sealed record DescriptiveGroup(string Group, IReadOnlyList<double> Values);

/// <summary>Estadísticas de un grupo (<c>describeByGroup</c>).</summary>
public sealed record GroupedDescriptive(string Group, IReadOnlyList<double> Values, DescriptiveResult Stats);

/// <summary>Intervalo de confianza para la media.</summary>
public sealed record MeanConfidenceIntervalResult(double Lower, double Upper, double HalfWidth);

/// <summary>Matriz de correlación con etiquetas.</summary>
public sealed record CorrelationMatrixResult(IReadOnlyList<string> Labels, IReadOnlyList<IReadOnlyList<double>> Matrix);

/// <summary>
/// Estadística descriptiva del motor (port 1:1 de <c>descriptive.ts</c>, incluida su copia
/// local de la inversa normal y del cuantil t).
/// </summary>
public static class DescriptiveStats
{
    /// <summary><c>describe(values, missing = 0)</c>.</summary>
    public static DescriptiveResult Describe(IReadOnlyList<double> values, double missing = 0)
    {
        var n = values.Count;
        var center = NumericMath.Mean(values);
        var deviation = NumericMath.Sd(values);
        var squares = new double[n];
        for (var i = 0; i < n; i++) squares[i] = values[i] * values[i];
        return new DescriptiveResult(
            N: n,
            Missing: missing,
            Mean: center,
            SeMean: n > 1 ? NumericMath.StandardErrorOfMean(values) : double.NaN,
            Sd: deviation,
            Variance: NumericMath.Variance(values),
            Cv: center != 0 ? deviation / Math.Abs(center) * 100 : double.NaN,
            Min: n > 0 ? NumericMath.MinOf(values) : double.NaN,
            Q1: NumericMath.Quantile(values, 0.25),
            Median: NumericMath.Median(values),
            Q3: NumericMath.Quantile(values, 0.75),
            Max: n > 0 ? NumericMath.MaxOf(values) : double.NaN,
            Iqr: NumericMath.Iqr(values),
            Range: n > 0 ? NumericMath.MaxOf(values) - NumericMath.MinOf(values) : double.NaN,
            Skewness: NumericMath.Skewness(values),
            Kurtosis: NumericMath.Kurtosis(values),
            Sum: NumericMath.Sum(values),
            SumSquares: NumericMath.Sum(squares));
    }

    /// <summary><c>meanConfidenceInterval(values, confidence = 0.95, sigma?)</c>.</summary>
    public static MeanConfidenceIntervalResult MeanConfidenceInterval(
        IReadOnlyList<double> values,
        double confidence = 0.95,
        double? sigma = null)
    {
        var n = values.Count;
        var center = NumericMath.Mean(values);
        var standardError = sigma is { } known
            ? known / Math.Sqrt(n)
            : NumericMath.Sd(values) / Math.Sqrt(n);
        var critical = sigma is not null
            ? NormalQuantile(1 - (1 - confidence) / 2)
            : TQuantile(1 - (1 - confidence) / 2, n - 1);
        var halfWidth = critical * standardError;
        return new MeanConfidenceIntervalResult(center - halfWidth, center + halfWidth, halfWidth);
    }

    /// <summary><c>describeByGroup(groups, missing = 0)</c>.</summary>
    public static IReadOnlyList<GroupedDescriptive> DescribeByGroup(
        IReadOnlyList<DescriptiveGroup> groups,
        double missing = 0)
    {
        var output = new List<GroupedDescriptive>(groups.Count);
        foreach (var group in groups)
        {
            output.Add(new GroupedDescriptive(group.Group, [.. group.Values], Describe(group.Values, missing)));
        }
        return output;
    }

    /// <summary><c>correlationMatrix(labels, columns)</c>.</summary>
    public static CorrelationMatrixResult CorrelationMatrix(
        IReadOnlyList<string> labels,
        IReadOnlyList<IReadOnlyList<double>> columns)
    {
        var n = columns.Count;
        var matrix = new List<IReadOnlyList<double>>(n);
        for (var i = 0; i < n; i++)
        {
            var row = new double[n];
            for (var j = 0; j < n; j++)
            {
                row[j] = i == j ? 1 : NumericMath.Correlation(columns[i], columns[j]);
            }
            matrix.Add(row);
        }
        return new CorrelationMatrixResult([.. labels], matrix);
    }

    /// <summary><c>covarianceMatrix(columns)</c>.</summary>
    public static IReadOnlyList<IReadOnlyList<double>> CovarianceMatrix(IReadOnlyList<IReadOnlyList<double>> columns)
    {
        var n = columns.Count;
        var matrix = new List<IReadOnlyList<double>>(n);
        for (var i = 0; i < n; i++)
        {
            var row = new double[n];
            for (var j = 0; j < n; j++)
            {
                row[j] = i == j ? NumericMath.Variance(columns[i]) : NumericMath.Covariance(columns[i], columns[j]);
            }
            matrix.Add(row);
        }
        return matrix;
    }

    /// <summary><c>normalQuantile(p)</c> con la aproximación Beasley-Springer-Moro del módulo.</summary>
    private static double NormalQuantile(double p) => InverseNormalApprox(p);

    // paridad: descriptive.ts:85 — coeficientes exactos de la aproximación BSM.
    private static readonly double[] InverseA =
    [
        0, -3.969683028665376e1, 2.209460984245205e2, -2.759285104469687e2,
        1.38357751867269e2, -3.066479806614716e1, 2.506628277459239
    ];

    private static readonly double[] InverseB =
    [
        0, -5.447609879822406e1, 1.615858368580409e2, -1.556989798598866e2,
        6.680131188771972e1, -1.328068155288572e1
    ];

    private static readonly double[] InverseC =
    [
        0, -7.784894002430293e-3, -3.223964580411365e-1, -2.400758277161838,
        -2.549732539343734, 4.374664141464968, 2.938163982698783
    ];

    private static readonly double[] InverseD =
    [
        0, 7.784695709041462e-3, 3.224671290700398e-1, 2.445134137142996,
        3.754408661907416
    ];

    private static double InverseNormalApprox(double p)
    {
        const double plow = 0.02425;
        const double phigh = 1 - plow;
        if (p <= 0) return double.NegativeInfinity;
        if (p >= 1) return double.PositiveInfinity;
        double x;
        if (p < plow)
        {
            var q = Math.Sqrt(-2 * Math.Log(p));
            x = (((((InverseC[1] * q + InverseC[2]) * q + InverseC[3]) * q + InverseC[4]) * q + InverseC[5]) * q + InverseC[6]);
            x /= ((((InverseD[1] * q + InverseD[2]) * q + InverseD[3]) * q + InverseD[4]) * q + 1);
        }
        else if (p <= phigh)
        {
            var q = p - 0.5;
            var r = q * q;
            x = ((((((InverseA[1] * r + InverseA[2]) * r + InverseA[3]) * r + InverseA[4]) * r + InverseA[5]) * r + InverseA[6]) * q);
            x /= ((((((InverseB[1] * r + InverseB[2]) * r + InverseB[3]) * r + InverseB[4]) * r + InverseB[5]) * r + 1));
        }
        else
        {
            var q = Math.Sqrt(-2 * Math.Log(1 - p));
            x = -(((((InverseC[1] * q + InverseC[2]) * q + InverseC[3]) * q + InverseC[4]) * q + InverseC[5]) * q - InverseC[6]);
            x /= ((((InverseD[1] * q + InverseD[2]) * q + InverseD[3]) * q + InverseD[4]) * q + 1);
        }
        return x;
    }

    // paridad: descriptive.ts:113 — cuantil t por bisección en [-100, 100] y z si df > 200.
    private static double TQuantile(double p, double df)
    {
        if (df <= 0) return double.NaN;
        if (df > 200) return NormalQuantile(p);
        var low = -100.0;
        var high = 100.0;
        for (var i = 0; i < 200; i++)
        {
            var mid = (low + high) / 2;
            if (InferenceDistributions.TCdf(mid, df) < p) low = mid;
            else high = mid;
        }
        return (low + high) / 2;
    }
}
