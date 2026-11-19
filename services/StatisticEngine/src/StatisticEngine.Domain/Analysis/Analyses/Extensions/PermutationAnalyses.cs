using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Prueba de permutación (análisis no determinista).</summary>
internal static class PermutationAnalyses
{
    public static readonly AnalysisDefinition Permutation = new(
        "permutation",
        "Prueba de permutación",
        "Remuestreo",
        "Valor p exacto o aproximado reordenando los datos al azar: diferencia de medias o medianas entre grupos, datos pareados o correlación.",
        [],
        [],
        [
            new(
                "design",
                "Qué se compara",
                AnalysisParamKinds.Select,
                "mean_diff",
                [
                    new("mean_diff", "Diferencia de medias (dos grupos)"),
                    new("median_diff", "Diferencia de medianas (dos grupos)"),
                    new("paired", "Diferencia media de datos pareados (dos columnas)"),
                    new("correlation", "Correlación de Pearson (dos columnas)")
                ],
                false),
            new(
                "alternative",
                "Hipótesis alternativa",
                AnalysisParamKinds.Select,
                "two-sided",
                [new("two-sided", "Distinta (≠)"), new("greater", "Mayor (>)"), new("less", "Menor (<)")],
                false),
            new("resamples", "Permutaciones", AnalysisParamKinds.Integer, 9999.0, [], false, Minimum: 999, Maximum: 200000),
            new AnalysisParamDefinition("alpha", "Nivel de significancia α", AnalysisParamKinds.Number, 0.05, [], false, Minimum: 0.001, Maximum: 0.5),
            new("seed", "Semilla (para repetir el resultado)", AnalysisParamKinds.Integer, 12345.0, [], false, Minimum: 0, Maximum: 2147483647)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Remuestreo",
        Heavy: true,
        Roles:
        [
            new ColumnRole("var", "Variable", ColumnKinds.Numeric),
            new ColumnRole("var2", "Segunda columna (pareados / correlación)", ColumnKinds.Numeric, Optional: true),
            new ColumnRole("group", "Grupos de 2 categorías (diferencias)", ColumnKinds.Categorical, Optional: true)
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var kind = input.Pstr("design", "mean_diff");
        var frame = input.AsFrame();
        var variable = ExtendedHelpers.FirstRole(input, "var", "Variable");
        var second = ExtendedHelpers.FirstRole(input, "var2", "Segunda columna");
        var group = ExtendedHelpers.FirstRole(input, "group", "Grupos");
        var alpha = input.Pnum("alpha", 0.05);
        var resamples = input.Pint("resamples", 9999);
        var alternative = input.Pstr("alternative", "two-sided");
        var random = new System.Random(input.Pint("seed", 12345));
        double observed;
        double[] nullDistribution;
        string label;
        if (kind is "paired" or "correlation")
        {
            if (string.IsNullOrEmpty(second)) return ExtendedHelpers.Invalid("Elija la segunda columna.");
            var complete = frame.CompleteCases([variable, second]);
            var x = complete.Numeric(variable);
            var y = complete.Numeric(second);
            if (x.Length < 5) return ExtendedHelpers.Invalid("Se necesitan al menos 5 pares completos.");
            if (kind == "paired")
            {
                observed = x.Select((value, index) => value - y[index]).Average();
                label = $"media de {variable} − {second}";
                nullDistribution = new double[resamples];
                for (var draw = 0; draw < resamples; draw++)
                {
                    var sum = 0.0;
                    for (var index = 0; index < x.Length; index++) sum += random.Next(2) == 0 ? x[index] - y[index] : y[index] - x[index];
                    nullDistribution[draw] = sum / x.Length;
                }
            }
            else
            {
                observed = Correlation(x, y);
                label = $"correlación de {variable} y {second}";
                nullDistribution = new double[resamples];
                var shuffled = (double[])y.Clone();
                for (var draw = 0; draw < resamples; draw++)
                {
                    for (var index = shuffled.Length - 1; index > 0; index--)
                    {
                        var swap = random.Next(index + 1);
                        (shuffled[index], shuffled[swap]) = (shuffled[swap], shuffled[index]);
                    }
                    nullDistribution[draw] = Correlation(x, shuffled);
                }
            }
        }
        else
        {
            if (string.IsNullOrEmpty(group)) return ExtendedHelpers.Invalid("Elija la columna de grupos.");
            var parts = ExtendedHelpers.Groups(frame, variable, group).Where(part => part.Values.Length >= 2).ToArray();
            if (parts.Length != 2) return ExtendedHelpers.Invalid($"«{group}» debe tener exactamente 2 categorías con datos.");
            var first = parts[0].Values;
            var secondValues = parts[1].Values;
            Func<double[], double> reducer = kind == "median_diff" ? values => NumericMath.Quantile(values, 0.5) : values => values.Average();
            observed = reducer(first) - reducer(secondValues);
            label = $"{(kind == "median_diff" ? "mediana" : "media")} de {parts[0].Label} − {parts[1].Label}";
            var combined = first.Concat(secondValues).ToArray();
            nullDistribution = new double[resamples];
            for (var draw = 0; draw < resamples; draw++)
            {
                for (var index = combined.Length - 1; index > 0; index--)
                {
                    var swap = random.Next(index + 1);
                    (combined[index], combined[swap]) = (combined[swap], combined[index]);
                }
                nullDistribution[draw] = reducer(combined[..first.Length]) - reducer(combined[first.Length..]);
            }
        }
        var moreExtreme = alternative switch
        {
            "greater" => nullDistribution.Count(value => value >= observed),
            "less" => nullDistribution.Count(value => value <= observed),
            _ => nullDistribution.Count(value => Math.Abs(value) >= Math.Abs(observed))
        };
        var pValue = (moreExtreme + 1.0) / (resamples + 1.0);
        var reject = pValue < alpha;
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Estadístico observado", ModelHelpers.N(observed)),
                new TileItem("Valor p", ModelHelpers.P(pValue)),
                new TileItem("Permutaciones", JsNumber.ToString(resamples))
            ]),
            new AlertSection(
                reject ? "Aviso" : "Conclusión",
                reject ? "warning" : "success",
                $"p = {pValue.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)} {(reject ? "<" : "≥")} α = {SigmaText.General(alpha)}: {(reject ? "se rechaza" : "no se rechaza")} la hipótesis de que el estadístico no difiere de lo esperado por azar."),
            new TableSection(
                "Resultado",
                ["Estadístico", "Observado", "Media bajo H₀", "Desv. est. bajo H₀", "Valor p"],
                [[
                    label,
                    ModelHelpers.N(observed),
                    ModelHelpers.N(nullDistribution.Average()),
                    ModelHelpers.N(NumericMath.Sd(nullDistribution)),
                    ModelHelpers.P(pValue)
                ]],
                Note: "El valor p es la proporción de permutaciones con un estadístico tan o más extremo que el observado (incluye la observada).")
        };
        var points = nullDistribution.OrderBy(value => value).Select((value, index) => new ScatterPoint(index + 1, value)).ToList();
        return Result.Ok(new AnalysisResult(
            "permutation",
            $"Prueba de permutación: {label}",
            sections,
            Subtitle: $"{resamples.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} permutaciones · semilla {input.Pint("seed", 12345)}",
            Charts:
            [
                new ScatterChart("Distribución del estadístico bajo la hipótesis nula", points, "Permutación", label)
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["observed"] = observed,
                ["p"] = pValue,
                ["permutations"] = resamples
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double Correlation(double[] x, double[] y)
    {
        var meanX = x.Average();
        var meanY = y.Average();
        var numerator = x.Select((value, index) => (value - meanX) * (y[index] - meanY)).Sum();
        var denominator = Math.Sqrt(x.Sum(value => Math.Pow(value - meanX, 2)) * y.Sum(value => Math.Pow(value - meanY, 2)));
        return denominator > 0 ? numerator / denominator : 0;
    }
}
