using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Generación de datos aleatorios (análisis no determinista).</summary>
internal static class RandomDataAnalyses
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

    public static readonly AnalysisDefinition RandomData = new(
        "random-data",
        "Generar datos aleatorios",
        "Simulación",
        "Crea un dataset con columnas aleatorias de una distribución (para ensayar métodos o simular escenarios).",
        [],
        [],
        [
            new("distribution", "Distribución", AnalysisParamKinds.Select, "norm", Distributions.Select(item => new AnalysisParamOption(item.Key, item.Label)).ToArray(), false),
            new("a", "Parámetro a", AnalysisParamKinds.Number, 0.0, [], false),
            new("b", "Parámetro b (si aplica)", AnalysisParamKinds.Number, 1.0, [], true),
            new("n", "Filas", AnalysisParamKinds.Integer, 100.0, [], false, Minimum: 1, Maximum: 1000000),
            new("columns", "Columnas", AnalysisParamKinds.Integer, 1.0, [], false, Minimum: 1, Maximum: 100),
            new("prefix", "Prefijo del nombre de las columnas", AnalysisParamKinds.Text, "x", [], false),
            new("seed", "Semilla (para repetir el resultado)", AnalysisParamKinds.Integer, 12345.0, [], false, Minimum: 0, Maximum: 2147483647)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Simulación",
        Roles: [],
        Outputs: [new AnalysisOutputSpec("resultado", "Datos aleatorios", Required: true)]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var distribution = input.Pstr("distribution", "norm");
        var a = input.Pnum("a", 0);
        var b = input.Pnum("b", 1);
        var n = input.Pint("n", 100);
        var columns = input.Pint("columns", 1);
        var prefix = input.Pstr("prefix", "x");
        if (prefix.Length == 0) prefix = "x";
        var random = new System.Random(input.Pint("seed", 12345));
        var generator = Generator(distribution, a, b, random);
        var data = Enumerable.Range(0, columns)
            .Select(column => DataColumn.Numeric($"{prefix}{column + 1}", Enumerable.Range(0, n).Select(_ => generator()).ToArray()))
            .ToArray();
        var tableRows = Enumerable.Range(0, Math.Min(n, 10))
            .Select(row => (IReadOnlyList<string>)data.Select(column => ModelHelpers.N(column.NumberAt(row))).ToArray())
            .ToArray();
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Filas", JsNumber.ToString(n)),
                new TileItem("Columnas", JsNumber.ToString(columns)),
                new TileItem("Distribución", Distributions.First(item => item.Key == distribution).Label.Split(" (")[0]),
                new TileItem("Semilla", JsNumber.ToString(input.Pint("seed", 12345)))
            ]),
            new TableSection("Primeras filas", data.Select(column => column.Name).ToArray(), tableRows)
        };
        var chartRows = data.Select(column => new LineSeries(column.Name, column.Numbers)).ToList();
        return Result.Ok(new AnalysisResult(
            "random-data",
            "Datos aleatorios",
            sections,
            Subtitle: $"{Distributions.First(item => item.Key == distribution).Label.Split(" (")[0]} · {n} filas × {columns} columnas · semilla {input.Pint("seed", 12345)}",
            Charts:
            [
                new LineChart("Datos generados", chartRows, "Fila")
            ],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
            {
                ["resultado"] = new DatasetResult("Datos aleatorios", data)
            },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["columns"] = columns
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

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
