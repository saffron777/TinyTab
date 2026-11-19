using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Comparaciones múltiples post hoc (port de <c>anova_advanced._posthoc</c>).</summary>
internal static class AnovaPosthocAnalyses
{
    private static readonly (string Key, string Label)[] Methods =
    [
        ("tukey", "Tukey HSD (todas las parejas, varianzas iguales)"),
        ("games_howell", "Games-Howell (varianzas desiguales)"),
        ("dunnett", "Dunnett (cada grupo contra un control)"),
        ("bonferroni", "Bonferroni"),
        ("holm", "Holm"),
        ("sidak", "Šidák"),
        ("scheffe", "Scheffé"),
        ("lsd", "Diferencia mínima significativa de Fisher (sin ajuste)")
    ];

    public static readonly AnalysisDefinition Posthoc = new(
        "anova-posthoc",
        "Comparaciones múltiples (post hoc)",
        "Comparaciones",
        "Qué grupos difieren de cuáles: Tukey, Games-Howell, Dunnett, Bonferroni, Holm, Šidák, Scheffé o Fisher, con intervalos de confianza.",
        [],
        [],
        [
            new(
                "method",
                "Método",
                AnalysisParamKinds.Select,
                "tukey",
                Methods.Select(item => new AnalysisParamOption(item.Key, item.Label)).ToArray(),
                false),
            new(
                "control",
                "Grupo de control (vacío = el primero)",
                AnalysisParamKinds.Text,
                null,
                [],
                true,
                VisibleIf: new("method", ["dunnett"])),
            new AnalysisParamDefinition("alpha", "Nivel de significancia α", AnalysisParamKinds.Number, 0.05, [], false, Minimum: 0.001, Maximum: 0.5)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Comparaciones",
        Roles:
        [
            new ColumnRole("response", "Respuesta (Y)", ColumnKinds.Numeric),
            new ColumnRole("factor", "Factor (grupos)", ColumnKinds.Categorical)
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var response = ExtendedHelpers.FirstRole(input, "response", "Respuesta (Y)");
        var factor = ExtendedHelpers.FirstRole(input, "factor", "Factor");
        var method = input.Pstr("method", "tukey");
        var alpha = input.Pnum("alpha", 0.05);
        if (method is "tukey" or "games_howell" or "dunnett")
        {
            return ExtendedHelpers.Error(
                DomainErrorCode.NotSupported,
                $"El método «{method}» necesita la distribución del rango estudentizado y aún no está disponible; use bonferroni, holm, sidak, scheffe o lsd.");
        }

        var parts = ExtendedHelpers.Groups(frame, response, factor).Where(part => part.Values.Length >= 2).ToArray();
        if (parts.Length < 2) return ExtendedHelpers.Invalid("Se necesitan al menos 2 grupos con 2 datos cada uno.");
        var labels = parts.Select(part => part.Label).ToArray();
        var arrays = parts.Select(part => part.Values).ToArray();
        var sizes = arrays.Select(values => values.Length).ToArray();
        var means = arrays.Select(values => NumericMath.Mean(values)).ToArray();
        var n = sizes.Sum();
        var k = parts.Length;
        var dfError = n - k;
        var mse = arrays.Sum(values =>
        {
            var mean = NumericMath.Mean(values);
            return values.Sum(value => Math.Pow(value - mean, 2));
        }) / dfError;

        var pairs = new List<(int A, int B)>();
        for (var first = 0; first < k; first++)
        {
            for (var second = first + 1; second < k; second++) pairs.Add((first, second));
        }
        var m = pairs.Count;
        var resultRows = new List<(int A, int B, double Difference, double StandardError, double Statistic, double PValue, double Low, double High)>();
        foreach (var (first, second) in pairs)
        {
            var difference = means[first] - means[second];
            var standardError = Math.Sqrt(mse * (1.0 / sizes[first] + 1.0 / sizes[second]));
            var t = difference / standardError;
            var rawP = 2 * (1 - InferenceDistributions.TCdf(Math.Abs(t), dfError));
            double pValue;
            double half;
            switch (method)
            {
                case "scheffe":
                {
                    var f = t * t / (k - 1.0);
                    pValue = ExtendedStatistics.FSf(f, k - 1, dfError);
                    half = Math.Sqrt((k - 1) * ExtendedStatistics.FInv(1 - alpha, k - 1, dfError)) * standardError;
                    break;
                }
                case "lsd":
                    pValue = rawP;
                    half = InferenceDistributions.TQuantile(1 - alpha / 2, dfError) * standardError;
                    break;
                default:
                {
                    var level = method switch
                    {
                        "bonferroni" or "holm" => alpha / m,
                        "sidak" => 1 - Math.Pow(1 - alpha, 1.0 / m),
                        _ => alpha
                    };
                    pValue = rawP;
                    half = InferenceDistributions.TQuantile(1 - level / 2, dfError) * standardError;
                    break;
                }
            }
            resultRows.Add((first, second, difference, standardError, t, pValue, difference - half, difference + half));
        }

        if (method is "bonferroni" or "holm" or "sidak")
        {
            var raw = resultRows.Select(row => row.PValue).ToArray();
            var adjusted = method switch
            {
                "bonferroni" => raw.Select(value => Math.Min(1, value * m)).ToArray(),
                "sidak" => raw.Select(value => 1 - Math.Pow(1 - value, m)).ToArray(),
                _ => Holm(raw)
            };
            for (var index = 0; index < resultRows.Count; index++)
            {
                var row = resultRows[index];
                resultRows[index] = (row.A, row.B, row.Difference, row.StandardError, row.Statistic, adjusted[index], row.Low, row.High);
            }
        }

        var significant = resultRows.Count(row => row.PValue < alpha);
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Grupos", JsNumber.ToString(k)),
                new TileItem("Comparaciones", JsNumber.ToString(m)),
                new TileItem($"Diferencias significativas (α = {SigmaText.General(alpha)})", JsNumber.ToString(significant)),
                new TileItem("Error cuadrático medio", ModelHelpers.N(mse))
            ])
        };
        if (method == "lsd")
        {
            sections.Add(new AlertSection(
                "Aviso",
                "warning",
                "Sin ajuste por comparaciones múltiples la probabilidad de algún falso positivo crece rápido con el número de grupos. Úsela solo tras un ANOVA significativo."));
        }
        if (method == "holm")
        {
            sections.Add(new AlertSection(
                "Aviso",
                "info",
                "Holm ajusta los valores p; no tiene un intervalo de confianza simultáneo simple, por eso los límites mostrados usan Bonferroni."));
        }

        var tableRows = resultRows.Select(row => (IReadOnlyList<string>)new[]
        {
            labels[row.A],
            labels[row.B],
            ModelHelpers.N(row.Difference),
            ModelHelpers.N(row.StandardError),
            ModelHelpers.N(row.Statistic),
            ModelHelpers.P(row.PValue),
            ModelHelpers.N(row.Low),
            ModelHelpers.N(row.High),
            row.PValue < alpha ? "sí" : "no"
        }).ToArray();
        sections.Add(new TableSection(
            "Diferencias entre pares",
            [
                "Grupo A",
                "Grupo B",
                "Diferencia (A − B)",
                "Error est.",
                "t",
                "Valor p ajustado",
                $"IC {SigmaText.General(100 * (1 - alpha))} % simultáneo inf.",
                "sup.",
                "¿Difieren?"
            ],
            tableRows));

        var points = resultRows.Take(40)
            .Select(row => new IntervalPoint($"{labels[row.A]} − {labels[row.B]}", row.Difference, row.Low, row.High))
            .ToList();
        sections.Add(new TableSection(
            "Medias por grupo",
            ["Grupo", "n", "Media", "Desv. est."],
            Enumerable.Range(0, k).Select(index => (IReadOnlyList<string>)new[]
            {
                labels[index],
                JsNumber.ToString(sizes[index]),
                ModelHelpers.N(means[index]),
                ModelHelpers.N(NumericMath.Sd(arrays[index]))
            }).ToArray()));

        var pairsResult = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var row in resultRows) pairsResult[$"{labels[row.A]}|{labels[row.B]}"] = row.PValue;

        return Result.Ok(new AnalysisResult(
            "anova-posthoc",
            $"Comparaciones múltiples: {response} según {factor}",
            sections,
            Subtitle: $"{Methods.First(item => item.Key == method).Label.Split(" (")[0]} · {k} grupos · n = {n}",
            Charts:
            [
                new IntervalChart("Diferencias y sus intervalos de confianza", points, "Diferencia de medias")
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["significant"] = significant,
                ["comparisons"] = m,
                ["pairs"] = pairsResult
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double[] Holm(double[] raw)
    {
        var m = raw.Length;
        var order = Enumerable.Range(0, m).OrderBy(index => raw[index]).ToArray();
        var adjusted = new double[m];
        var running = 0.0;
        for (var position = 0; position < m; position++)
        {
            var index = order[position];
            var value = Math.Min(1, raw[index] * (m - position));
            running = Math.Max(running, value);
            adjusted[index] = running;
        }
        return adjusted;
    }
}
