using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Pruebas de signos, rachas y equivalencia TOST.</summary>
internal static class AdditionalTestAnalyses
{
    public static readonly AnalysisDefinition SignTest = new(
        "sign-test",
        "Prueba de los signos (mediana)",
        "Pruebas no paramétricas",
        "Contrasta la mediana con un valor, o la mediana de las diferencias de dos columnas pareadas; con IC para la mediana.",
        [],
        [],
        [
            new("median0", "Mediana de referencia (una columna)", AnalysisParamKinds.Number, 0.0, [], false),
            new("alternative", "Hipótesis alternativa", AnalysisParamKinds.Select, "two-sided", [new("two-sided", "Distinta (≠)"), new("greater", "Mayor (>)"), new("less", "Menor (<)")], false),
            new("alpha", "Nivel de significancia α", AnalysisParamKinds.Number, 0.05, [], false, Minimum: 0.001, Maximum: 0.5)
        ],
        RunSign,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "No paramétricas y datos pareados",
        Roles:
        [
            new ColumnRole("var", "Variable", ColumnKinds.Numeric),
            new ColumnRole("var2", "Segunda columna (datos pareados, opcional)", ColumnKinds.Numeric, Optional: true)
        ]);

    public static readonly AnalysisDefinition RunsTest = new(
        "runs",
        "Prueba de rachas (aleatoriedad)",
        "Pruebas no paramétricas",
        "¿Es aleatoria la secuencia de datos? Cuenta las rachas por encima y por debajo de un valor de corte.",
        [],
        [],
        [
            new("cutoff", "Corte", AnalysisParamKinds.Select, "median", [new("median", "Mediana"), new("mean", "Media"), new("value", "Un valor")], false),
            new("value", "Valor de corte", AnalysisParamKinds.Number, 0.0, [], false),
            new("alpha", "Nivel de significancia α", AnalysisParamKinds.Number, 0.05, [], false, Minimum: 0.001, Maximum: 0.5)
        ],
        RunRuns,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "No paramétricas y datos pareados",
        Roles: [new ColumnRole("var", "Variable", ColumnKinds.Numeric)]);

    public static readonly AnalysisDefinition Tost = new(
        "tost",
        "Equivalencia de medias (TOST)",
        "Pruebas de equivalencia",
        "Demuestra que una diferencia es tan pequeña que no importa: dos pruebas unilaterales contra límites de equivalencia.",
        [],
        [],
        [
            new("design", "Diseño", AnalysisParamKinds.Select, "two", [new("one", "Una muestra frente a un valor"), new("two", "Dos muestras independientes"), new("paired", "Datos pareados")], false),
            new("target", "Valor de referencia", AnalysisParamKinds.Number, 0.0, [], false),
            new("low", "Límite inferior de equivalencia (diferencia)", AnalysisParamKinds.Number, -1.0, [], false),
            new("upp", "Límite superior de equivalencia (diferencia)", AnalysisParamKinds.Number, 1.0, [], false),
            new("alpha", "Nivel de significancia α", AnalysisParamKinds.Number, 0.05, [], false, Minimum: 0.001, Maximum: 0.5)
        ],
        RunTost,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Equivalencia y multivariadas",
        Roles:
        [
            new ColumnRole("var", "Variable", ColumnKinds.Numeric),
            new ColumnRole("var2", "Segunda columna (datos pareados)", ColumnKinds.Numeric, Optional: true),
            new ColumnRole("group", "Grupos (2 muestras independientes)", ColumnKinds.Categorical, Optional: true)
        ]);

    private static Result<AnalysisResult> RunSign(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var variable = ExtendedHelpers.FirstRole(input, "var", "Variable");
        var second = ExtendedHelpers.FirstRole(input, "var2", "Segunda columna");
        var first = frame.Numeric(variable);
        var differences = second.Length > 0
            ? Enumerable.Range(0, frame.RowCount).Where(row => !double.IsNaN(first[row]) && !double.IsNaN(frame.Numeric(second)[row])).Select(row => first[row] - frame.Numeric(second)[row]).ToArray()
            : first.Where(value => !double.IsNaN(value)).Select(value => value - input.Pnum("median0", 0)).ToArray();
        var positive = differences.Count(value => value > 0);
        var negative = differences.Count(value => value < 0);
        var ties = differences.Length - positive - negative;
        var n = positive + negative;
        if (n == 0) return ExtendedHelpers.Invalid("No hay diferencias no nulas.");
        var alternative = input.Pstr("alternative", "two-sided");
        double pValue = alternative switch
        {
            "greater" => 1 - BinomialCdf(positive - 1, n, 0.5),
            "less" => BinomialCdf(positive, n, 0.5),
            _ => 2 * Math.Min(BinomialCdf(positive, n, 0.5), 1 - BinomialCdf(positive - 1, n, 0.5))
        };
        pValue = Math.Min(1, pValue);
        var alpha = input.Pnum("alpha", 0.05);
        var sample = second.Length > 0 ? differences : first.Where(value => !double.IsNaN(value)).ToArray();
        var sorted = sample.OrderBy(value => value).ToArray();
        var median = NumericMath.Quantile(sorted, 0.5);
        var k = 1;
        while (k < sorted.Length && BinomialCdf(k, sorted.Length, 0.5) <= alpha / 2) k++;
        var achieved = 1 - 2 * BinomialCdf(k - 1, sorted.Length, 0.5);
        var low = sorted[Math.Max(0, k - 1)];
        var high = sorted[Math.Min(sorted.Length - 1, sorted.Length - k)];
        var reference = input.Pnum("median0", 0);
        var rejects = pValue < alpha;
        var message = $"p {(pValue < 0.0001 ? "< 0.0001" : "= " + ModelHelpers.P(pValue))} {(rejects ? "<" : "≥")} α = {SigmaText.General(alpha)}: {(rejects ? "se rechaza" : "no se rechaza")} H₀ (la mediana de {variable} es {SigmaText.General(reference)}).";
        return Result.Ok(new AnalysisResult(
            "sign-test",
            "Prueba de los signos",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("n", JsNumber.ToString(sorted.Length)),
                    new TileItem("Por encima", JsNumber.ToString(positive)),
                    new TileItem("Por debajo", JsNumber.ToString(negative)),
                    new TileItem("Empates", JsNumber.ToString(ties)),
                    new TileItem("Valor p", ModelHelpers.P(pValue))
                ]),
                new AlertSection("Conclusión", "warning", message),
                new TableSection("Resultado", ["Mediana de la muestra", "Valor de referencia", "Por encima", "Por debajo", "Valor p (exacto)"],
                    [[ModelHelpers.N(median), ModelHelpers.N(reference), JsNumber.ToString(positive), JsNumber.ToString(negative), ModelHelpers.P(pValue)]]),
                new TableSection("Intervalo de confianza para la mediana", ["Confianza lograda", "Inferior", "Superior"],
                    [[ModelHelpers.N(achieved), ModelHelpers.N(low), ModelHelpers.N(high)]],
                    Note: "Basado en estadísticos de orden: la confianza solo puede tomar ciertos valores discretos.")
            ],
            Subtitle: second.Length > 0 ? $"diferencias {variable} − {second}" : $"mediana de {variable}",
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal) { ["positive"] = positive, ["negative"] = negative, ["p"] = pValue, ["n"] = sorted.Length, ["median"] = median },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunRuns(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var variable = ExtendedHelpers.FirstRole(input, "var", "Variable");
        var values = frame.Numeric(variable).Where(value => !double.IsNaN(value)).ToArray();
        var cutoff = input.Pstr("cutoff", "median") switch
        {
            "mean" => values.Average(),
            "value" => input.Pnum("value", 0),
            _ => NumericMath.Quantile(values, 0.5)
        };
        var above = values.Count(value => value > cutoff);
        var below = values.Count(value => value <= cutoff);
        var binary = values.Select(value => value > cutoff).ToArray();
        var runs = 1;
        for (var index = 1; index < binary.Length; index++) if (binary[index] != binary[index - 1]) runs++;
        var expected = 2.0 * above * below / (above + below) + 1;
        var variance = 2.0 * above * below * (2.0 * above * below - above - below) / (Math.Pow(above + below, 2) * (above + below - 1));
        var z = (runs - expected) / Math.Sqrt(variance);
        var pValue = 2 * (1 - SpecialFunctions.NormalCdf(Math.Abs(z)));
        var alpha = input.Pnum("alpha", 0.05);
        var rejects = pValue < alpha;
        var message = $"p  {(pValue < 0.0001 ? "< 0.0001" : "= " + ModelHelpers.P(pValue))} {(rejects ? "<" : "≥")} α = {SigmaText.General(alpha)}: {(rejects ? "se rechaza" : "no se rechaza")} H₀ (la secuencia es aleatoria).";
        return Result.Ok(new AnalysisResult(
            "runs",
            $"Prueba de rachas de {variable}",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Rachas observadas", JsNumber.ToString(runs)),
                    new TileItem("Rachas esperadas", ModelHelpers.N(expected)),
                    new TileItem("Z", ModelHelpers.N(z)),
                    new TileItem("Valor p", ModelHelpers.P(pValue))
                ]),
                new AlertSection("Conclusión", rejects ? "warning" : "success", message),
                new TableSection("Resultado", ["Corte", "Por encima", "Por debajo / igual", "Rachas", "Esperadas", "Z", "Valor p"],
                    [[ModelHelpers.N(cutoff), JsNumber.ToString(above), JsNumber.ToString(below), JsNumber.ToString(runs), ModelHelpers.N(expected), ModelHelpers.N(z), ModelHelpers.P(pValue)]],
                    Note: "Pocas rachas indican tendencia o agrupamiento; demasiadas, oscilación sistemática. Los datos iguales al corte se omiten.")
            ],
            Subtitle: $"aleatoriedad respecto a {ModelHelpers.N(cutoff)}",
            Charts: [new LineChart("Secuencia", [new LineSeries("Valor", values)], "Orden")],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal) { ["runs"] = runs, ["expected"] = expected, ["z"] = z, ["p"] = pValue, ["n"] = values.Length },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunTost(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var variable = ExtendedHelpers.FirstRole(input, "var", "Variable");
        var second = ExtendedHelpers.FirstRole(input, "var2", "Segunda columna");
        var group = ExtendedHelpers.FirstRole(input, "group", "Grupos");
        var design = input.Pstr("design", "two");
        double difference;
        double standardError;
        double degrees;
        string groupText = string.Empty;
        if (design == "paired")
        {
            if (second.Length == 0) return ExtendedHelpers.Invalid("El diseño pareado necesita la segunda columna.");
            var differences = Enumerable.Range(0, frame.RowCount).Where(row => !double.IsNaN(frame.Numeric(variable)[row]) && !double.IsNaN(frame.Numeric(second)[row])).Select(row => frame.Numeric(variable)[row] - frame.Numeric(second)[row]).ToArray();
            difference = differences.Average();
            standardError = NumericMath.Sd(differences) / Math.Sqrt(differences.Length);
            degrees = differences.Length - 1;
        }
        else if (design == "one")
        {
            var values = frame.Numeric(variable).Where(value => !double.IsNaN(value)).ToArray();
            difference = values.Average() - input.Pnum("target", 0);
            standardError = NumericMath.Sd(values) / Math.Sqrt(values.Length);
            degrees = values.Length - 1;
        }
        else
        {
            if (group.Length == 0) return ExtendedHelpers.Invalid("El diseño de dos muestras necesita grupos.");
            var levels = frame.Levels(group).Take(2).ToArray();
            if (levels.Length < 2) return ExtendedHelpers.Invalid("Se necesitan dos grupos.");
            var texts = frame.Texts(group);
            var first = Enumerable.Range(0, frame.RowCount).Where(row => texts[row] == levels[0]).Select(row => frame.Numeric(variable)[row]).Where(value => !double.IsNaN(value)).ToArray();
            var secondValues = Enumerable.Range(0, frame.RowCount).Where(row => texts[row] == levels[1]).Select(row => frame.Numeric(variable)[row]).Where(value => !double.IsNaN(value)).ToArray();
            difference = first.Average() - secondValues.Average();
            var firstVariance = Math.Pow(NumericMath.Sd(first), 2);
            var secondVariance = Math.Pow(NumericMath.Sd(secondValues), 2);
            standardError = Math.Sqrt(firstVariance / first.Length + secondVariance / secondValues.Length);
            degrees = Math.Pow(firstVariance / first.Length + secondVariance / secondValues.Length, 2) /
                      (Math.Pow(firstVariance / first.Length, 2) / (first.Length - 1) + Math.Pow(secondVariance / secondValues.Length, 2) / (secondValues.Length - 1));
            groupText = $"{levels[0]} − {levels[1]}";
        }
        var alpha = input.Pnum("alpha", 0.05);
        var low = input.Pnum("low", -1);
        var upp = input.Pnum("upp", 1);
        var tLow = (difference - low) / standardError;
        var tUpp = (difference - upp) / standardError;
        var pLow = 1 - InferenceDistributions.TCdf(tLow, degrees);
        var pUpp = InferenceDistributions.TCdf(tUpp, degrees);
        var pValue = Math.Max(pLow, pUpp);
        var critical = InferenceDistributions.TQuantile(1 - alpha, degrees);
        var intervalLow = difference - critical * standardError;
        var intervalHigh = difference + critical * standardError;
        var equivalent = pValue < alpha;
        var message = $"El IC del {SigmaText.General(100 * (1 - 2 * alpha))} % [{intervalLow.ToString("G4", System.Globalization.CultureInfo.InvariantCulture)}, {intervalHigh.ToString("G4", System.Globalization.CultureInfo.InvariantCulture)}] queda dentro de los límites [{ModelHelpers.N(low)}, {ModelHelpers.N(upp)}]: {(equivalent ? "se concluye equivalencia." : "no se concluye equivalencia.")}";
        return Result.Ok(new AnalysisResult(
            "tost",
            "Prueba de equivalencia (TOST)",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Diferencia", ModelHelpers.N(difference)),
                    new TileItem("Valor p de TOST", ModelHelpers.P(pValue)),
                    new TileItem("Decisión", equivalent ? "Equivalentes" : "No equivalentes")
                ]),
                new AlertSection("Conclusión", equivalent ? "success" : "warning", message),
                new TableSection("Dos pruebas unilaterales", ["Hipótesis nula", "Estadístico t", "gl", "Valor p"], [
                    new[] { $"diferencia ≤ {ModelHelpers.N(low)}", ModelHelpers.N(tLow), ModelHelpers.N(degrees), ModelHelpers.P(pLow) },
                    new[] { $"diferencia ≥ {ModelHelpers.N(upp)}", ModelHelpers.N(tUpp), ModelHelpers.N(degrees), ModelHelpers.P(pUpp) }
                ], Note: "La equivalencia se declara si las dos hipótesis nulas se rechazan (el mayor de los dos valores p es menor que α)."),
                new TableSection("Diferencia e intervalo", ["Diferencia", "Error estándar", $"IC {SigmaText.General(100 * (1 - 2 * alpha))} % inf.", "IC sup."],
                    [[ModelHelpers.N(difference), ModelHelpers.N(standardError), ModelHelpers.N(intervalLow), ModelHelpers.N(intervalHigh)]])
            ],
            Subtitle: groupText.Length > 0 ? $"{groupText} · límites [{ModelHelpers.N(low)}, {ModelHelpers.N(upp)}]" : $"límites [{ModelHelpers.N(low)}, {ModelHelpers.N(upp)}]",
            Charts: [new LineChart("Intervalo", [new LineSeries("Diferencia", [difference])], "Diferencia")],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal) { ["difference"] = difference, ["se"] = standardError, ["p"] = pValue, ["equivalent"] = equivalent, ["ci"] = new[] { intervalLow, intervalHigh } },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double BinomialCdf(int x, int n, double p)
    {
        var total = 0.0;
        for (var k = 0; k <= Math.Min(x, n); k++) total += Math.Exp(SpecialFunctions.LogGamma(n + 1) - SpecialFunctions.LogGamma(k + 1) - SpecialFunctions.LogGamma(n - k + 1) + k * Math.Log(p) + (n - k) * Math.Log(1 - p));
        return Math.Min(1, total);
    }
}
