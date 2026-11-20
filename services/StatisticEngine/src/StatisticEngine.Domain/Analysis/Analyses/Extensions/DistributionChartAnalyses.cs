using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Histograma y diagrama de caja/violín con estadísticos por grupo.</summary>
internal static class DistributionChartAnalyses
{
    public static readonly AnalysisDefinition Histogram = new(
        "histogram",
        "Histograma",
        "Distribución",
        "Distribución de una variable con curva normal o de densidad, por grupos superpuestos o en paneles.",
        [],
        [],
        [
            new("bins", "Número de intervalos (vacío = automático)", AnalysisParamKinds.Integer, null, [], true, Minimum: 2, Maximum: 200),
            new(
                "norm",
                "Eje vertical",
                AnalysisParamKinds.Select,
                "density",
                [new("density", "Densidad"), new("count", "Frecuencia"), new("percent", "Porcentaje")],
                false),
            new(
                "curve",
                "Curva",
                AnalysisParamKinds.Select,
                "normal",
                [new("none", "Ninguna"), new("normal", "Normal ajustada"), new("kde", "Densidad (KDE)")],
                false),
            new(
                "layout",
                "Varios grupos",
                AnalysisParamKinds.Select,
                "overlay",
                [new("overlay", "En un solo gráfico"), new("panels", "Un panel por grupo")],
                false)
        ],
        RunHistogram,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Distribución",
        Roles:
        [
            new ColumnRole("var", "Variable", ColumnKinds.Numeric),
            new ColumnRole("group", "Grupos (opcional)", ColumnKinds.Categorical, Optional: true)
        ]);

    public static readonly AnalysisDefinition Boxplot = new(
        "boxplot",
        "Diagrama de caja, violín y valores individuales",
        "Distribución",
        "Compara la distribución de una o varias variables (o de una variable por grupos) con cajas, violines o puntos.",
        [],
        [],
        [
            new(
                "style",
                "Estilo",
                AnalysisParamKinds.Select,
                "box",
                [
                    new("box", "Diagrama de caja"), new("box_points", "Caja con todos los puntos"),
                    new("violin", "Violín"), new("strip", "Valores individuales (puntos)")
                ],
                false)
        ],
        RunBoxplot,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Distribución",
        Roles:
        [
            new ColumnRole("vars", "Variable(s)", ColumnKinds.Numeric, Multiple: true),
            new ColumnRole("group", "Grupos (opcional, con una sola variable)", ColumnKinds.Categorical, Optional: true)
        ]);

    private static Result<AnalysisResult> RunHistogram(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var variable = ExtendedHelpers.FirstRole(input, "var", "Variable");
        var group = ExtendedHelpers.FirstRole(input, "group", "Grupos");
        var names = new List<string> { variable };
        if (group.Length > 0) names.Add(group);
        var complete = frame.CompleteCases(names);
        var values = complete.Numeric(variable);
        if (values.Length < 3) return ExtendedHelpers.Invalid("Se necesitan al menos 3 valores.");
        var labels = group.Length > 0 ? complete.Texts(group).Select(value => value ?? "Todos").ToArray() : Enumerable.Repeat("Todos", complete.RowCount).ToArray();
        var levels = group.Length > 0 ? complete.Levels(group) : ["Todos"];
        var rows = levels.Select(level =>
        {
            var selected = Enumerable.Range(0, complete.RowCount).Where(index => labels[index] == level).Select(index => values[index]).ToArray();
            var stats = Statistics(selected);
            return (IReadOnlyList<string>)new[]
            {
                level, JsNumber.ToString(stats.N), ModelHelpers.N(stats.Mean), ModelHelpers.N(stats.Sd),
                ModelHelpers.N(stats.Min), ModelHelpers.N(stats.Q1), ModelHelpers.N(stats.Median),
                ModelHelpers.N(stats.Q3), ModelHelpers.N(stats.Max), ModelHelpers.N(stats.Skew),
                ModelHelpers.N(stats.Kurtosis), ModelHelpers.P(stats.Shapiro)
            };
        }).ToArray();
        var overall = Statistics(values);
        var points = values.OrderBy(value => value).Select((value, index) => new ScatterPoint(index + 1, value)).ToList();
        return Result.Ok(new AnalysisResult(
            "histogram",
            $"Histograma de {variable}",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Observaciones", JsNumber.ToString(values.Length)),
                    new TileItem("Media", ModelHelpers.N(overall.Mean)),
                    new TileItem("Desv. estándar", ModelHelpers.N(overall.Sd)),
                    new TileItem("Asimetría", ModelHelpers.N(overall.Skew))
                ]),
                new TableSection(
                    "Estadísticos",
                    ["Grupo", "n", "Media", "Desv. est.", "Mínimo", "Q1", "Mediana", "Q3", "Máximo", "Asimetría", "Curtosis", "p Shapiro-Wilk"],
                    rows,
                    Note: "Curtosis en exceso (0 = normal). Shapiro-Wilk con hasta 5 000 datos.")
            ],
            Subtitle: group.Length > 0 ? $"por {group}" : "sin grupos",
            Charts: [new ScatterChart("Histograma", points, variable, "Densidad")],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = values.Length,
                ["mean"] = overall.Mean,
                ["sd"] = overall.Sd,
                ["skew"] = overall.Skew
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunBoxplot(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var variables = input.RoleNames("vars", true);
        var group = ExtendedHelpers.FirstRole(input, "group", "Grupos");
        if (variables.Count > 1 && group.Length > 0)
        {
            return ExtendedHelpers.Invalid("Con grupos solo se admite una variable.");
        }
        var names = new List<string>(variables);
        if (group.Length > 0) names.Add(group);
        var complete = frame.CompleteCases(names);
        var levels = group.Length > 0 ? complete.Levels(group) : ["Todos"];
        var labels = group.Length > 0 ? complete.Texts(group).Select(value => value ?? "Todos").ToArray() : Enumerable.Repeat("Todos", complete.RowCount).ToArray();
        var rows = new List<IReadOnlyList<string>>();
        var medians = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var level in levels)
        {
            var selected = Enumerable.Range(0, complete.RowCount).Where(index => labels[index] == level)
                .Select(index => complete.Numeric(variables[0])[index]).ToArray();
            if (selected.Length == 0) continue;
            var stats = Statistics(selected);
            rows.Add([
                level, JsNumber.ToString(stats.N), ModelHelpers.N(stats.Mean), ModelHelpers.N(stats.Sd),
                ModelHelpers.N(stats.Min), ModelHelpers.N(stats.Q1), ModelHelpers.N(stats.Median),
                ModelHelpers.N(stats.Q3), ModelHelpers.N(stats.Max), JsNumber.ToString(stats.Outliers)
            ]);
            medians[level] = stats.Median;
        }
        var style = input.Pstr("style", "box");
        var styleLabel = style switch
        {
            "violin" => "Gráfico de violín",
            "strip" => "Valores individuales",
            _ => "Diagrama de caja"
        };
        var all = complete.Numeric(variables[0]);
        var points = all.OrderBy(value => value).Select((value, index) => new ScatterPoint(index + 1, value)).ToList();
        return Result.Ok(new AnalysisResult(
            "boxplot",
            $"{styleLabel}: {variables[0]}",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Grupos", JsNumber.ToString(levels.Length)),
                    new TileItem("Observaciones", JsNumber.ToString(complete.RowCount))
                ]),
                new TableSection(
                    "Estadísticos",
                    ["Grupo", "n", "Media", "Desv. est.", "Mínimo", "Q1", "Mediana", "Q3", "Máximo", "Atípicos (Tukey)"],
                    rows,
                    Note: "Atípicos: fuera de Q1 − 1.5·RIC o Q3 + 1.5·RIC.")
            ],
            Subtitle: group.Length > 0 ? $"por {group}" : "sin grupos",
            Charts: [new ScatterChart("Distribución", points, variables[0], "Valor")],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["groups"] = levels.Length,
                ["medians"] = medians
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private sealed record Stats(int N, double Mean, double Sd, double Min, double Q1, double Median, double Q3, double Max, double Skew, double Kurtosis, double Shapiro, int Outliers);

    private static Stats Statistics(double[] values)
    {
        Array.Sort(values);
        var n = values.Length;
        var mean = values.Average();
        var sd = NumericMath.Sd(values);
        var q1 = NumericMath.Quantile(values, 0.25);
        var median = NumericMath.Quantile(values, 0.5);
        var q3 = NumericMath.Quantile(values, 0.75);
        var iqr = q3 - q1;
        var low = q1 - 1.5 * iqr;
        var high = q3 + 1.5 * iqr;
        var outliers = values.Count(value => value < low || value > high);
        return new Stats(n, mean, sd, values[0], q1, median, q3, values[^1], PopulationSkew(values, mean), PopulationKurtosis(values, mean), ShapiroP(values), outliers);
    }

    private static double PopulationSkew(double[] values, double mean)
    {
        var n = values.Length;
        var m2 = values.Sum(value => Math.Pow(value - mean, 2)) / n;
        var m3 = values.Sum(value => Math.Pow(value - mean, 3)) / n;
        return m2 > 0 ? m3 / Math.Pow(m2, 1.5) : 0;
    }

    private static double PopulationKurtosis(double[] values, double mean)
    {
        var n = values.Length;
        var m2 = values.Sum(value => Math.Pow(value - mean, 2)) / n;
        var m4 = values.Sum(value => Math.Pow(value - mean, 4)) / n;
        return m2 > 0 ? m4 / (m2 * m2) - 3 : 0;
    }

    /// <summary>Valor p de Shapiro-Wilk (Royston 1995, AS R94) para n ≥ 12.</summary>
    private static double ShapiroP(double[] values)
    {
        var n = values.Length;
        if (n < 12 || n > 5000) return double.NaN;
        var m = new double[n];
        for (var index = 0; index < n; index++) m[index] = SpecialFunctions.NormalInv((index + 1 - 0.375) / (n + 0.25));
        var sumM = m.Sum(value => value * value);
        var c = m.Select(value => value / Math.Sqrt(sumM)).ToArray();
        var u = 1 / Math.Sqrt(n);
        double Polynomial(double value, double c1, double c2, double c3, double c4, double c5) =>
            value + c1 * u + c2 * u * u + c3 * Math.Pow(u, 3) + c4 * Math.Pow(u, 4) + c5 * Math.Pow(u, 5);
        var a = new double[n];
        a[n - 1] = Polynomial(c[n - 1], 0.221157, -0.147981, -2.071190, 4.434685, -2.706056);
        a[n - 2] = Polynomial(c[n - 2], 0.042981, -0.293762, -1.752461, 5.682633, -3.582633);
        a[0] = -a[n - 1];
        a[1] = -a[n - 2];
        var phi = (sumM - 2 * m[n - 1] * m[n - 1] - 2 * m[n - 2] * m[n - 2]) /
                  (1 - 2 * a[n - 1] * a[n - 1] - 2 * a[n - 2] * a[n - 2]);
        for (var index = 2; index < n - 2; index++) a[index] = m[index] / Math.Sqrt(phi);
        var mean = values.Average();
        var numerator = a.Select((coefficient, index) => coefficient * values[index]).Sum();
        var denominator = values.Sum(value => Math.Pow(value - mean, 2));
        if (denominator <= 0) return double.NaN;
        var w = numerator * numerator / denominator;
        var logN = Math.Log(n);
        var mu = -1.5861 - 0.31082 * logN - 0.083751 * logN * logN + 0.0038915 * Math.Pow(logN, 3);
        var sigma = Math.Exp(-0.4803 - 0.082676 * logN + 0.0030302 * logN * logN);
        var z = (Math.Log(1 - Math.Min(w, 1 - 1e-15)) - mu) / sigma;
        return 1 - SpecialFunctions.NormalCdf(z);
    }
}
