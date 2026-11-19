using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Cópula Gaussiana (parámetro por tau de Kendall) y dependencia de colas.</summary>
internal static class CopulaAnalyses
{
    public static readonly AnalysisDefinition Copula = new(
        "copula",
        "Cópula (dependencia entre variables)",
        "Dependencia y cópulas",
        "Ajusta una cópula (Gaussiana, Clayton, Gumbel o Frank) a dos variables y reporta la τ de Kendall y la dependencia de colas.",
        [],
        [],
        [
            new(
                "family",
                "Familia de cópula",
                AnalysisParamKinds.Select,
                "gaussian",
                [
                    new("gaussian", "Gaussiana (elíptica, sin dependencia de colas)"),
                    new("clayton", "Clayton (dependencia en la cola inferior)"),
                    new("gumbel", "Gumbel (dependencia en la cola superior)"),
                    new("frank", "Frank (simétrica, sin dependencia de colas)")
                ],
                false),
            new("simulate", "Puntos a simular (0 = no)", AnalysisParamKinds.Integer, 0.0, [], false, Minimum: 0, Maximum: 5000)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Dependencia y cópulas",
        Roles:
        [
            new ColumnRole("x", "Primera variable", ColumnKinds.Numeric),
            new ColumnRole("y", "Segunda variable", ColumnKinds.Numeric)
        ],
        Outputs: [new AnalysisOutputSpec("copula", "Muestra simulada", Required: false)]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        if (input.Pstr("family", "gaussian") != "gaussian")
        {
            return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "Por ahora solo está disponible la cópula Gaussiana; Clayton, Gumbel y Frank quedan pendientes.");
        }
        if (input.Pint("simulate", 0) > 0)
        {
            return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "La simulación de la cópula aún no está disponible; use simulate = 0.");
        }
        var frame = input.AsFrame();
        var xName = ExtendedHelpers.FirstRole(input, "x", "Primera variable");
        var yName = ExtendedHelpers.FirstRole(input, "y", "Segunda variable");
        var complete = frame.CompleteCases([xName, yName]);
        var x = complete.Numeric(xName);
        var y = complete.Numeric(yName);
        var n = x.Length;
        if (n < 20) return ExtendedHelpers.Invalid("Se necesitan al menos 20 observaciones pareadas.");
        if (x.Select(value => value - x.Average()).Sum(value => value * value) == 0
            || y.Select(value => value - y.Average()).Sum(value => value * value) == 0)
        {
            return ExtendedHelpers.Invalid("Una de las variables es constante.");
        }
        var pearson = Correlation(x, y);
        var tau = KendallTau(x, y);
        var parameter = Math.Sin(Math.PI * tau / 2);
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Correlación de Pearson", ModelHelpers.N(pearson)),
                new TileItem("τ de Kendall", ModelHelpers.N(tau)),
                new TileItem("Parámetro", ModelHelpers.N(parameter)),
                new TileItem("Dependencia cola inferior", ModelHelpers.N(0)),
                new TileItem("Dependencia cola superior", ModelHelpers.N(0))
            ]),
            new AlertSection(
                "Aviso",
                "info",
                "La cópula separa la estructura de dependencia de las distribuciones marginales: τ y la dependencia de colas describen la asociación sin suponer normalidad.")
        };
        var points = Enumerable.Range(0, n).Select(index => new ScatterPoint(x[index], y[index])).ToList();
        return Result.Ok(new AnalysisResult(
            "copula",
            "Cópula Gaussiana",
            sections,
            Subtitle: $"n = {n} pares",
            Charts:
            [
                new ScatterChart("Datos y muestra simulada", points, xName, yName)
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["family"] = "gaussian",
                ["parameter"] = parameter,
                ["tau"] = tau,
                ["pearson"] = pearson,
                ["tail_lower"] = 0.0,
                ["tail_upper"] = 0.0
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

    private static double KendallTau(double[] x, double[] y)
    {
        var concordant = 0.0;
        var discordant = 0.0;
        for (var first = 0; first < x.Length - 1; first++)
        {
            for (var second = first + 1; second < x.Length; second++)
            {
                var product = Math.Sign(x[second] - x[first]) * Math.Sign(y[second] - y[first]);
                if (product > 0) concordant++;
                else if (product < 0) discordant++;
            }
        }
        return (concordant - discordant) / (0.5 * x.Length * (x.Length - 1));
    }
}
