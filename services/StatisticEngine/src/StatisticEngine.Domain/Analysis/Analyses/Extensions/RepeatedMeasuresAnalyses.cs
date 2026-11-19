using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>ANOVA de medidas repetidas con esfericidad de Mauchly (port de <c>anova_advanced._repeated</c>).</summary>
internal static class RepeatedMeasuresAnalyses
{
    private static readonly (string Key, string Label)[] Adjustments =
    [
        ("holm", "Holm"),
        ("bonferroni", "Bonferroni"),
        ("sidak", "Šidák"),
        ("fdr_bh", "Benjamini-Hochberg (FDR)")
    ];

    public static readonly AnalysisDefinition Repeated = new(
        "anova-repeated-measures",
        "ANOVA de medidas repetidas",
        "Modelos lineales",
        "Un mismo sujeto medido varias veces: esfericidad de Mauchly, correcciones de Greenhouse-Geisser y Huynh-Feldt, y comparaciones por pares.",
        [],
        [],
        [
            new(
                "adjust",
                "Ajuste de las comparaciones por pares",
                AnalysisParamKinds.Select,
                "holm",
                Adjustments.Select(item => new AnalysisParamOption(item.Key, item.Label)).ToArray(),
                false),
            new AnalysisParamDefinition("alpha", "Nivel de significancia α", AnalysisParamKinds.Number, 0.05, [], false, Minimum: 0.001, Maximum: 0.5)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Modelos lineales",
        Roles:
        [
            new ColumnRole("vars", "Mediciones (una columna por condición)", ColumnKinds.Numeric, Multiple: true, MinCount: 2)
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var names = input.RoleNames("vars", true);
        if (names.Count < 2) return ExtendedHelpers.Invalid("Elija al menos dos mediciones.");
        var complete = frame.CompleteCases(names);
        var n = complete.RowCount;
        var k = names.Count;
        if (n < 3) return ExtendedHelpers.Invalid("Se necesitan al menos 3 sujetos con todas las mediciones.");
        var alpha = input.Pnum("alpha", 0.05);
        var x = new double[n][];
        for (var row = 0; row < n; row++)
        {
            x[row] = new double[k];
            for (var column = 0; column < k; column++) x[row][column] = complete.Numeric(names[column])[row];
        }

        var grand = x.SelectMany(row => row).Average();
        var subjectMeans = x.Select(row => row.Average()).ToArray();
        var conditionMeans = Enumerable.Range(0, k).Select(column => x.Average(row => row[column])).ToArray();
        var ssSubject = k * subjectMeans.Sum(mean => Math.Pow(mean - grand, 2));
        var ssCondition = n * conditionMeans.Sum(mean => Math.Pow(mean - grand, 2));
        var ssTotal = x.Sum(row => row.Sum(value => Math.Pow(value - grand, 2)));
        var ssError = ssTotal - ssSubject - ssCondition;
        var dfC = k - 1;
        var dfE = (n - 1) * (k - 1);
        var f = ssCondition / dfC / (ssError / dfE);
        var sphericity = Sphericity(x, n, k);
        var etaPartial = ssCondition / (ssCondition + ssError);
        var etaGeneral = ssCondition / (ssCondition + ssSubject + ssError);

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Sujetos", JsNumber.ToString(n)),
                new TileItem("Mediciones", JsNumber.ToString(k)),
                new TileItem("F", ModelHelpers.N(f)),
                new TileItem("η² parcial", ModelHelpers.N(etaPartial))
            ])
        };
        if (complete.RowCount < frame.RowCount)
        {
            sections.Add(new AlertSection("Aviso", "info", $"Se omitieron {frame.RowCount - complete.RowCount} sujeto(s) con mediciones faltantes."));
        }

        sections.Add(new TableSection(
            "Efecto de la medición",
            ["Corrección", "gl (efecto)", "gl (error)", "F", "Valor p"],
            [
                new[] { "Sin corrección", JsNumber.ToString(dfC), JsNumber.ToString(dfE), ModelHelpers.N(f), ModelHelpers.P(ExtendedStatistics.FSf(f, dfC, dfE)) },
                new[]
                {
                    "Greenhouse-Geisser",
                    ModelHelpers.N(dfC * sphericity.Gg),
                    ModelHelpers.N(dfE * sphericity.Gg),
                    ModelHelpers.N(f),
                    ModelHelpers.P(ExtendedStatistics.FSf(f, dfC * sphericity.Gg, dfE * sphericity.Gg))
                },
                new[]
                {
                    "Huynh-Feldt",
                    ModelHelpers.N(dfC * sphericity.Hf),
                    ModelHelpers.N(dfE * sphericity.Hf),
                    ModelHelpers.N(f),
                    ModelHelpers.P(ExtendedStatistics.FSf(f, dfC * sphericity.Hf, dfE * sphericity.Hf))
                }
            ],
            Note: "Si no se cumple la esfericidad, use Greenhouse-Geisser (o Huynh-Feldt si ε > 0.75)."));

        sections.Add(new TableSection(
            "Tabla de análisis de varianza",
            ["Fuente", "gl", "Suma de cuadrados", "Cuadrado medio", "F", "Valor p", "η² parcial", "η² generalizado"],
            [
                new[]
                {
                    "Mediciones", JsNumber.ToString(dfC), ModelHelpers.N(ssCondition), ModelHelpers.N(ssCondition / dfC),
                    ModelHelpers.N(f), ModelHelpers.P(ExtendedStatistics.FSf(f, dfC, dfE)),
                    ModelHelpers.N(etaPartial), ModelHelpers.N(etaGeneral)
                },
                new[] { "Sujetos", JsNumber.ToString(n - 1), ModelHelpers.N(ssSubject), ModelHelpers.N(ssSubject / (n - 1)), string.Empty, string.Empty, string.Empty, string.Empty },
                new[] { "Error", JsNumber.ToString(dfE), ModelHelpers.N(ssError), ModelHelpers.N(ssError / dfE), string.Empty, string.Empty, string.Empty, string.Empty }
            ]));

        if (k >= 3)
        {
            sections.Add(new TableSection(
                "Esfericidad",
                ["Prueba", "W de Mauchly", "χ²", "gl", "Valor p", "ε Greenhouse-Geisser", "ε Huynh-Feldt"],
                [
                    new[]
                    {
                        "Mauchly", ModelHelpers.N(sphericity.W), ModelHelpers.N(sphericity.ChiSquare), JsNumber.ToString(sphericity.Df),
                        ModelHelpers.P(sphericity.P), ModelHelpers.N(sphericity.Gg), ModelHelpers.N(sphericity.Hf)
                    }
                ]));
            var violated = sphericity.P < alpha;
            sections.Add(new AlertSection(
                violated ? "Aviso" : "Conclusión",
                violated ? "warning" : "success",
                violated
                    ? "Se viola la esfericidad (Mauchly): use la corrección de Greenhouse-Geisser."
                    : "No hay evidencia de que se viole la esfericidad."));
        }

        var centered = x.Select(row =>
        {
            var rowMean = row.Average();
            return row.Select(value => value - rowMean + grand).ToArray();
        }).ToArray();
        var critical = InferenceDistributions.TQuantile(1 - alpha / 2, n - 1);
        var spread = Enumerable.Range(0, k)
            .Select(column => NumericMath.Sd(centered.Select(row => row[column]).ToArray()) / Math.Sqrt(n) * Math.Sqrt(k / (double)(k - 1)))
            .ToArray();
        var intervalPoints = Enumerable.Range(0, k)
            .Select(column => new IntervalPoint(
                names[column],
                conditionMeans[column],
                conditionMeans[column] - critical * spread[column],
                conditionMeans[column] + critical * spread[column]))
            .ToList();
        var pairs = new List<(int A, int B)>();
        for (var first = 0; first < k; first++)
        {
            for (var second = first + 1; second < k; second++) pairs.Add((first, second));
        }
        var raw = pairs.Select(pair =>
        {
            var differences = Enumerable.Range(0, n).Select(row => x[row][pair.A] - x[row][pair.B]).ToArray();
            var mean = differences.Average();
            var se = NumericMath.Sd(differences) / Math.Sqrt(n);
            var t = mean / se;
            return (Mean: mean, T: t, P: 2 * (1 - InferenceDistributions.TCdf(Math.Abs(t), n - 1)));
        }).ToArray();
        var adjust = input.Pstr("adjust", "holm");
        var adjusted = Adjust(raw.Select(item => item.P).ToArray(), adjust);
        var pairRows = pairs.Select((pair, index) => (IReadOnlyList<string>)new[]
        {
            names[pair.A],
            names[pair.B],
            ModelHelpers.N(raw[index].Mean),
            ModelHelpers.N(raw[index].T),
            ModelHelpers.P(raw[index].P),
            ModelHelpers.P(adjusted[index])
        }).ToArray();
        sections.Add(new TableSection(
            $"Comparaciones por pares (t pareada, ajuste {Adjustments.First(item => item.Key == adjust).Label})",
            ["Medición A", "Medición B", "Diferencia (A − B)", "t", "Valor p", "Valor p ajustado"],
            pairRows));

        return Result.Ok(new AnalysisResult(
            "anova-repeated-measures",
            "ANOVA de medidas repetidas",
            sections,
            Subtitle: $"{k} mediciones en {n} sujetos",
            Charts:
            [
                new IntervalChart(
                    $"Medias por medición (IC {SigmaText.General(100 * (1 - alpha))} % intrasujeto)",
                    intervalPoints,
                    "Media")
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["f"] = f,
                ["p"] = ExtendedStatistics.FSf(f, dfC, dfE),
                ["p_gg"] = ExtendedStatistics.FSf(f, dfC * sphericity.Gg, dfE * sphericity.Gg),
                ["mauchly_p"] = sphericity.P,
                ["epsilon_gg"] = sphericity.Gg,
                ["epsilon_hf"] = sphericity.Hf,
                ["eta_p"] = etaPartial
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private sealed record SphericityResult(double W, double ChiSquare, int Df, double P, double Gg, double Hf);

    private static SphericityResult Sphericity(double[][] x, int n, int k)
    {
        if (k == 2) return new SphericityResult(1, 0, 0, 1, 1, 1);
        var helmert = new double[k - 1][];
        for (var i = 1; i < k; i++)
        {
            var row = new double[k];
            for (var j = 0; j < i; j++) row[j] = 1;
            row[i] = -i;
            var norm = Math.Sqrt(i * (i + 1.0));
            for (var j = 0; j < k; j++) row[j] /= norm;
            helmert[i - 1] = row;
        }
        var contrasts = new double[n][];
        for (var row = 0; row < n; row++)
        {
            contrasts[row] = new double[k - 1];
            for (var j = 0; j < k - 1; j++) contrasts[row][j] = x[row].Zip(helmert[j], (a, b) => a * b).Sum();
        }
        var means = Enumerable.Range(0, k - 1).Select(column => contrasts.Average(row => row[column])).ToArray();
        var s = new double[k - 1][];
        for (var i = 0; i < k - 1; i++)
        {
            s[i] = new double[k - 1];
            for (var j = 0; j < k - 1; j++)
            {
                s[i][j] = contrasts.Sum(row => (row[i] - means[i]) * (row[j] - means[j])) / (n - 1.0);
            }
        }
        var m = k - 1;
        var trace = Enumerable.Range(0, m).Sum(i => s[i][i]);
        var s2 = Multiply(s, s);
        var trace2 = Enumerable.Range(0, m).Sum(i => s2[i][i]);
        var gg = trace * trace / (m * trace2);
        var hf = Math.Min(1, (n * m * gg - 2) / (m * (n - 1 - m * gg)));
        var determinant = Determinant(s);
        var w = Math.Max(determinant, 1e-300) / Math.Pow(trace / m, m);
        var chi2 = -(n - 1 - (2 * m * m + m + 2) / (6.0 * m)) * Math.Log(w);
        var df = k * (k - 1) / 2 - 1;
        return new SphericityResult(w, chi2, df, ExtendedStatistics.ChiSquareSf(chi2, df), gg, Math.Max(hf, 1.0 / m));
    }

    private static double[] Adjust(double[] raw, string method)
    {
        var m = raw.Length;
        if (m <= 1) return raw;
        return method switch
        {
            "bonferroni" => raw.Select(value => Math.Min(1, value * m)).ToArray(),
            "sidak" => raw.Select(value => 1 - Math.Pow(1 - value, m)).ToArray(),
            "fdr_bh" => Fdr(raw),
            _ => Holm(raw)
        };
    }

    private static double[] Holm(double[] raw)
    {
        var m = raw.Length;
        var order = Enumerable.Range(0, m).OrderBy(index => raw[index]).ToArray();
        var output = new double[m];
        var running = 0.0;
        for (var position = 0; position < m; position++)
        {
            var index = order[position];
            running = Math.Max(running, Math.Min(1, raw[index] * (m - position)));
            output[index] = running;
        }
        return output;
    }

    private static double[] Fdr(double[] raw)
    {
        var m = raw.Length;
        var order = Enumerable.Range(0, m).OrderByDescending(index => raw[index]).ToArray();
        var output = new double[m];
        var running = 1.0;
        for (var position = 0; position < m; position++)
        {
            var index = order[position];
            var rank = m - position;
            running = Math.Min(running, Math.Min(1, raw[index] * m / rank));
            output[index] = running;
        }
        return output;
    }

    private static double[][] Multiply(double[][] a, double[][] b)
    {
        var result = new double[a.Length][];
        for (var i = 0; i < a.Length; i++)
        {
            result[i] = new double[b[0].Length];
            for (var j = 0; j < b[0].Length; j++)
            {
                for (var k = 0; k < b.Length; k++) result[i][j] += a[i][k] * b[k][j];
            }
        }
        return result;
    }

    private static double Determinant(double[][] matrix)
    {
        var n = matrix.Length;
        var copy = matrix.Select(row => (double[])row.Clone()).ToArray();
        var determinant = 1.0;
        for (var column = 0; column < n; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < n; row++)
            {
                if (Math.Abs(copy[row][column]) > Math.Abs(copy[pivot][column])) pivot = row;
            }
            if (Math.Abs(copy[pivot][column]) < 1e-14) return 0;
            if (pivot != column)
            {
                (copy[pivot], copy[column]) = (copy[column], copy[pivot]);
                determinant = -determinant;
            }
            determinant *= copy[column][column];
            for (var row = column + 1; row < n; row++)
            {
                var factor = copy[row][column] / copy[column][column];
                for (var j = column; j < n; j++) copy[row][j] -= factor * copy[column][j];
            }
        }
        return determinant;
    }
}
