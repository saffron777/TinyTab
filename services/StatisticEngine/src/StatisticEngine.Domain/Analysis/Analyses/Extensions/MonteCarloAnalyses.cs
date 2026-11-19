using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Simulación Monte Carlo de una distribución muestral (no determinista).</summary>
internal static class MonteCarloAnalyses
{
    private static readonly (string Key, string Label)[] Distributions =
    [
        ("norm", "Normal (a = media, b = desviación estándar)"),
        ("uniform", "Uniforme (a = mínimo, b = máximo)"),
        ("expon", "Exponencial (a = media)"),
        ("lognorm", "Lognormal (a = μ del log, b = σ del log)"),
        ("t", "t de Student (a = grados de libertad)"),
        ("chi2", "Chi cuadrado (a = grados de libertad)"),
        ("gamma", "Gamma (a = forma, b = escala)"),
        ("poisson", "Poisson (a = media)"),
        ("binom", "Binomial (a = ensayos, b = probabilidad)"),
        ("bernoulli", "Bernoulli (a = probabilidad de éxito)"),
        ("randint", "Entero uniforme (a = mínimo, b = máximo, incluidos)")
    ];

    private static readonly (string Key, string Label)[] Statistics =
    [
        ("mean", "Media"), ("median", "Mediana"), ("std", "Desviación estándar"),
        ("variance", "Varianza"), ("min", "Mínimo"), ("max", "Máximo"), ("range", "Rango")
    ];

    private static readonly double[] QuantileLevels = [0.05, 0.25, 0.5, 0.75, 0.95];

    private static readonly int[] QuantilePercents = [5, 25, 50, 75, 95];

    public static readonly AnalysisDefinition MonteCarlo = new(
        "monte-carlo",
        "Simulación Monte Carlo de una distribución muestral",
        "Simulación",
        "Simula miles de muestras de una distribución y muestra cómo se comporta un estadístico (teorema central del límite, sesgo, dispersión). No necesita dataset.",
        [],
        [],
        [
            new("distribution", "Distribución de la población", AnalysisParamKinds.Select, "expon", Distributions.Select(item => new AnalysisParamOption(item.Key, item.Label)).ToArray(), false),
            new("a", "Parámetro a", AnalysisParamKinds.Number, 1.0, [], false),
            new("b", "Parámetro b (si aplica)", AnalysisParamKinds.Number, 1.0, [], true),
            new("n", "Tamaño de cada muestra", AnalysisParamKinds.Integer, 30.0, [], false, Minimum: 2, Maximum: 100000),
            new("statistic", "Estadístico", AnalysisParamKinds.Select, "mean", Statistics.Select(item => new AnalysisParamOption(item.Key, item.Label)).ToArray(), false),
            new("replications", "Réplicas", AnalysisParamKinds.Integer, 10000.0, [], false, Minimum: 100, Maximum: 500000),
            new("seed", "Semilla (para repetir el resultado)", AnalysisParamKinds.Integer, 12345.0, [], false, Minimum: 0, Maximum: 2147483647)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Simulación");

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var distribution = input.Pstr("distribution", "expon");
        var a = input.Pnum("a", 1);
        var b = input.Pnum("b", 1);
        var n = input.Pint("n", 30);
        var statistic = input.Pstr("statistic", "mean");
        var replications = input.Pint("replications", 10000);
        var random = new System.Random(input.Pint("seed", 12345));
        var draws = new double[replications];
        var sample = new double[n];
        Func<double> generator = Generator(distribution, a, b, random);
        for (var replication = 0; replication < replications; replication++)
        {
            for (var index = 0; index < n; index++) sample[index] = generator();
            draws[replication] = Statistic(statistic, sample);
        }
        var mean = draws.Average();
        var standardError = NumericMath.Sd(draws);
        var quantiles = new double[5];
        foreach (var (q, index) in QuantileLevels.Select((q, index) => (q, index))) quantiles[index] = NumericMath.Quantile(draws, q);
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Media de las réplicas", ModelHelpers.N(mean)),
                new TileItem("Error estándar", ModelHelpers.N(standardError)),
                new TileItem("Réplicas", JsNumber.ToString(replications)),
                new TileItem("Tamaño de muestra", JsNumber.ToString(n))
            ]),
            new TableSection(
                "Distribución muestral simulada",
                ["Estadístico", "Media", "Desv. est.", "Mínimo", "Máximo"],
                [[Statistics.First(item => item.Key == statistic).Label, ModelHelpers.N(mean), ModelHelpers.N(standardError), ModelHelpers.N(draws.Min()), ModelHelpers.N(draws.Max())]]),
            new TableSection(
                "Contraste con la teoría",
                ["Cuantil", "Valor"],
                QuantilePercents.Select((q, index) => (IReadOnlyList<string>)new[]                {
                    $"P{q}",
                    ModelHelpers.N(quantiles[index])
                }).ToArray())
        };
        var points = draws.OrderBy(value => value).Select((value, index) => new ScatterPoint(index + 1, value)).ToList();
        return Result.Ok(new AnalysisResult(
            "monte-carlo",
            $"Simulación Monte Carlo: {Statistics.First(item => item.Key == statistic).Label.ToLowerInvariant()} muestral",
            sections,
            Subtitle: $"{Distributions.First(item => item.Key == distribution).Label.Split(" (")[0]} · n = {n} · {replications.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} réplicas · semilla {input.Pint("seed", 12345)}",
            Charts:
            [
                new ScatterChart("Distribución muestral simulada", points, "Réplica ordenada", Statistics.First(item => item.Key == statistic).Label)
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["mean"] = mean,
                ["se"] = standardError,
                ["quantiles"] = quantiles
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double Statistic(string kind, double[] sample) => kind switch
    {
        "median" => NumericMath.Quantile(sample, 0.5),
        "std" => NumericMath.Sd(sample),
        "variance" => Math.Pow(NumericMath.Sd(sample), 2),
        "min" => sample.Min(),
        "max" => sample.Max(),
        "range" => sample.Max() - sample.Min(),
        _ => sample.Average()
    };

    private static Func<double> Generator(string distribution, double a, double b, System.Random random)
    {
        double Uniform() => random.NextDouble();
        double Normal()
        {
            var u1 = Math.Max(Uniform(), 1e-12);
            var u2 = Uniform();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
        double Gamma(double shape)
        {
            if (shape < 1) return Gamma(shape + 1) * Math.Pow(Uniform(), 1 / shape);
            var d = shape - 1.0 / 3;
            var c = 1 / Math.Sqrt(9 * d);
            while (true)
            {
                var x = Normal();
                var v = Math.Pow(1 + c * x, 3);
                if (v <= 0) continue;
                var u = Uniform();
                if (u < 1 - 0.0331 * Math.Pow(x, 4) || Math.Log(u) < 0.5 * x * x + d - d * v + d * Math.Log(v)) return d * v;
            }
        }
        return distribution switch
        {
            "norm" => () => a + b * Normal(),
            "uniform" => () => a + (b - a) * Uniform(),
            "expon" => () => -a * Math.Log(Math.Max(Uniform(), 1e-12)),
            "lognorm" => () => Math.Exp(a + b * Normal()),
            "t" => () => Normal() / Math.Sqrt(Gamma(a / 2) * 2 / a),
            "chi2" => () => Gamma(a / 2) * 2,
            "gamma" => () => Gamma(a) * b,
            "poisson" => () =>
            {
                var limit = Math.Exp(-a);
                var product = 1.0;
                var count = 0;
                do { count++; product *= Uniform(); } while (product > limit && count < 10000);
                return count - 1;
            },
            "binom" => () =>
            {
                var successes = 0;
                for (var trial = 0; trial < (int)a; trial++) if (Uniform() < b) successes++;
                return successes;
            },
            "bernoulli" => () => Uniform() < a ? 1 : 0,
            _ => () => random.Next((int)a, (int)b + 1)
        };
    }
}
