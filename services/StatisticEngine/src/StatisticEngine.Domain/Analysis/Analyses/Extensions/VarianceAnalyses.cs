using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Varianzas, medianas y bloques: análisis no paramétricos y de homogeneidad de Sigma.</summary>
internal static class VarianceAnalyses
{
    private const string Category = "Pruebas adicionales";

    private static readonly AnalysisParamDefinition Alpha = ExtendedHelpers.Alpha();

    private static readonly AnalysisDefinition Variances = new(
        "variance-equality-tests",
        "Igualdad de varianzas (Levene, Bartlett, Fligner)",
        Category,
        "Cuatro pruebas de homogeneidad de varianzas para 2 o más grupos, con intervalos de Bonferroni para cada desviación.",
        [],
        [],
        [Alpha],
        RunVariances,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Varianzas",
        Roles:
        [
            new ColumnRole("var", "Variable", ColumnKinds.Numeric),
            new ColumnRole("group", "Grupos", ColumnKinds.Categorical)
        ]);

    private static readonly AnalysisDefinition Mood = new(
        "mood-median",
        "Mediana de Mood (k grupos)",
        Category,
        "Compara las medianas de varios grupos con una tabla de datos por encima y por debajo de la mediana global.",
        [],
        [],
        [Alpha],
        RunMood,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "No paramétricas y datos pareados",
        Roles:
        [
            new ColumnRole("var", "Variable", ColumnKinds.Numeric),
            new ColumnRole("group", "Grupos", ColumnKinds.Categorical)
        ]);

    private static readonly AnalysisDefinition Friedman = new(
        "friedman",
        "Friedman (bloques)",
        Category,
        "Compara 3 o más tratamientos medidos en los mismos sujetos, con rangos; alternativa al ANOVA de medidas repetidas.",
        [],
        [],
        [Alpha],
        RunFriedman,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "No paramétricas y datos pareados",
        Roles:
        [
            new ColumnRole("vars", "Tratamientos (columnas)", ColumnKinds.Numeric, Multiple: true, MinCount: 3)
        ]);

    private static readonly Lazy<IReadOnlyList<AnalysisDefinition>> LazyAll =
        new(() => [Variances, Mood, Friedman], isThreadSafe: true);

    public static IReadOnlyList<AnalysisDefinition> All => LazyAll.Value;

    private static Result<AnalysisResult> RunVariances(AnalysisInput input)
    {
        var frame = ExtendedHelpers.FrameOf(input);
        var name = ExtendedHelpers.FirstRole(input, "var", "Variable");
        var group = ExtendedHelpers.FirstRole(input, "group", "Grupos");
        var parts = ExtendedHelpers.Groups(frame, name, group);
        if (parts.Count < 2) return ExtendedHelpers.Invalid("Se necesitan al menos 2 grupos con datos.");
        foreach (var (label, values) in parts)
        {
            if (values.Length < 2) return ExtendedHelpers.Invalid($"El grupo «{label}» necesita al menos 2 datos.");
        }

        var samples = parts.Select(part => part.Values).ToArray();
        var k = samples.Length;
        var n = samples.Sum(values => values.Length);
        var alpha = input.Pnum("alpha", 0.05);

        var bartlett = VarianceEquality.BartlettTest(samples);
        if (bartlett.IsError) return Result.Err<AnalysisResult>(bartlett.Error.Code, bartlett.Error.Message);
        var leveneMean = ExtendedHelpers.Levene(samples, true);
        var brown = ExtendedHelpers.Levene(samples, false);
        var fligner = ExtendedHelpers.Fligner(samples);

        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "Bartlett (supone normalidad)", ExtendedHelpers.N(bartlett.Value.Statistic), JsNumber.ToString(k - 1), ExtendedHelpers.P(bartlett.Value.PValue) },
            new[] { "Levene (centrada en la media)", ExtendedHelpers.N(leveneMean.Statistic), $"{k - 1}, {n - k}", ExtendedHelpers.P(leveneMean.PValue) },
            new[] { "Brown-Forsythe / Levene (mediana)", ExtendedHelpers.N(brown.Statistic), $"{k - 1}, {n - k}", ExtendedHelpers.P(brown.PValue) },
            new[] { "Fligner-Killeen (robusta)", ExtendedHelpers.N(fligner.Statistic), JsNumber.ToString(k - 1), ExtendedHelpers.P(fligner.PValue) }
        };

        var variances = samples.Select(values => NumericMath.Variance(values)).ToArray();
        var ratio = variances.Max() / variances.Min();

        var adjusted = alpha / k;
        var groups = new List<IntervalPoint>();
        var groupRows = new List<IReadOnlyList<string>>();
        for (var index = 0; index < k; index++)
        {
            var values = samples[index];
            var df = values.Length - 1;
            var sd = NumericMath.Sd(values);
            var low = sd * Math.Sqrt(df / ExtendedStatistics.ChiSquareInv(1 - adjusted / 2, df));
            var high = sd * Math.Sqrt(df / ExtendedStatistics.ChiSquareInv(adjusted / 2, df));
            groups.Add(new IntervalPoint(parts[index].Label, sd, low, high));
            groupRows.Add(new[]
            {
                parts[index].Label,
                JsNumber.ToString(values.Length),
                ExtendedHelpers.N(variances[index]),
                ExtendedHelpers.N(sd),
                ExtendedHelpers.N(low),
                ExtendedHelpers.N(high)
            });
        }

        return Result.Ok(new AnalysisResult(
            "variance-equality-tests",
            $"Igualdad de varianzas de {name}",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Grupos", JsNumber.ToString(k)),
                    new TileItem("n total", JsNumber.ToString(n)),
                    new TileItem("Razón máx/mín de varianzas", ExtendedHelpers.N(ratio))
                ]),
                new TableSection(
                    "Pruebas de homogeneidad de varianzas",
                    ["Prueba", "Estadístico", "gl", "Valor p"],
                    rows,
                    Note: "Con datos no normales prefiera Brown-Forsythe o Fligner-Killeen."),
                SigmaText.Decision(brown.PValue, alpha, "todas las varianzas son iguales (Brown-Forsythe)"),
                new TableSection(
                    "Desviaciones estándar por grupo",
                    ["Grupo", "n", "Varianza", "Desv. est.", "IC inf.", "IC sup."],
                    groupRows)
            ],
            Subtitle: $"{k} grupos de «{group}»",
            Charts:
            [
                new IntervalChart(
                    $"IC de Bonferroni del {SigmaText.General(100 * (1 - alpha))} % para las desviaciones estándar",
                    groups,
                    "Desviación estándar")
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["levene_p"] = leveneMean.PValue,
                ["brown_forsythe_p"] = brown.PValue,
                ["bartlett_p"] = bartlett.Value.PValue,
                ["fligner_p"] = fligner.PValue
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunMood(AnalysisInput input)
    {
        var frame = ExtendedHelpers.FrameOf(input);
        var name = ExtendedHelpers.FirstRole(input, "var", "Variable");
        var group = ExtendedHelpers.FirstRole(input, "group", "Grupos");
        var parts = ExtendedHelpers.Groups(frame, name, group);
        if (parts.Count < 2) return ExtendedHelpers.Invalid("Se necesitan al menos 2 grupos con datos.");

        var pooled = parts.SelectMany(part => part.Values).ToArray();
        var grand = NumericMath.Median(pooled);
        var above = new int[parts.Count];
        var below = new int[parts.Count];
        var medians = new double[parts.Count];
        var sizes = new int[parts.Count];
        for (var index = 0; index < parts.Count; index++)
        {
            var values = parts[index].Values;
            medians[index] = NumericMath.Median(values);
            sizes[index] = values.Length;
            foreach (var value in values)
            {
                if (value > grand) above[index]++;
                else below[index]++;
            }
        }

        var table = new double[2][];
        table[0] = above.Select(value => (double)value).ToArray();
        table[1] = below.Select(value => (double)value).ToArray();
        var rowTotals = new[] { above.Sum(), below.Sum() };
        var colTotals = sizes.Select(value => (double)value).ToArray();
        var total = (double)sizes.Sum();
        var statistic = 0.0;
        for (var row = 0; row < 2; row++)
        {
            for (var column = 0; column < parts.Count; column++)
            {
                var expected = rowTotals[row] * colTotals[column] / total;
                if (expected <= 0) continue;
                var observed = table[row][column];
                var correction = parts.Count == 2 ? 0.5 : 0.0;
                var delta = Math.Abs(observed - expected) - correction;
                statistic += delta * delta / expected;
            }
        }
        var df = parts.Count - 1.0;
        var pValue = ExtendedStatistics.ChiSquareSf(statistic, df);
        var alpha = input.Pnum("alpha", 0.05);

        var rows = new List<IReadOnlyList<string>>();
        for (var index = 0; index < parts.Count; index++)
        {
            rows.Add([
                parts[index].Label,
                JsNumber.ToString(sizes[index]),
                ExtendedHelpers.N(medians[index]),
                JsNumber.ToString(above[index]),
                JsNumber.ToString(below[index])
            ]);
        }

        return Result.Ok(new AnalysisResult(
            "mood-median",
            $"Prueba de la mediana de Mood de {name}",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Mediana global", ExtendedHelpers.N(grand)),
                    new TileItem("χ²", ExtendedHelpers.N(statistic)),
                    new TileItem("Valor p", ExtendedHelpers.P(pValue))
                ]),
                SigmaText.Decision(pValue, alpha, "todos los grupos tienen la misma mediana"),
                new TableSection(
                    "Datos por encima y por debajo de la mediana global",
                    ["Grupo", "n", "Mediana", "> mediana global", "≤ mediana global"],
                    rows)
            ],
            Subtitle: $"{parts.Count} grupos de «{group}»",
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["statistic"] = statistic,
                ["p"] = pValue,
                ["grand_median"] = grand
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunFriedman(AnalysisInput input)
    {
        var frame = ExtendedHelpers.FrameOf(input);
        var names = input.RoleNames("vars", true);
        if (names.Count < 3) return ExtendedHelpers.Invalid("Se necesitan al menos 3 tratamientos (columnas).");
        var missing = names.Where(name => !frame.Has(name)).ToArray();
        if (missing.Length > 0) return ExtendedHelpers.Invalid($"La columna «{missing[0]}» no existe en el dataset.");

        var complete = frame.CompleteCases(names, out var dropped);
        if (complete.RowCount < 3) return ExtendedHelpers.Invalid("Se necesitan al menos 3 filas completas (bloques).");

        var k = names.Count;
        var n = complete.RowCount;
        var rankSums = new double[k];
        var rankValues = new double[n][];
        var ties = 0.0;
        for (var row = 0; row < n; row++)
        {
            var values = new double[k];
            for (var column = 0; column < k; column++) values[column] = complete.Numeric(names[column])[row];
            var ranks = NumericMath.Ranks(values);
            rankValues[row] = ranks;
            for (var column = 0; column < k; column++) rankSums[column] += ranks[column];
            // Corrección de empates de scipy: t*(t^2-1) por cada grupo de valores repetidos.
            var ordered = values.OrderBy(value => value).ToArray();
            var start = 0;
            while (start < k)
            {
                var end = start;
                while (end + 1 < k && ordered[end + 1] == ordered[start]) end++;
                var t = end - start + 1.0;
                if (t > 1) ties += t * (t * t - 1);
                start = end + 1;
            }
        }

        var ssbn = rankSums.Sum(value => value * value);
        var correction = 1 - ties / (k * (k * (double)k - 1) * n);
        var statistic = (12.0 / (k * n * (k + 1.0)) * ssbn - 3 * n * (k + 1.0)) / correction;
        var pValue = ExtendedStatistics.ChiSquareSf(statistic, k - 1.0);
        var kendall = statistic / (n * (k - 1.0));
        var alpha = input.Pnum("alpha", 0.05);

        var rows = new List<IReadOnlyList<string>>();
        for (var column = 0; column < k; column++)
        {
            var values = complete.Numeric(names[column]);
            var meanRank = rankValues.Sum(row => row[column]) / n;
            rows.Add([
                names[column],
                ExtendedHelpers.N(NumericMath.Median(values)),
                ExtendedHelpers.N(meanRank),
                ExtendedHelpers.N(rankSums[column])
            ]);
        }

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Bloques", JsNumber.ToString(n)),
                new TileItem("Tratamientos", JsNumber.ToString(k)),
                new TileItem("χ²", ExtendedHelpers.N(statistic)),
                new TileItem("Valor p", ExtendedHelpers.P(pValue)),
                new TileItem("W de Kendall", ExtendedHelpers.N(kendall))
            ]),
            SigmaText.Decision(pValue, alpha, "los tratamientos no difieren"),
            new TableSection(
                "Rangos medios por tratamiento",
                ["Tratamiento", "Mediana", "Rango medio", "Suma de rangos"],
                rows,
                Note: "W de Kendall = concordancia entre bloques (0 a 1).")
        };
        if (dropped > 0)
        {
            sections.Add(new AlertSection("Aviso", "info", $"Se omitieron {dropped} fila(s) con datos faltantes."));
        }

        return Result.Ok(new AnalysisResult(
            "friedman",
            "Prueba de Friedman",
            sections,
            Subtitle: $"{k} tratamientos en {n} bloques (filas completas)",
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["statistic"] = statistic,
                ["p"] = pValue,
                ["kendall_w"] = kendall
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
