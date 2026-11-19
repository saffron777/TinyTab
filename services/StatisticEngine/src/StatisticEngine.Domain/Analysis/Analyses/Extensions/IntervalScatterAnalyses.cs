using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

internal static class IntervalScatterAnalyses
{
    public static readonly AnalysisDefinition Interval = new(
        "interval",
        "Gráfico de intervalos de confianza",
        "Categorías",
        "Media e intervalo de confianza de una variable en cada grupo, para ver de un vistazo qué grupos se diferencian.",
        [],
        [],
        [
            new("confidence", "Nivel de confianza (%)", AnalysisParamKinds.Number, 95.0, [], false, Minimum: 50, Maximum: 99.9),
            new("points", "Mostrar los datos individuales", AnalysisParamKinds.Boolean, true, [], false)
        ],
        RunInterval,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Categorías",
        Roles:
        [
            new ColumnRole("var", "Variable", ColumnKinds.Numeric),
            new ColumnRole("group", "Grupos", ColumnKinds.Categorical)
        ]);

    public static readonly AnalysisDefinition Scatter = new(
        "scatter",
        "Dispersión y burbujas (con ajuste)",
        "Relaciones",
        "Gráfico X–Y con grupos, tamaño de burbuja, recta, polinomio o suavizado LOWESS; correlaciones de Pearson y Spearman.",
        [],
        [],
        [
            new(
                "fit",
                "Ajuste",
                AnalysisParamKinds.Select,
                "linear",
                [
                    new("none", "Sin ajuste"),
                    new("linear", "Recta (mínimos cuadrados)"),
                    new("quadratic", "Polinomio de grado 2"),
                    new("cubic", "Polinomio de grado 3"),
                    new("lowess", "Suavizado LOWESS")
                ],
                false),
            new("smooth", "Suavizado LOWESS (fracción de datos)", AnalysisParamKinds.Number, 0.6, [], false, Minimum: 0.1, Maximum: 1, VisibleIf: new("fit", ["lowess"])),
            new("ci", "Banda de confianza 95 % (recta con un solo grupo)", AnalysisParamKinds.Boolean, true, [], false, VisibleIf: new("fit", ["linear"])),
            new(
                "layout",
                "Varios grupos",
                AnalysisParamKinds.Select,
                "overlay",
                [new("overlay", "En un solo gráfico"), new("panels", "Un panel por grupo")],
                false)
        ],
        RunScatter,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Relaciones",
        Roles:
        [
            new ColumnRole("x", "Eje X", ColumnKinds.Numeric),
            new ColumnRole("y", "Eje Y", ColumnKinds.Numeric),
            new ColumnRole("group", "Grupos (opcional)", ColumnKinds.Categorical, Optional: true),
            new ColumnRole("size", "Tamaño de burbuja (opcional)", ColumnKinds.Numeric, Optional: true)
        ]);

    private static Result<AnalysisResult> RunInterval(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var variable = ExtendedHelpers.FirstRole(input, "var", "Variable");
        var group = ExtendedHelpers.FirstRole(input, "group", "Grupos");
        var confidence = input.Pnum("confidence", 95);
        var parts = ExtendedHelpers.Groups(frame, variable, group).OrderBy(part => part.Label, StringComparer.Ordinal).ToArray();
        var rows = new List<IReadOnlyList<string>>();
        var points = new List<IntervalPoint>();
        var means = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            var n = part.Values.Length;
            var mean = part.Values.Average();
            var sd = NumericMath.Sd(part.Values);
            var se = sd / Math.Sqrt(n);
            var critical = InferenceDistributions.TQuantile(1 - (1 - confidence / 100) / 2, n - 1);
            var low = mean - critical * se;
            var high = mean + critical * se;
            rows.Add([part.Label, JsNumber.ToString(n), ModelHelpers.N(mean), ModelHelpers.N(sd), ModelHelpers.N(se), ModelHelpers.N(low), ModelHelpers.N(high)]);
            points.Add(new IntervalPoint(part.Label, mean, low, high));
            means[part.Label] = mean;
        }
        var overlapping = new List<string>();
        for (var i = 0; i < parts.Length - 1; i++)
        {
            for (var j = i + 1; j < parts.Length; j++)
            {
                if (points[i].Upper < points[j].Lower || points[j].Upper < points[i].Lower) overlapping.Add($"{parts[i].Label} y {parts[j].Label}");
            }
        }
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Grupos", JsNumber.ToString(parts.Length)),
                new TileItem("Confianza", $"{SigmaText.General(confidence)} %")
            ])
        };
        if (overlapping.Count > 0)
        {
            sections.Add(new AlertSection(
                "Aviso",
                "info",
                $"Intervalos que no se solapan (diferencia probable): {string.Join("; ", overlapping)}. Es solo una guía: use ANOVA para comparar con rigor."));
        }
        sections.Add(new TableSection(
            "Intervalos de confianza",
            ["Grupo", "n", "Media", "Desv. est.", "Error est.", "Límite inf.", "Límite sup."],
            rows));
        return Result.Ok(new AnalysisResult(
            "interval",
            $"Gráfico de intervalos de {variable}",
            sections,
            Subtitle: $"IC del {SigmaText.General(confidence)} % de la media, por {group}",
            Charts: [new IntervalChart("Medias e intervalos de confianza", points, "Media")],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal) { ["means"] = means },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunScatter(AnalysisInput input)
    {
        if (input.Pstr("fit", "linear") != "linear")
        {
            return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "Por ahora el ajuste de dispersión solo admite la recta de mínimos cuadrados.");
        }
        var frame = input.AsFrame();
        var xName = ExtendedHelpers.FirstRole(input, "x", "Eje X");
        var yName = ExtendedHelpers.FirstRole(input, "y", "Eje Y");
        var group = ExtendedHelpers.FirstRole(input, "group", "Grupos");
        var names = new List<string> { xName, yName };
        if (!string.IsNullOrEmpty(group)) names.Add(group);
        var complete = frame.CompleteCases(names);
        var n = complete.RowCount;
        var rows = new List<IReadOnlyList<string>>();
        var series = new List<XYSeries>();
        var groupLabels = string.IsNullOrEmpty(group) ? [string.Empty] : complete.Texts(group).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var xAll = complete.Numeric(xName);
        var yAll = complete.Numeric(yName);
        var groupValues = string.IsNullOrEmpty(group) ? Enumerable.Repeat(string.Empty, n).ToArray() : complete.Texts(group).Select(value => value ?? string.Empty).ToArray();
        foreach (var level in groupLabels)
        {
            var indices = Enumerable.Range(0, n).Where(index => groupValues[index] == level).ToArray();
            var x = indices.Select(index => xAll[index]).ToArray();
            var y = indices.Select(index => yAll[index]).ToArray();
            var pearson = Correlation(x, y);
            var t = Math.Abs(pearson) < 1 ? pearson * Math.Sqrt((x.Length - 2.0) / (1 - pearson * pearson)) : double.PositiveInfinity;
            var pValue = 2 * (1 - InferenceDistributions.TCdf(Math.Abs(t), x.Length - 2));
            var spearman = Correlation(Ranks(x), Ranks(y));
            var fit = LinearModel.Fit(x.Select(value => new[] { 1.0, value }).ToArray(), y);
            var slope = fit.IsOk ? fit.Value.Coefficients[1] : double.NaN;
            var intercept = fit.IsOk ? fit.Value.Coefficients[0] : double.NaN;
            var fitText = $"y = {slope.ToString("G4", System.Globalization.CultureInfo.InvariantCulture)}x + {intercept.ToString("G4", System.Globalization.CultureInfo.InvariantCulture)}";
            rows.Add([
                string.IsNullOrEmpty(level) ? "Todas" : level,
                JsNumber.ToString(x.Length),
                ModelHelpers.N(pearson),
                ModelHelpers.P(pValue),
                ModelHelpers.N(spearman),
                fitText,
                ModelHelpers.N(fit.IsOk ? fit.Value.R2 : double.NaN)
            ]);
            series.Add(new XYSeries(string.IsNullOrEmpty(level) ? "Datos" : level, x, y));
        }
        return Result.Ok(new AnalysisResult(
            "scatter",
            $"Dispersión de {yName} según {xName}",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Observaciones", JsNumber.ToString(n)),
                    new TileItem("Grupos", JsNumber.ToString(groupLabels.Length))
                ]),
                new TableSection(
                    "Asociación",
                    ["Serie", "n", "r de Pearson", "p", "ρ de Spearman", "Ajuste", "R²"],
                    rows)
            ],
            Subtitle: string.IsNullOrEmpty(group) ? "sin grupos" : $"por {group}",
            Charts: [new SeriesChart("Dispersión y ajuste", series, xName, yName)],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["pearson"] = string.IsNullOrEmpty(group) ? Correlation(xAll, yAll) : null
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

    private static double[] Ranks(double[] values)
    {
        var order = Enumerable.Range(0, values.Length).OrderBy(index => values[index]).ToArray();
        var ranks = new double[values.Length];
        for (var position = 0; position < order.Length;)
        {
            var end = position + 1;
            while (end < order.Length && values[order[end]] == values[order[position]]) end++;
            var average = (position + end - 1) / 2.0 + 1;
            for (var index = position; index < end; index++) ranks[order[index]] = average;
            position = end;
        }
        return ranks;
    }
}
