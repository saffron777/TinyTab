using System.Globalization;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Piezas compartidas por los análisis portados desde Sigma: roles, tablas, decisiones y estadística.</summary>
internal static class ExtendedHelpers
{
    public static AnalysisParamDefinition Alpha(string label = "Nivel de significancia α") => new(
        "alpha",
        label,
        AnalysisParamKinds.Number,
        0.05,
        [],
        false,
        Minimum: 0.001,
        Maximum: 0.5);

    public static AnalysisParamDefinition Alternative() => new(
        "alternative",
        "Hipótesis alternativa",
        AnalysisParamKinds.Select,
        "two-sided",
        [
            new AnalysisParamOption("two-sided", "Distinta (≠)"),
            new AnalysisParamOption("greater", "Mayor (>)"),
            new AnalysisParamOption("less", "Menor (<)")
        ],
        false);

    public static Result<AnalysisResult> Error(DomainErrorCode code, string message) =>
        Result.Err<AnalysisResult>(code, message);

    public static Result<AnalysisResult> Invalid(string message) =>
        Result.Err<AnalysisResult>(DomainErrorCode.InvalidInput, message);

    public static Frame FrameOf(AnalysisInput input) => input.AsFrame();

    public static string FirstRole(AnalysisInput input, string role, string label)
    {
        var names = input.RoleNames(role);
        return names.Count > 0 ? names[0] : string.Empty;
    }

    public static double[] Complete(double[] values) => values.Where(value => !double.IsNaN(value)).ToArray();

    public static string N(double value, int digits = 4) => FormatHelpers.N(value, digits);

    public static string P(double value) => SigmaText.PValue(value);

    public static string G(double value) => SigmaText.General(value);

    public static double NormalTwoSided(double z) => 2 * (1 - SpecialFunctions.NormalCdf(Math.Abs(z)));

    public static double NormalOneSided(double z) => 1 - SpecialFunctions.NormalCdf(z);

    /// <summary>P-valor "minlike" de una binomial (dos colas, como <c>statsmodels binom_test</c>).</summary>
    public static double BinomialMinlike(int observed, int trials, double probability)
    {
        if (trials <= 0) return 1;
        var observedPmf = ExtendedStatistics.BinomialPmf(observed, trials, probability);
        var total = 0.0;
        for (var k = 0; k <= trials; k++)
        {
            var pmf = ExtendedStatistics.BinomialPmf(k, trials, probability);
            if (pmf <= observedPmf * (1 + 1e-7)) total += pmf;
        }
        return Math.Clamp(total, 0, 1);
    }

    public static double BinomialOneSided(int observed, int trials, double probability, string alternative) =>
        alternative switch
        {
            "greater" => 1 - ExtendedStatistics.BinomialCdf(observed - 1, trials, probability),
            "less" => ExtendedStatistics.BinomialCdf(observed, trials, probability),
            _ => BinomialMinlike(observed, trials, probability)
        };

    public static double HypergeometricPmf(int a, int row1, int col1, int total)
    {
        var log = NumericMath.LogCombination(col1, a)
                  + NumericMath.LogCombination(total - col1, row1 - a)
                  - NumericMath.LogCombination(total, row1);
        return Math.Exp(log);
    }

    public static double FisherExact(int a, int b, int c, int d)
    {
        var total = a + b + c + d;
        var row1 = a + b;
        var col1 = a + c;
        var observed = HypergeometricPmf(a, row1, col1, total);
        var sum = 0.0;
        var low = Math.Max(0, row1 - (total - col1));
        var high = Math.Min(row1, col1);
        for (var value = low; value <= high; value++)
        {
            var pmf = HypergeometricPmf(value, row1, col1, total);
            if (pmf <= observed * (1 + 1e-7)) sum += pmf;
        }
        return Math.Clamp(sum, 0, 1);
    }

    public static double ContingencyChi2(double[][] table, out double pValue)
    {
        var rows = table.Length;
        var cols = table[0].Length;
        var rowTotals = new double[rows];
        var colTotals = new double[cols];
        var total = 0.0;
        for (var i = 0; i < rows; i++)
        {
            for (var j = 0; j < cols; j++)
            {
                rowTotals[i] += table[i][j];
                colTotals[j] += table[i][j];
                total += table[i][j];
            }
        }
        var statistic = 0.0;
        for (var i = 0; i < rows; i++)
        {
            for (var j = 0; j < cols; j++)
            {
                var expected = rowTotals[i] * colTotals[j] / total;
                if (expected <= 0) continue;
                var delta = table[i][j] - expected;
                statistic += delta * delta / expected;
            }
        }
        pValue = ExtendedStatistics.ChiSquareSf(statistic, (rows - 1.0) * (cols - 1));
        return statistic;
    }

    public static List<(string Label, double[] Values)> Groups(Frame frame, string valueColumn, string groupColumn) =>
        [.. Grouping.NumericByGroup(frame, valueColumn, groupColumn)];

    /// <summary>Levene sobre desviaciones a la media o a la mediana (F de un factor).</summary>
    public static (double Statistic, double Df1, double Df2, double PValue) Levene(
        IReadOnlyList<double[]> groups,
        bool centerOnMean)
    {
        var transformed = new double[groups.Count][];
        for (var index = 0; index < groups.Count; index++)
        {
            var values = groups[index];
            var center = centerOnMean ? NumericMath.Mean(values) : NumericMath.Median(values);
            transformed[index] = values.Select(value => Math.Abs(value - center)).ToArray();
        }

        var k = transformed.Length;
        var n = transformed.Sum(values => values.Length);
        var grand = transformed.SelectMany(values => values).Average();
        var ssBetween = 0.0;
        var ssWithin = 0.0;
        foreach (var values in transformed)
        {
            var mean = values.Average();
            ssBetween += values.Length * Math.Pow(mean - grand, 2);
            foreach (var value in values) ssWithin += Math.Pow(value - mean, 2);
        }
        var df1 = k - 1.0;
        var df2 = n - k;
        var f = (ssBetween / df1) / (ssWithin / df2);
        return (f, df1, df2, ExtendedStatistics.FSf(f, df1, df2));
    }

    /// <summary>Fligner-Killeen (chi-cuadrado sobre rangos normalizados de las desviaciones a la mediana).</summary>
    public static (double Statistic, double Df, double PValue) Fligner(IReadOnlyList<double[]> groups)
    {
        var k = groups.Count;
        var deviations = new List<double>();
        var groupOf = new List<int>();
        var groupMedians = new double[k];
        for (var index = 0; index < k; index++)
        {
            groupMedians[index] = NumericMath.Median(groups[index]);
            foreach (var value in groups[index])
            {
                deviations.Add(Math.Abs(value - groupMedians[index]));
                groupOf.Add(index);
            }
        }
        var n = deviations.Count;
        var ranks = NumericMath.Ranks(deviations);
        var scores = new double[n];
        for (var index = 0; index < n; index++)
        {
            scores[index] = SpecialFunctions.NormalInv(ranks[index] / (2 * (n + 1.0)) + 0.5);
        }
        var grand = scores.Average();
        var variance = NumericMath.Variance(scores);
        double statistic = 0;
        for (var group = 0; group < k; group++)
        {
            var values = new List<double>();
            for (var index = 0; index < n; index++)
            {
                if (groupOf[index] == group) values.Add(scores[index]);
            }
            var mean = values.Average();
            statistic += values.Count * Math.Pow(mean - grand, 2);
        }
        statistic /= variance;
        return (statistic, k - 1.0, ExtendedStatistics.ChiSquareSf(statistic, k - 1.0));
    }

    public static string Join(IEnumerable<string> values, string separator = ", ") => string.Join(separator, values);

    public static string FormatList(IReadOnlyList<double> values) =>
        Join(values.Select(value => value.ToString("0.########", CultureInfo.InvariantCulture)));
}
