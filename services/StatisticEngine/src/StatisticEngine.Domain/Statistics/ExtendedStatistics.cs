using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Statistics;

/// <summary>
/// Funciones de distribución y pruebas que necesitan los análisis extendidos: chi-cuadrado,
/// Kolmogorov-Smirnov (exacto por el método de Marsaglia-Tsang-Wang y por conteo de caminos),
/// Anderson-Darling y Lilliefors (tablas de statsmodels).
/// </summary>
public static class ExtendedStatistics
{
    /// <summary>P(χ² &gt; x) con <paramref name="df"/> grados de libertad.</summary>
    public static double ChiSquareSf(double x, double df) =>
        x <= 0 ? 1 : SpecialFunctions.Gammq(df / 2, x / 2);

    public static double ChiSquareCdf(double x, double df) => 1 - ChiSquareSf(x, df);

    /// <summary>Cuantil de χ² por bisección monótona.</summary>
    public static double ChiSquareInv(double probability, double df)
    {
        if (!(probability > 0 && probability < 1) || !(df > 0)) return double.NaN;
        var low = 0.0;
        var high = Math.Max(10, df * 10 + 100);
        while (ChiSquareCdf(high, df) < probability && high < 1e12) high *= 2;
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var middle = (low + high) / 2;
            if (ChiSquareCdf(middle, df) < probability) low = middle;
            else high = middle;
            if (high - low < 1e-12 * Math.Max(1, high)) break;
        }
        return (low + high) / 2;
    }

    /// <summary>P(F &gt; f).</summary>
    public static double FSf(double f, double df1, double df2)
    {
        if (!(f >= 0)) return double.NaN;
        if (f == 0) return 1;
        return 1 - SpecialFunctions.Betai(df1 / 2, df2 / 2, df1 * f / (df1 * f + df2));
    }

    public static double FCdf(double f, double df1, double df2) => 1 - FSf(f, df1, df2);

    /// <summary>Cuantil de F por bisección monótona.</summary>
    public static double FInv(double probability, double df1, double df2)
    {
        if (!(probability > 0 && probability < 1) || !(df1 > 0) || !(df2 > 0)) return double.NaN;
        var low = 0.0;
        var high = 1.0;
        while (FCdf(high, df1, df2) < probability && high < 1e12) high *= 2;
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var middle = (low + high) / 2;
            if (FCdf(middle, df1, df2) < probability) low = middle;
            else high = middle;
            if (high - low < 1e-13 * Math.Max(1, high)) break;
        }
        return (low + high) / 2;
    }

    /// <summary>P(X ≤ k) para X ~ Binomial(n, p).</summary>
    public static double BinomialCdf(int k, int n, double p)
    {
        if (k < 0) return 0;
        if (k >= n) return 1;
        if (p <= 0) return 1;
        if (p >= 1) return 0;
        // Relación con la beta incompleta: P(X ≤ k) = I_{1-p}(n-k, k+1).
        return SpecialFunctions.Betai(n - k, k + 1, 1 - p);
    }

    public static double BinomialPmf(int k, int n, double p)
    {
        if (k < 0 || k > n) return 0;
        var log = NumericMath.LogCombination(n, k) + k * Math.Log(p) + (n - k) * Math.Log(1 - p);
        return Math.Exp(log);
    }

    /// <summary>
    /// CDF exacta de Kolmogorov-Smirnov de una muestra (Marsaglia, Tsang y Wang 2003).
    /// </summary>
    public static double KolmogorovCdf(double d, int n)
    {
        if (d <= 0) return 0;
        if (d >= 1) return 1;
        var k = (int)(n * d) + 1;
        var m = 2 * k - 1;
        var h = k - n * d;
        if (m > 220)
        {
            // Para d grande el método matricial es costoso; la aproximación asintótica es excelente.
            return 1 - KolmogorovAsymptoticSf(d, n);
        }

        var matrix = new double[m][];
        for (var i = 0; i < m; i++)
        {
            matrix[i] = new double[m];
            for (var j = 0; j < m; j++) matrix[i][j] = i - j + 1 >= 0 ? 1 : 0;
        }
        for (var i = 0; i < m; i++)
        {
            matrix[i][0] -= Math.Pow(h, i + 1);
            matrix[m - 1][i] -= Math.Pow(h, m - i);
        }
        var twice = 2 * h - 1;
        if (twice > 0) matrix[m - 1][0] += Math.Pow(twice, m);

        for (var i = 0; i < m; i++)
        {
            for (var j = 0; j < m; j++)
            {
                var factorial = i - j + 1;
                if (factorial > 0) matrix[i][j] /= Math.Exp(SpecialFunctions.LogGamma(factorial + 1));
            }
        }

        var power = MatrixPower(matrix, n);
        var sum = power[k - 1][k - 1];
        for (var i = 1; i <= n; i++) sum *= i / (double)n;
        return Math.Clamp(sum, 0, 1);
    }

    public static double KolmogorovSf(double d, int n) => 1 - KolmogorovCdf(d, n);

    /// <summary>Aproximación asintótica de Kolmogorov (Smirnov).</summary>
    public static double KolmogorovAsymptoticSf(double d, int n)
    {
        if (d <= 0) return 1;
        var z = d * Math.Sqrt(n);
        var sum = 0.0;
        for (var j = 1; j <= 100; j++)
        {
            var term = Math.Pow(-1, j - 1) * Math.Exp(-2 * j * j * z * z);
            sum += term;
            if (Math.Abs(term) < 1e-17) break;
        }
        return Math.Clamp(2 * sum, 0, 1);
    }

    /// <summary>
    /// P(D ≥ d) exacta para dos muestras por conteo de caminos en la retícula (Hodges).
    /// Se usa cuando <paramref name="n1"/>·<paramref name="n2"/> no es enorme.
    /// </summary>
    public static double KsTwoSampleSf(double d, int n1, int n2)
    {
        if (d <= 0) return 1;
        if (d >= 1) return 0;
        if ((long)n1 * n2 > 2_000_000)
        {
            var effective = (int)Math.Round((double)n1 * n2 / (n1 + n2));
            return KolmogorovSf(d, effective);
        }

        var limit = d * n1 * n2;
        var previous = new double[n2 + 1];
        var current = new double[n2 + 1];
        for (var j = 0; j <= n2; j++)
        {
            var inside = Math.Abs(j * n1) < limit - 1e-9;
            previous[j] = inside ? 1 : 0;
        }
        for (var i = 1; i <= n1; i++)
        {
            Array.Clear(current);
            for (var j = 0; j <= n2; j++)
            {
                if (Math.Abs(i * n2 - j * n1) >= limit - 1e-9) continue;
                var value = previous[j];
                if (j > 0) value += current[j - 1];
                current[j] = value;
            }
            (previous, current) = (current, previous);
        }

        var total = Math.Exp(NumericMath.LogCombination(n1 + n2, n1));
        if (!double.IsFinite(total) || total <= 0) return KolmogorovSf(d, (int)Math.Round(Math.Sqrt((double)n1 * n2)));
        var insideCount = previous[n2];
        return Math.Clamp(1 - insideCount / total, 0, 1);
    }

    /// <summary>Valores críticos de Anderson-Darling (tablas de scipy).</summary>
    private static readonly double[] AndersonNorm = [0.561, 0.631, 0.752, 0.873, 1.035];

    private static readonly double[] AndersonExpon = [0.916, 1.062, 1.321, 1.591, 1.959];

    private static readonly double[] AndersonLogistic = [0.426, 0.563, 0.66, 0.769, 0.906, 1.01];

    private static readonly double[] AndersonGumbel = [0.474, 0.637, 0.757, 0.877, 1.038];

    /// <summary>
    /// Prueba de Anderson-Darling con p-valor interpolado de tablas (equivalente a
    /// <c>scipy.stats.anderson(method="interpolate")</c>). Distribuciones: norm, expon, logistic, gumbel_r.
    /// </summary>
    public static (double Statistic, double PValue) AndersonDarling(double[] values, string distribution)
    {
        var n = values.Length;
        var ordered = NumericMath.Sorted(values);
        double[] critical;
        double[] significance;
        var logCdf = new double[n];
        var logSf = new double[n];
        switch (distribution)
        {
            case "expon":
            {
                var mean = NumericMath.Mean(values);
                for (var i = 0; i < n; i++)
                {
                    var w = ordered[i] / mean;
                    logCdf[i] = Math.Log(1 - Math.Exp(-w));
                    logSf[i] = -w;
                }
                critical = Scale(AndersonExpon, 1 + 0.6 / n);
                significance = [0.15, 0.10, 0.05, 0.025, 0.01];
                break;
            }
            case "logistic":
            {
                var location = NumericMath.Mean(values);
                var scale = NumericMath.Sd(values) * Math.Sqrt(3) / Math.PI;
                for (var i = 0; i < n; i++)
                {
                    var z = (ordered[i] - location) / scale;
                    logCdf[i] = -Math.Log(1 + Math.Exp(-z));
                    logSf[i] = -Math.Log(1 + Math.Exp(z));
                }
                critical = Scale(AndersonLogistic, 1 + 0.25 / n);
                significance = [0.25, 0.10, 0.05, 0.025, 0.01, 0.005];
                break;
            }
            case "gumbel_r":
            {
                var beta = NumericMath.Sd(values) * Math.Sqrt(6) / Math.PI;
                var location = NumericMath.Mean(values) - 0.5772156649015329 * beta;
                for (var i = 0; i < n; i++)
                {
                    var z = (ordered[i] - location) / beta;
                    logCdf[i] = -Math.Exp(-z);
                    logSf[i] = Math.Log(1 - Math.Exp(-Math.Exp(-z)));
                }
                critical = Scale(AndersonGumbel, 1 + 0.2 / Math.Sqrt(n));
                significance = [0.25, 0.10, 0.05, 0.025, 0.01];
                break;
            }
            default:
            {
                var mean = NumericMath.Mean(values);
                var sd = NumericMath.Sd(values);
                for (var i = 0; i < n; i++)
                {
                    var z = (ordered[i] - mean) / sd;
                    logCdf[i] = LogNormalCdf(z);
                    logSf[i] = LogNormalSf(z);
                }
                critical = Scale(AndersonNorm, 1 + 0.75 / n + 2.25 / (n * n));
                significance = [0.15, 0.10, 0.05, 0.025, 0.01];
                break;
            }
        }

        var statistic = -(double)n;
        for (var i = 0; i < n; i++)
        {
            statistic -= (2.0 * (i + 1) - 1) / n * (logCdf[i] + logSf[n - 1 - i]);
        }
        var pValue = Interpolate(statistic, critical, significance);
        return (statistic, pValue);
    }

    /// <summary>
    /// Prueba de Lilliefors (Kolmogorov-Smirnov con parámetros estimados) con la tabla de statsmodels.
    /// </summary>
    public static (double Statistic, double PValue) Lilliefors(double[] values, bool exponential)
    {
        var n = values.Length;
        var ordered = NumericMath.Sorted(values);
        double statistic;
        if (exponential)
        {
            var mean = NumericMath.Mean(values);
            statistic = 0;
            for (var i = 0; i < n; i++)
            {
                var cdf = 1 - Math.Exp(-ordered[i] / mean);
                statistic = Math.Max(statistic, Math.Abs((i + 1.0) / n - cdf));
                statistic = Math.Max(statistic, Math.Abs(cdf - i / (double)n));
            }
        }
        else
        {
            var mean = NumericMath.Mean(values);
            var sd = NumericMath.Sd(values);
            statistic = 0;
            for (var i = 0; i < n; i++)
            {
                var cdf = SpecialFunctions.NormalCdf((ordered[i] - mean) / sd);
                statistic = Math.Max(statistic, Math.Abs((i + 1.0) / n - cdf));
                statistic = Math.Max(statistic, Math.Abs(cdf - i / (double)n));
            }
        }

        var sizes = exponential ? LillieforsTable.ExponentialSizes : LillieforsTable.NormalSizes;
        var alphas = exponential ? LillieforsTable.ExponentialAlphas : LillieforsTable.NormalAlphas;
        var criticalTable = exponential ? LillieforsTable.ExponentialCritical : LillieforsTable.NormalCritical;

        var criticalValues = new double[alphas.Length];
        for (var column = 0; column < alphas.Length; column++)
        {
            var row = new double[sizes.Length];
            for (var index = 0; index < sizes.Length; index++) row[index] = criticalTable[index][column];
            criticalValues[column] = Interpolate(n, sizes, row);
        }

        // La tabla es decreciente en alpha: se invierte para interpolar el p-valor.
        var probabilities = (double[])alphas.Clone();
        var reversedCritical = new double[criticalValues.Length];
        var reversedProbabilities = new double[probabilities.Length];
        for (var index = 0; index < criticalValues.Length; index++)
        {
            reversedCritical[index] = criticalValues[criticalValues.Length - 1 - index];
            reversedProbabilities[index] = probabilities[probabilities.Length - 1 - index];
        }
        var pValue = Interpolate(statistic, reversedCritical, reversedProbabilities);
        return (statistic, pValue);
    }

    private static double LogNormalCdf(double z) => Math.Log(SpecialFunctions.NormalCdf(z));

    private static double LogNormalSf(double z) => Math.Log(1 - SpecialFunctions.NormalCdf(z));

    private static double[] Scale(double[] values, double divisor)
    {
        var output = new double[values.Length];
        for (var index = 0; index < values.Length; index++) output[index] = Math.Round(values[index] / divisor, 3, MidpointRounding.ToEven);
        return output;
    }

    /// <summary>Interpolación lineal con recorte a los extremos (<c>np.interp</c>).</summary>
    private static double Interpolate(double x, IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        if (xs.Count == 0) return double.NaN;
        if (x <= xs[0]) return ys[0];
        if (x >= xs[^1]) return ys[^1];
        for (var index = 1; index < xs.Count; index++)
        {
            if (x > xs[index]) continue;
            var fraction = (x - xs[index - 1]) / (xs[index] - xs[index - 1]);
            return ys[index - 1] + fraction * (ys[index] - ys[index - 1]);
        }
        return ys[^1];
    }

    private static double[][] MatrixPower(double[][] matrix, int exponent)
    {
        var size = matrix.Length;
        var result = LinearAlgebra.Identity(size);
        var power = matrix;
        var remaining = exponent;
        while (remaining > 0)
        {
            if ((remaining & 1) == 1) result = LinearAlgebra.Multiply(result, power);
            remaining >>= 1;
            if (remaining > 0) power = LinearAlgebra.Multiply(power, power);
        }
        return result;
    }
}
