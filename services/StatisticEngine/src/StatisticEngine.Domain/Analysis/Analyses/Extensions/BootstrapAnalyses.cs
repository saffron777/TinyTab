using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Bootstrap de un estadístico con intervalo percentil (análisis no determinista).</summary>
internal static class BootstrapAnalyses
{
    public static readonly AnalysisDefinition Bootstrap = new(
        "bootstrap",
        "Bootstrap: intervalo de confianza de un estadístico",
        "Remuestreo",
        "Intervalo de confianza sin supuestos de distribución para una media, mediana, desviación, percentil, correlación o diferencia entre grupos.",
        [],
        [],
        [
            new(
                "statistic",
                "Estadístico",
                AnalysisParamKinds.Select,
                "mean",
                [
                    new("mean", "Media"),
                    new("median", "Mediana"),
                    new("std", "Desviación estándar"),
                    new("trimmed", "Media recortada 10 %"),
                    new("iqr", "Rango intercuartílico"),
                    new("percentile", "Percentil (indique cuál)"),
                    new("correlation", "Correlación de Pearson (dos columnas)"),
                    new("mean_diff", "Diferencia de medias (dos grupos)"),
                    new("median_diff", "Diferencia de medianas (dos grupos)")
                ],
                false),
            new("percentile", "Percentil (0 a 100)", AnalysisParamKinds.Number, 90.0, [], false, Minimum: 0, Maximum: 100, VisibleIf: new("statistic", ["percentile"])),
            new(
                "method",
                "Método del intervalo",
                AnalysisParamKinds.Select,
                "BCa",
                [
                    new("BCa", "BCa (corregido por sesgo y asimetría)"),
                    new("percentile", "Percentil"),
                    new("basic", "Básico")
                ],
                false),
            new("confidence", "Nivel de confianza (%)", AnalysisParamKinds.Number, 95.0, [], false, Minimum: 50, Maximum: 99.9),
            new("resamples", "Réplicas bootstrap", AnalysisParamKinds.Integer, 10000.0, [], false, Minimum: 500, Maximum: 200000),
            new("seed", "Semilla (para repetir el resultado)", AnalysisParamKinds.Integer, 12345.0, [], false, Minimum: 0, Maximum: 2147483647)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Remuestreo",
        Heavy: true,
        Roles:
        [
            new ColumnRole("var", "Variable", ColumnKinds.Numeric),
            new ColumnRole("var2", "Segunda columna (correlación)", ColumnKinds.Numeric, Optional: true),
            new ColumnRole("group", "Grupos de 2 categorías (diferencias)", ColumnKinds.Categorical, Optional: true)
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var kind = input.Pstr("statistic", "mean");
        if (kind is not ("mean" or "median" or "std" or "percentile"))
        {
            return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "Por ahora el bootstrap admite media, mediana, desviación estándar y percentil.");
        }
        var frame = input.AsFrame();
        var variable = ExtendedHelpers.FirstRole(input, "var", "Variable");
        var values = frame.Numeric(variable).Where(value => !double.IsNaN(value)).ToArray();
        if (values.Length < 5) return ExtendedHelpers.Invalid("Se necesitan al menos 5 valores.");
        var percentile = input.Pnum("percentile", 90);
        Func<double[], double> statistic = kind switch
        {
            "median" => sample => NumericMath.Quantile(sample, 0.5),
            "std" => sample => NumericMath.Sd(sample),
            "percentile" => sample => NumericMath.Quantile(sample, percentile / 100),
            _ => sample => sample.Average()
        };
        var estimate = statistic(values);
        var resamples = input.Pint("resamples", 10000);
        var confidence = input.Pnum("confidence", 95) / 100;
        var random = new System.Random(input.Pint("seed", 12345));
        var draws = new double[resamples];
        for (var draw = 0; draw < resamples; draw++)
        {
            var sample = new double[values.Length];
            for (var index = 0; index < sample.Length; index++) sample[index] = values[random.Next(values.Length)];
            draws[draw] = statistic(sample);
        }
        var low = NumericMath.Quantile(draws, (1 - confidence) / 2);
        var high = NumericMath.Quantile(draws, 1 - (1 - confidence) / 2);
        var standardError = NumericMath.Sd(draws);
        var bias = draws.Average() - estimate;
        var label = kind switch
        {
            "median" => $"Mediana de {variable}",
            "std" => $"Desviación estándar de {variable}",
            "percentile" => $"Percentil {SigmaText.General(percentile)} de {variable}",
            _ => $"Media de {variable}"
        };
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Estimación", ModelHelpers.N(estimate)),
                new TileItem("Error estándar bootstrap", ModelHelpers.N(standardError)),
                new TileItem("Límite inferior", ModelHelpers.N(low)),
                new TileItem("Límite superior", ModelHelpers.N(high))
            ]),
            new TableSection(
                "Intervalo de confianza bootstrap",
                ["Estadístico", "Estimación", "Error estándar", "Sesgo", "Límite inf.", "Límite sup."],
                [[label, ModelHelpers.N(estimate), ModelHelpers.N(standardError), ModelHelpers.N(bias), ModelHelpers.N(low), ModelHelpers.N(high)]],
                Note: "Sesgo = media de las réplicas − estimación original.")
        };
        var points = draws.OrderBy(value => value).Select((value, index) => new ScatterPoint(index + 1, value)).ToList();
        return Result.Ok(new AnalysisResult(
            "bootstrap",
            $"Bootstrap: {label}",
            sections,
            Subtitle: $"{resamples.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} réplicas · IC {SigmaText.General(100 * confidence)} % (percentil) · semilla {input.Pint("seed", 12345)}",
            Charts:
            [
                new ScatterChart("Distribución bootstrap del estadístico", points, "Réplica", "Estadístico")
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["estimate"] = estimate,
                ["se"] = standardError,
                ["low"] = low,
                ["high"] = high,
                ["bias"] = bias
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
