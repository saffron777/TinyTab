using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Gráfico de probabilidad (Q-Q) con ajuste normal.</summary>
internal static class ProbabilityAnalyses
{
    public static readonly AnalysisDefinition Probability = new(
        "probability",
        "Gráfico de probabilidad (Q-Q)",
        "Distribución",
        "Compara los datos con los cuantiles de una distribución ajustada (normal, lognormal, Weibull…) y mide la bondad de ajuste.",
        [],
        [],
        [
            new(
                "distribution",
                "Distribución",
                AnalysisParamKinds.Select,
                "norm",
                [
                    new("norm", "Normal"), new("lognorm", "Lognormal"), new("expon", "Exponencial"),
                    new("weibull_min", "Weibull"), new("gamma", "Gamma"), new("logistic", "Logística"),
                    new("gumbel_r", "Valor extremo (Gumbel)"), new("laplace", "Laplace"), new("uniform", "Uniforme")
                ],
                false)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Distribución",
        Roles:
        [
            new ColumnRole("var", "Variable", ColumnKinds.Numeric)
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        if (input.Pstr("distribution", "norm") != "norm")
        {
            return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "Por ahora el gráfico de probabilidad solo ajusta la distribución normal.");
        }
        var frame = input.AsFrame();
        var variable = ExtendedHelpers.FirstRole(input, "var", "Variable");
        var values = frame.Numeric(variable).Where(value => !double.IsNaN(value)).OrderBy(value => value).ToArray();
        var n = values.Length;
        if (n < 8) return ExtendedHelpers.Invalid("Se necesitan al menos 8 valores.");
        var mean = values.Average();
        var sd = Math.Sqrt(values.Sum(value => Math.Pow(value - mean, 2)) / n);
        var correlation = 0.0;
        var theoretical = new double[n];
        var standardized = new double[n];
        for (var index = 0; index < n; index++)
        {
            theoretical[index] = SpecialFunctions.NormalInv((index + 1 - 0.3) / (n + 0.4));
            standardized[index] = (values[index] - mean) / sd;
        }
        correlation = Correlation(theoretical, standardized);
        var statistic = 0.0;
        for (var index = 0; index < n; index++)
        {
            var cdf = SpecialFunctions.NormalCdf((values[index] - mean) / sd);
            statistic = Math.Max(statistic, Math.Max((index + 1.0) / n - cdf, cdf - index / (double)n));
        }
        var ksP = ExtendedStatistics.KolmogorovSf(statistic, n);
        var (adStatistic, adP) = ExtendedStatistics.AndersonDarling(values, "norm");
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("Correlación del gráfico", ModelHelpers.N(correlation)),
                new TileItem("A² de Anderson-Darling", ModelHelpers.N(adStatistic))
            ]),
            new TableSection(
                "Parámetros estimados",
                ["Parámetro", "Valor"],
                [
                    new[] { "media", ModelHelpers.N(mean) },
                    new[] { "desv. estándar", ModelHelpers.N(sd) }
                ]),
            new TableSection(
                "Bondad de ajuste",
                ["Medida", "Valor", "p"],
                [
                    new[] { "Kolmogorov-Smirnov (parámetros estimados)", ModelHelpers.N(statistic), ModelHelpers.P(ksP) },
                    new[] { "Correlación del gráfico", ModelHelpers.N(correlation), string.Empty },
                    new[] { "Anderson-Darling A²", ModelHelpers.N(adStatistic), ModelHelpers.N(adP) }
                ],
                Note: "El p de Kolmogorov-Smirnov es optimista cuando los parámetros se estiman con los mismos datos; el de Anderson-Darling se interpola de tablas (entre 0.01 y 0.15) y sí tiene en cuenta que se estimaron."),
            new AlertSection(
                "Conclusión",
                correlation > 0.98 ? "success" : "warning",
                correlation > 0.98
                    ? "Los puntos siguen de cerca la recta: la distribución describe bien los datos."
                    : "Los puntos se separan de la recta: la distribución no describe bien los datos.")
        };
        var points = Enumerable.Range(0, n).Select(index => new ScatterPoint(theoretical[index], standardized[index])).ToList();
        return Result.Ok(new AnalysisResult(
            "probability",
            $"Gráfico de probabilidad de {variable}",
            sections,
            Subtitle: "distribución Normal ajustada por máxima verosimilitud",
            Charts: [new ScatterChart("Gráfico de probabilidad normal", points, "Cuantil teórico", "Valor estandarizado")],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["correlation"] = correlation,
                ["parameters"] = new[] { mean, sd },
                ["ks_p"] = ksP
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double Correlation(double[] x, double[] y)
    {
        var meanX = x.Average();
        var meanY = y.Average();
        var numerator = x.Select((value, index) => (value - meanX) * (y[index] - meanY)).Sum();
        var denominator = Math.Sqrt(x.Sum(value => Math.Pow(value - meanX, 2)) * y.Sum(value => Math.Pow(value - meanY, 2)));
        return denominator > 0 ? numerator / denominator : double.NaN;
    }
}
