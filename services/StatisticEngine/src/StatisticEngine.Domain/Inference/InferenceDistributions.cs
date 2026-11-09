using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Inference;

/// <summary>Hipótesis alternativa de las pruebas de una y dos colas.</summary>
public enum Alternative
{
    TwoSided,
    Less,
    Greater
}

public static class Alternatives
{
    public static Alternative Parse(string? text) =>
        text switch
        {
            "less" => Alternative.Less,
            "greater" => Alternative.Greater,
            _ => Alternative.TwoSided
        };

    public static string ToWire(Alternative alternative) =>
        alternative switch
        {
            Alternative.Less => "less",
            Alternative.Greater => "greater",
            _ => "two-sided"
        };
}

/// <summary>
/// Funciones de distribución compartidas por las pruebas de hipótesis (t y F).
/// Provienen de <c>tests.ts</c> y <c>anova.ts</c> del motor y se centralizan aquí para que
/// los módulos de inferencia, ANOVA, regresión y potencia no dependan entre sí.
/// </summary>
public static class InferenceDistributions
{
    public static double TCdf(double x, double df)
    {
        if (!(df > 0)) return double.NaN;
        var tail = 0.5 * SpecialFunctions.Betai(df / 2, 0.5, df / (df + x * x));
        return x >= 0 ? 1 - tail : tail;
    }

    public static double TQuantile(double p, double df)
    {
        if (p <= 0) return double.NegativeInfinity;
        if (p >= 1) return double.PositiveInfinity;
        var lo = -1e6;
        var hi = 1e6;
        for (var i = 0; i < 200; i++)
        {
            var mid = (lo + hi) / 2;
            if (TCdf(mid, df) < p) lo = mid;
            else hi = mid;
        }
        return (lo + hi) / 2;
    }

    /// <summary>P(F &gt; f): cola superior de la distribución F.</summary>
    public static double FCdfUpper(double f, double df1, double df2)
    {
        if (!(f >= 0)) return double.NaN;
        if (f == 0) return 1;
        return 1 - SpecialFunctions.Betai(df1 / 2, df2 / 2, df1 * f / (df1 * f + df2));
    }

    /// <summary>P(F ≤ f).</summary>
    public static double FCdf(double f, double df1, double df2) => 1 - FCdfUpper(f, df1, df2);

    /// <summary>Valor p a partir del estadístico y la CDF de la distribución de contraste.</summary>
    public static double PValueFromStatistic(double statistic, Func<double, double> cdf, Alternative alternative)
    {
        switch (alternative)
        {
            case Alternative.Less:
                return cdf(statistic);
            case Alternative.Greater:
                return 1 - cdf(statistic);
            default:
                return 2 * Math.Min(cdf(statistic), 1 - cdf(statistic));
        }
    }
}
