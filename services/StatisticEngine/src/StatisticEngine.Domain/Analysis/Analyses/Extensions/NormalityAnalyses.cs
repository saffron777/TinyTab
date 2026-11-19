using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Pruebas de normalidad y bondad de ajuste portadas de <c>sigma/domain/analysis/pruebas.py</c>.</summary>
internal static class NormalityAnalyses
{
    private const string Category = "Pruebas adicionales";

    private static readonly AnalysisParamDefinition Alpha = ExtendedHelpers.Alpha();

    private static readonly AnalysisDefinition Anderson = new(
        "anderson-darling",
        "Anderson-Darling (normalidad y otras)",
        Category,
        "Prueba de bondad de ajuste a la normal, exponencial, logística o Gumbel con un gráfico de probabilidad.",
        [],
        [],
        [
            new(
                "dist",
                "Distribución",
                AnalysisParamKinds.Select,
                "norm",
                [
                    new("norm", "Normal"),
                    new("expon", "Exponencial"),
                    new("logistic", "Logística"),
                    new("gumbel_r", "Gumbel")
                ],
                false),
            Alpha
        ],
        RunAnderson,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Normalidad",
        Roles: [new ColumnRole("var", "Variable", ColumnKinds.Numeric)]);

    private static readonly AnalysisDefinition Lilliefors = new(
        "lilliefors",
        "Lilliefors (Kolmogorov-Smirnov corregida)",
        Category,
        "Normalidad o exponencialidad con parámetros estimados de los propios datos.",
        [],
        [],
        [
            new(
                "dist",
                "Distribución",
                AnalysisParamKinds.Select,
                "norm",
                [new("norm", "Normal"), new("exp", "Exponencial")],
                false),
            Alpha
        ],
        RunLilliefors,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Normalidad",
        Roles: [new ColumnRole("var", "Variable", ColumnKinds.Numeric)]);

    private static readonly AnalysisDefinition KsOne = new(
        "ks-one-sample",
        "Kolmogorov-Smirnov de una muestra",
        Category,
        "Compara los datos con una distribución cuyos parámetros usted fija de antemano.",
        [],
        [],
        [
            new(
                "dist",
                "Distribución",
                AnalysisParamKinds.Select,
                "norm",
                [
                    new("norm", "Normal (ubicación = media, escala = desv. estándar)"),
                    new("uniform", "Uniforme (ubicación = mínimo, escala = amplitud)"),
                    new("expon", "Exponencial (ubicación = 0, escala = media)"),
                    new("lognorm", "Lognormal (σ = escala; ubicación = 0)")
                ],
                false),
            new("loc", "Ubicación", AnalysisParamKinds.Number, 0.0, [], false),
            new("scale", "Escala", AnalysisParamKinds.Number, 1.0, [], false, Minimum: 1e-9),
            ExtendedHelpers.Alternative(),
            Alpha
        ],
        RunKsOne,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Normalidad",
        Roles: [new ColumnRole("var", "Variable", ColumnKinds.Numeric)]);

    private static readonly AnalysisDefinition KsTwo = new(
        "ks-two-samples",
        "Kolmogorov-Smirnov de dos muestras",
        Category,
        "¿Provienen dos muestras de la misma distribución? Compara sus funciones de distribución empíricas.",
        [],
        [],
        [ExtendedHelpers.Alternative(), Alpha],
        RunKsTwo,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Normalidad",
        Roles:
        [
            new ColumnRole("var", "Variable", ColumnKinds.Numeric),
            new ColumnRole("group", "Grupos (2 categorías)", ColumnKinds.Categorical)
        ]);

    private static readonly AnalysisDefinition JarqueBera = new(
        "jarque-bera",
        "Jarque-Bera",
        Category,
        "Normalidad a partir de la asimetría y la curtosis (muestras grandes).",
        [],
        [],
        [Alpha],
        RunJarqueBera,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Normalidad",
        Roles: [new ColumnRole("var", "Variable", ColumnKinds.Numeric)]);

    private static Result<AnalysisResult> RunAnderson(AnalysisInput input)
    {
        var frame = ExtendedHelpers.FrameOf(input);
        var name = ExtendedHelpers.FirstRole(input, "var", "Variable");
        var values = ExtendedHelpers.Complete(frame.Numeric(name));
        if (values.Length < 8) return ExtendedHelpers.Invalid(SigmaText.NeedMessage(name, 8));

        var dist = input.Pstr("dist", "norm");
        var label = dist switch
        {
            "expon" => "Exponencial",
            "logistic" => "Logística",
            "gumbel_r" => "Gumbel",
            _ => "Normal"
        };
        var result = ExtendedStatistics.AndersonDarling(values, dist);
        var alpha = input.Pnum("alpha", 0.05);

        var ordered = NumericMath.Sorted(values);
        var points = new List<ScatterPoint>(ordered.Length);
        for (var index = 0; index < ordered.Length; index++)
        {
            var probability = (index + 1 - 0.3) / (ordered.Length + 0.4);
            points.Add(new ScatterPoint(SpecialFunctions.NormalInv(probability), ordered[index]));
        }

        return Result.Ok(new AnalysisResult(
            "anderson-darling",
            $"Prueba de Anderson-Darling de {name}",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("n", JsNumber.ToString(values.Length)),
                    new TileItem("A²", ExtendedHelpers.N(result.Statistic)),
                    new TileItem("Valor p", ExtendedHelpers.P(result.PValue))
                ]),
                SigmaText.Decision(result.PValue, alpha, $"los datos siguen una distribución {label.ToLowerInvariant()}"),
                new TableSection(
                    "Resultado",
                    ["Estadístico A²", "Valor p", "n"],
                    [[
                        ExtendedHelpers.N(result.Statistic),
                        ExtendedHelpers.P(result.PValue),
                        JsNumber.ToString(values.Length)
                    ]],
                    Note: "El valor p se interpola de tablas con los parámetros estimados; queda entre 0.01 y 0.15 (los extremos indican «menor» o «mayor» que eso).")
            ],
            Subtitle: $"distribución {label}",
            Charts:
            [
                new ScatterGroupsChart(
                    $"Gráfico de probabilidad ({label})",
                    [new ScatterGroup("Datos", points)],
                    "Cuantil teórico",
                    name)
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["statistic"] = result.Statistic,
                ["p"] = result.PValue,
                ["n"] = values.Length
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunLilliefors(AnalysisInput input)
    {
        var frame = ExtendedHelpers.FrameOf(input);
        var name = ExtendedHelpers.FirstRole(input, "var", "Variable");
        var values = ExtendedHelpers.Complete(frame.Numeric(name));
        if (values.Length < 5) return ExtendedHelpers.Invalid(SigmaText.NeedMessage(name, 5));

        var dist = input.Pstr("dist", "norm");
        if (dist == "exp" && values.Any(value => value < 0))
        {
            return ExtendedHelpers.Invalid("La distribución exponencial necesita valores no negativos.");
        }

        var exponential = dist == "exp";
        var result = ExtendedStatistics.Lilliefors(values, exponential);
        var label = exponential ? "exponencial" : "normal";
        var alpha = input.Pnum("alpha", 0.05);
        var ordered = NumericMath.Sorted(values);

        var empirical = new XYSeries(
            "Empírica",
            [.. ordered.Select((_, index) => (double)(index + 1))],
            [.. ordered.Select((_, index) => (index + 1.0) / ordered.Length)]);
        var grid = new double[200];
        var mean = NumericMath.Mean(values);
        var sd = NumericMath.Sd(values);
        for (var index = 0; index < grid.Length; index++)
        {
            grid[index] = ordered[0] + (ordered[^1] - ordered[0]) * index / (grid.Length - 1.0);
        }
        var fitted = new XYSeries(
            exponential ? "Ajustada (exponencial)" : "Ajustada (normal)",
            grid,
            [.. grid.Select(value => exponential
                ? 1 - Math.Exp(-value / mean)
                : SpecialFunctions.NormalCdf((value - mean) / sd))]);

        return Result.Ok(new AnalysisResult(
            "lilliefors",
            $"Prueba de Lilliefors de {name}",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("n", JsNumber.ToString(values.Length)),
                    new TileItem("D de Kolmogorov-Smirnov", ExtendedHelpers.N(result.Statistic)),
                    new TileItem("Valor p", ExtendedHelpers.P(result.PValue))
                ]),
                SigmaText.Decision(result.PValue, alpha, $"los datos siguen una distribución {label}"),
                new TableSection(
                    "Resultado",
                    ["Estadístico D", "Valor p", "n"],
                    [[
                        ExtendedHelpers.N(result.Statistic),
                        ExtendedHelpers.P(result.PValue),
                        JsNumber.ToString(values.Length)
                    ]],
                    Note: "Corrige la prueba de Kolmogorov-Smirnov porque los parámetros se estiman con los mismos datos; el valor p se obtiene de tablas (entre 0.001 y 0.2).")
            ],
            Subtitle: $"distribución {label} con parámetros estimados",
            Charts: [new SeriesChart("Función de distribución empírica y ajustada", [empirical, fitted], name, "Probabilidad acumulada")],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["statistic"] = result.Statistic,
                ["p"] = result.PValue,
                ["n"] = values.Length
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunKsOne(AnalysisInput input)
    {
        var frame = ExtendedHelpers.FrameOf(input);
        var name = ExtendedHelpers.FirstRole(input, "var", "Variable");
        var values = ExtendedHelpers.Complete(frame.Numeric(name));
        if (values.Length < 5) return ExtendedHelpers.Invalid(SigmaText.NeedMessage(name, 5));

        var dist = input.Pstr("dist", "norm");
        var location = input.Pnum("loc", 0);
        var scale = input.Pnum("scale", 1);
        if (!(scale > 0)) return ExtendedHelpers.Invalid("La escala debe ser mayor que 0.");

        Func<double, double> cdf = (dist switch
        {
            "uniform" => value => (value - location) / scale is var z && z is >= 0 and <= 1 ? z : (z < 0 ? 0 : 1),
            "expon" => value => value < location ? 0 : 1 - Math.Exp(-(value - location) / scale),
            "lognorm" => value =>
            {
                if (value <= 0) return 0;
                var z = (Math.Log(value) - location) / scale;
                return SpecialFunctions.NormalCdf(z);
            },
            _ => value => SpecialFunctions.NormalCdf((value - location) / scale)
        })!;

        var ordered = NumericMath.Sorted(values);
        var n = ordered.Length;
        var dPlus = 0.0;
        var dMinus = 0.0;
        for (var index = 0; index < n; index++)
        {
            var cdfValue = cdf(ordered[index]);
            dPlus = Math.Max(dPlus, (index + 1.0) / n - cdfValue);
            dMinus = Math.Max(dMinus, cdfValue - index / (double)n);
        }

        var alternative = input.Pstr("alternative", "two-sided");
        var statistic = alternative == "greater" ? dPlus : alternative == "less" ? dMinus : Math.Max(dPlus, dMinus);
        var pValue = alternative == "two-sided"
            ? ExtendedStatistics.KolmogorovSf(statistic, n)
            : Math.Min(1, 2 * ExtendedStatistics.KolmogorovSf(statistic, n));
        var alpha = input.Pnum("alpha", 0.05);
        var distributionName = dist switch
        {
            "uniform" => "Uniforme",
            "expon" => "Exponencial",
            "lognorm" => "Lognormal",
            _ => "Normal"
        };

        var grid = new double[250];
        var low = ordered[0];
        var high = ordered[^1];
        var spread = high - low;
        for (var index = 0; index < grid.Length; index++)
        {
            grid[index] = low - 0.1 * spread + 1.2 * spread * index / (grid.Length - 1.0);
        }
        var empirical = new XYSeries(
            "Empírica",
            [.. ordered.Select((_, index) => (double)(index + 1))],
            [.. ordered.Select((_, index) => (index + 1.0) / ordered.Length)]);
        var theoretical = new XYSeries("Teórica", grid, [.. grid.Select(value => cdf(value))]);

        return Result.Ok(new AnalysisResult(
            "ks-one-sample",
            $"Kolmogorov-Smirnov de {name}",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("n", JsNumber.ToString(n)),
                    new TileItem("D", ExtendedHelpers.N(statistic)),
                    new TileItem("Valor p", ExtendedHelpers.P(pValue))
                ]),
                SigmaText.Decision(pValue, alpha, "los datos provienen de esa distribución"),
                new TableSection(
                    "Distribución de referencia",
                    ["Distribución", "Ubicación", "Escala"],
                    [[distributionName, ExtendedHelpers.N(location), ExtendedHelpers.N(scale)]],
                    Note: "Si los parámetros se estimaron de estos datos, use la prueba de Lilliefors o Anderson-Darling.")
            ],
            Subtitle: "frente a una distribución completamente especificada",
            Charts: [new SeriesChart("Empírica frente a teórica", [empirical, theoretical], name, "Probabilidad acumulada")],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["statistic"] = statistic,
                ["p"] = pValue
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunKsTwo(AnalysisInput input)
    {
        var frame = ExtendedHelpers.FrameOf(input);
        var name = ExtendedHelpers.FirstRole(input, "var", "Variable");
        var group = ExtendedHelpers.FirstRole(input, "group", "Grupos (2 categorías)");
        var groups = ExtendedHelpers.Groups(frame, name, group);
        if (groups.Count != 2)
        {
            return ExtendedHelpers.Invalid($"«{group}» debe tener exactamente 2 categorías con datos (tiene {groups.Count}).");
        }

        var (labelA, a) = groups[0];
        var (labelB, b) = groups[1];
        if (a.Length < 1 || b.Length < 1) return ExtendedHelpers.Invalid("Las dos muestras necesitan datos.");

        var pooled = a.Concat(b).OrderBy(value => value).ToArray();
        var ecdfA = Ecdf(a);
        var ecdfB = Ecdf(b);
        var dPlus = 0.0;
        var dMinus = 0.0;
        foreach (var value in pooled)
        {
            var fa = CountAtMost(a, value) / (double)a.Length;
            var fb = CountAtMost(b, value) / (double)b.Length;
            dPlus = Math.Max(dPlus, fa - fb);
            dMinus = Math.Max(dMinus, fb - fa);
        }

        var alternative = input.Pstr("alternative", "two-sided");
        var statistic = alternative == "less" ? dMinus : alternative == "greater" ? dPlus : Math.Max(dPlus, dMinus);
        var pValue = alternative == "two-sided"
            ? ExtendedStatistics.KsTwoSampleSf(statistic, a.Length, b.Length)
            : AsymptoticTwoSampleOneSided(statistic, a.Length, b.Length);
        var alpha = input.Pnum("alpha", 0.05);

        return Result.Ok(new AnalysisResult(
            "ks-two-samples",
            $"Kolmogorov-Smirnov de dos muestras: {name}",
            [
                new TilesSection("Resumen",
                [
                    new TileItem($"n ({labelA})", JsNumber.ToString(a.Length)),
                    new TileItem($"n ({labelB})", JsNumber.ToString(b.Length)),
                    new TileItem("D", ExtendedHelpers.N(statistic)),
                    new TileItem("Valor p", ExtendedHelpers.P(pValue))
                ]),
                SigmaText.Decision(pValue, alpha, "las dos muestras provienen de la misma distribución"),
                new TableSection(
                    "Resumen",
                    ["Grupo", "n", "Media", "Mediana", "Desv. est."],
                    [
                        [labelA, JsNumber.ToString(a.Length), ExtendedHelpers.N(NumericMath.Mean(a)), ExtendedHelpers.N(NumericMath.Median(a)), ExtendedHelpers.N(NumericMath.Sd(a))],
                        [labelB, JsNumber.ToString(b.Length), ExtendedHelpers.N(NumericMath.Mean(b)), ExtendedHelpers.N(NumericMath.Median(b)), ExtendedHelpers.N(NumericMath.Sd(b))]
                    ])
            ],
            Subtitle: $"{labelA} frente a {labelB}",
            Charts:
            [
                new SeriesChart(
                    "Funciones de distribución empíricas",
                    [
                        new XYSeries(labelA, ecdfA.X, ecdfA.Y),
                        new XYSeries(labelB, ecdfB.X, ecdfB.Y)
                    ],
                    name,
                    "Probabilidad acumulada")
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["statistic"] = statistic,
                ["p"] = pValue
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunJarqueBera(AnalysisInput input)
    {
        var frame = ExtendedHelpers.FrameOf(input);
        var name = ExtendedHelpers.FirstRole(input, "var", "Variable");
        var values = ExtendedHelpers.Complete(frame.Numeric(name));
        if (values.Length < 8) return ExtendedHelpers.Invalid(SigmaText.NeedMessage(name, 8));

        var alpha = input.Pnum("alpha", 0.05);
        if (alpha < 0.001 || alpha > 0.5)
        {
            return ExtendedHelpers.Invalid("«Nivel de significancia α» debe estar entre 0.001 y 0.5.");
        }

        var n = values.Length;
        var mean = NumericMath.Mean(values);
        var m2 = 0.0;
        var m3 = 0.0;
        var m4 = 0.0;
        foreach (var value in values)
        {
            var delta = value - mean;
            var square = delta * delta;
            m2 += square;
            m3 += square * delta;
            m4 += square * square;
        }
        m2 /= n;
        m3 /= n;
        m4 /= n;
        var skew = m3 / Math.Pow(m2, 1.5);
        var kurtosis = m4 / (m2 * m2);
        var statistic = n / 6.0 * (skew * skew + Math.Pow(kurtosis - 3, 2) / 4);
        var pValue = Math.Exp(-statistic / 2);

        return Result.Ok(new AnalysisResult(
            "jarque-bera",
            $"Prueba de Jarque-Bera de {name}",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("n", JsNumber.ToString(n)),
                    new TileItem("JB", ExtendedHelpers.N(statistic)),
                    new TileItem("Valor p", ExtendedHelpers.P(pValue)),
                    new TileItem("Asimetría", ExtendedHelpers.N(skew)),
                    new TileItem("Curtosis", ExtendedHelpers.N(kurtosis))
                ]),
                SigmaText.Decision(pValue, alpha, "asimetría = 0 y curtosis = 3, como la normal"),
                new TableSection(
                    "Resultado",
                    ["JB", "Valor p", "Asimetría", "Curtosis (normal = 3)"],
                    [[
                        ExtendedHelpers.N(statistic),
                        ExtendedHelpers.P(pValue),
                        ExtendedHelpers.N(skew),
                        ExtendedHelpers.N(kurtosis)
                    ]])
            ],
            Subtitle: "normalidad según asimetría y curtosis",
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["statistic"] = statistic,
                ["p"] = pValue,
                ["skew"] = skew,
                ["kurtosis"] = kurtosis
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static (double[] X, double[] Y) Ecdf(double[] values)
    {
        var ordered = NumericMath.Sorted(values);
        var y = new double[ordered.Length];
        for (var index = 0; index < ordered.Length; index++) y[index] = (index + 1.0) / ordered.Length;
        return (ordered, y);
    }

    private static int CountAtMost(double[] values, double point)
    {
        var count = 0;
        foreach (var value in values)
        {
            if (value <= point) count++;
        }
        return count;
    }

    private static double AsymptoticTwoSampleOneSided(double d, int n1, int n2)
    {
        double m = Math.Max(n1, n2);
        double n = Math.Min(n1, n2);
        var en = m * n / (m + n);
        var z = Math.Sqrt(en) * d;
        var exponent = -2 * z * z - 2 * z * (m + 2 * n) / Math.Sqrt(m * n * (m + n)) / 3.0;
        return Math.Clamp(Math.Exp(exponent), 0, 1);
    }

    private static readonly Lazy<IReadOnlyList<AnalysisDefinition>> LazyAll =
        new(() => [Anderson, Lilliefors, KsOne, KsTwo, JarqueBera], isThreadSafe: true);

    public static IReadOnlyList<AnalysisDefinition> All => LazyAll.Value;
}
