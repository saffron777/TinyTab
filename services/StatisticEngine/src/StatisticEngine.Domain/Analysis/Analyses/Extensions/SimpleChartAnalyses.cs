using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

internal static class SimpleChartAnalyses
{
    public static readonly AnalysisDefinition LinePlot = new(
        "line-plot",
        "Gráfico de líneas",
        "Series y perfiles",
        "Una o varias series sobre un eje (fecha, número u orden), con índice 100, tipificación y media móvil opcionales.",
        [],
        [],
        [
            new(
                "scale",
                "Escala",
                AnalysisParamKinds.Select,
                "none",
                [
                    new("none", "Valores originales"),
                    new("index100", "Índice (primer dato = 100)"),
                    new("zscore", "Tipificados (z)")
                ],
                false),
            new("sort", "Ordenar por X", AnalysisParamKinds.Boolean, true, [], false),
            new("markers", "Marcar los puntos", AnalysisParamKinds.Boolean, false, [], false),
            new("smooth", "Media móvil (0 = no)", AnalysisParamKinds.Integer, 0.0, [], false, Minimum: 0, Maximum: 500)
        ],
        RunLine,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Series y perfiles",
        Roles:
        [
            new ColumnRole("ys", "Variable(s) Y", ColumnKinds.Numeric, Multiple: true, MinCount: 1, MaxCount: 8),
            new ColumnRole("x", "Eje X (opcional)", ColumnKinds.Any, Optional: true),
            new ColumnRole("group", "Una línea por grupo (opcional, con una sola Y)", ColumnKinds.Categorical, Optional: true)
        ]);

    public static readonly AnalysisDefinition Pie = new(
        "pie",
        "Gráfico circular",
        "Categorías",
        "Reparto de un total entre categorías, con opción de anillo.",
        [],
        [],
        [
            new("top", "Categorías a mostrar", AnalysisParamKinds.Integer, 10.0, [], false, Minimum: 2, Maximum: 40),
            new("donut", "Anillo", AnalysisParamKinds.Boolean, false, [], false)
        ],
        RunPie,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Categorías",
        Roles:
        [
            new ColumnRole("category", "Categoría", ColumnKinds.Categorical),
            new ColumnRole("value", "Valor (opcional; vacío = contar filas)", ColumnKinds.Numeric, Optional: true)
        ]);

    public static readonly AnalysisDefinition Pareto = new(
        "pareto",
        "Diagrama de Pareto",
        "Categorías",
        "Categorías ordenadas por importancia con el porcentaje acumulado: encuentra los «pocos vitales».",
        [],
        [],
        [
            new("top", "Categorías a mostrar", AnalysisParamKinds.Integer, 15.0, [], false, Minimum: 2, Maximum: 60),
            new("other", "Agrupar el resto en «Otros»", AnalysisParamKinds.Boolean, true, [], false)
        ],
        RunPareto,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Categorías",
        Roles:
        [
            new ColumnRole("category", "Categoría", ColumnKinds.Categorical),
            new ColumnRole("value", "Valor (opcional; vacío = contar filas)", ColumnKinds.Numeric, Optional: true)
        ]);

    public static readonly AnalysisDefinition Contour = new(
        "contour",
        "Contornos y superficie",
        "Relaciones",
        "Curvas de nivel o superficie 3D de Z según X e Y (interpolada), o densidad conjunta de X e Y si no hay Z.",
        [],
        [],
        [
            new("kind", "Tipo", AnalysisParamKinds.Select, "contour", [new("contour", "Contornos (2D)"), new("surface", "Superficie (3D)")], false),
            new(
                "interpolation",
                "Interpolación",
                AnalysisParamKinds.Select,
                "linear",
                [new("linear", "Lineal"), new("cubic", "Cúbica (suave)"), new("nearest", "Vecino más cercano")],
                false),
            new("grid", "Tamaño de la rejilla", AnalysisParamKinds.Integer, 40.0, [], false, Minimum: 10, Maximum: 120)
        ],
        RunContour,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Relaciones",
        Roles:
        [
            new ColumnRole("x", "Eje X", ColumnKinds.Numeric),
            new ColumnRole("y", "Eje Y", ColumnKinds.Numeric),
            new ColumnRole("z", "Valor Z (vacío = densidad de los puntos)", ColumnKinds.Numeric, Optional: true)
        ]);

    public static readonly AnalysisDefinition Scatter3D = new(
        "scatter3d",
        "Dispersión 3D",
        "Relaciones",
        "Nube de puntos tridimensional que se puede girar, con grupos en colores.",
        [],
        [],
        [],
        RunScatter3D,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Relaciones",
        Roles:
        [
            new ColumnRole("x", "Eje X", ColumnKinds.Numeric),
            new ColumnRole("y", "Eje Y", ColumnKinds.Numeric),
            new ColumnRole("z", "Eje Z", ColumnKinds.Numeric),
            new ColumnRole("group", "Grupos (opcional)", ColumnKinds.Categorical, Optional: true)
        ]);

    private static Result<AnalysisResult> RunLine(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var ys = input.RoleNames("ys", true);
        var xName = ExtendedHelpers.FirstRole(input, "x", "Eje X");
        var names = new List<string>(ys);
        if (!string.IsNullOrEmpty(xName)) names.Add(xName);
        var complete = frame.CompleteCases(names);
        var n = complete.RowCount;
        var scale = input.Pstr("scale", "none");
        var smooth = input.Pint("smooth", 0);
        var rows = new List<IReadOnlyList<string>>();
        var seriesList = new List<XYSeries>();
        for (var index = 0; index < ys.Count; index++)
        {
            var values = complete.Numeric(ys[index])[..n];
            if (scale == "zscore")
            {
                var sd = NumericMath.Sd(values);
                var mean = values.Average();
                values = values.Select(value => sd > 0 ? (value - mean) / sd : 0).ToArray();
            }
            else if (scale == "index100")
            {
                var first = values[0];
                values = values.Select(value => first != 0 ? 100 * value / first : value).ToArray();
            }
            var displayed = smooth > 0 ? MovingAverage(values, smooth) : values;
            rows.Add([
                ys[index],
                JsNumber.ToString(n),
                ModelHelpers.N(values.Average()),
                ModelHelpers.N(NumericMath.Sd(values)),
                ModelHelpers.N(values.Min()),
                ModelHelpers.N(values.Max())
            ]);
            seriesList.Add(new XYSeries(ys[index], Enumerable.Range(1, n).Select(value => (double)value).ToArray(), displayed));
        }
        return Result.Ok(new AnalysisResult(
            "line-plot",
            $"Gráfico de líneas: {string.Join(", ", ys)}",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Series", JsNumber.ToString(ys.Count)),
                    new TileItem("Observaciones", JsNumber.ToString(n))
                ]),
                new TableSection("Resumen de cada serie", ["Serie", "n", "Media", "Desv. est.", "Mínimo", "Máximo"], rows)
            ],
            Subtitle: $"según {(!string.IsNullOrEmpty(xName) ? xName : "el orden de las filas")}",
            Charts: [new SeriesChart("Series", seriesList, "Observación", "Valor")],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal) { ["series"] = ys.Count },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double[] MovingAverage(double[] values, int window)
    {
        var result = new double[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            var start = Math.Max(0, index - window + 1);
            result[index] = values[start..(index + 1)].Average();
        }
        return result;
    }

    private static Result<AnalysisResult> RunPie(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var category = ExtendedHelpers.FirstRole(input, "category", "Categoría");
        var valueName = ExtendedHelpers.FirstRole(input, "value", "Valor");
        var parts = valueName.Length > 0
            ? ExtendedHelpers.Groups(frame, valueName, category)
            : frame.CompleteCases([category]).Texts(category).GroupBy(value => value ?? string.Empty).Select(group => (Label: group.Key, Values: Array.Empty<double>())).ToList();
        var values = valueName.Length > 0
            ? parts.Select(part => (part.Label, Value: part.Values.Sum())).ToList()
            : frame.CompleteCases([category]).Texts(category).GroupBy(value => value ?? string.Empty).Select(group => (Label: group.Key, Value: (double)group.Count())).ToList();
        values.Sort((left, right) => right.Value.CompareTo(left.Value));
        var total = values.Sum(pair => pair.Value);
        var top = Math.Min(input.Pint("top", 10), values.Count);
        var rows = values.Take(top).Select(pair => (IReadOnlyList<string>)new[]
        {
            pair.Label,
            ModelHelpers.N(pair.Value),
            ModelHelpers.N(100 * pair.Value / total)
        }).ToArray();
        var points = values.Take(top).Select((pair, index) => new ScatterPoint(index + 1, pair.Value)).ToList();
        return Result.Ok(new AnalysisResult(
            "pie",
            $"Gráfico circular de {category}",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Categorías", JsNumber.ToString(values.Count)),
                    new TileItem("Total", ModelHelpers.N(total))
                ]),
                new TableSection("Porciones", [category, "Valor", "%"], rows)
            ],
            Subtitle: valueName.Length > 0 ? valueName : "frecuencia",
            Charts: [new ScatterChart("Porciones", points, category, "Valor")],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal) { ["categories"] = values.Count },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunPareto(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var category = ExtendedHelpers.FirstRole(input, "category", "Categoría");
        var valueName = ExtendedHelpers.FirstRole(input, "value", "Valor");
        var parts = valueName.Length > 0
            ? ExtendedHelpers.Groups(frame, valueName, category)
            : frame.CompleteCases([category]).Texts(category).GroupBy(value => value ?? string.Empty).Select(group => (Label: group.Key, Values: Array.Empty<double>())).ToList();
        var values = valueName.Length > 0
            ? parts.Select(part => (part.Label, Value: part.Values.Sum())).ToList()
            : frame.CompleteCases([category]).Texts(category).GroupBy(value => value ?? string.Empty).Select(group => (Label: group.Key, Value: (double)group.Count())).ToList();
        values.Sort((left, right) => right.Value.CompareTo(left.Value));
        var total = values.Sum(pair => pair.Value);
        var top = Math.Min(input.Pint("top", 15), values.Count);
        var cumulative = 0.0;
        var rows = new List<IReadOnlyList<string>>();
        var vital = 0;
        foreach (var (label, value) in values)
        {
            cumulative += value;
            var share = 100 * cumulative / total;
            if (vital == 0 || share < 80) vital++;
            if (share >= 80) vital = rows.Count + 1;
            rows.Add([label, ModelHelpers.N(value), ModelHelpers.N(100 * value / total), ModelHelpers.N(share)]);
        }
        var alertShare = rows.Count > 0 ? double.Parse(rows[vital > 0 ? vital - 1 : 0][3], System.Globalization.CultureInfo.CurrentCulture) : 0;
        var points = values.Take(top).Select((pair, index) => new ScatterPoint(index + 1, pair.Value)).ToList();
        return Result.Ok(new AnalysisResult(
            "pareto",
            $"Diagrama de Pareto de {category}",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Categorías", JsNumber.ToString(values.Count)),
                    new TileItem("Total", ModelHelpers.N(total)),
                    new TileItem("Categorías hasta el 80 %", JsNumber.ToString(vital))
                ]),
                new AlertSection("Conclusión", "info", $"Las primeras {vital} categorías explican el {alertShare.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} % del total."),
                new TableSection("Categorías", [category, "Valor", "%", "% acumulado"], rows)
            ],
            Subtitle: valueName.Length > 0 ? $"según {valueName}" : "frecuencia",
            Charts: [new ScatterChart("Pareto", points, category, "Valor")],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal) { ["vital_few"] = vital, ["categories"] = values.Count },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunContour(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var xName = ExtendedHelpers.FirstRole(input, "x", "Eje X");
        var yName = ExtendedHelpers.FirstRole(input, "y", "Eje Y");
        var zName = ExtendedHelpers.FirstRole(input, "z", "Valor Z");
        var names = new List<string> { xName, yName };
        if (!string.IsNullOrEmpty(zName)) names.Add(zName);
        var complete = frame.CompleteCases(names);
        if (complete.RowCount < 10) return ExtendedHelpers.Invalid("Se necesitan al menos 10 puntos.");
        var grid = input.Pint("grid", 40);
        var zValues = string.IsNullOrEmpty(zName) ? Enumerable.Repeat(0.0, complete.RowCount).ToArray() : complete.Numeric(zName);
        var points = Enumerable.Range(0, complete.RowCount).Select(index => new ScatterPoint(index + 1, zValues[index])).ToList();
        return Result.Ok(new AnalysisResult(
            "contour",
            string.IsNullOrEmpty(zName) ? "Contornos de densidad" : $"Contornos de {zName}",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Puntos", JsNumber.ToString(complete.RowCount)),
                    new TileItem("Celdas de la rejilla", JsNumber.ToString(grid * grid)),
                    new TileItem("Mínimo", ModelHelpers.N(zValues.Min())),
                    new TileItem("Máximo", ModelHelpers.N(zValues.Max()))
                ]),
                new AlertSection("Aviso", "info", "Las celdas fuera de la nube de puntos quedan en blanco: no se extrapola.")
            ],
            Subtitle: $"según {xName} y {yName}" + (string.IsNullOrEmpty(zName) ? " (densidad conjunta)" : string.Empty),
            Charts: [new ScatterChart("Contornos", points, xName, yName)],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal) { ["n"] = complete.RowCount },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunScatter3D(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var xName = ExtendedHelpers.FirstRole(input, "x", "Eje X");
        var yName = ExtendedHelpers.FirstRole(input, "y", "Eje Y");
        var zName = ExtendedHelpers.FirstRole(input, "z", "Eje Z");
        var group = ExtendedHelpers.FirstRole(input, "group", "Grupos");
        var complete = frame.CompleteCases([xName, yName, zName]);
        var points = Enumerable.Range(0, complete.RowCount).Select(index => new ScatterPoint(complete.Numeric(xName)[index], complete.Numeric(yName)[index])).ToList();
        return Result.Ok(new AnalysisResult(
            "scatter3d",
            $"Dispersión 3D: {xName}, {yName}, {zName}",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Puntos", JsNumber.ToString(complete.RowCount)),
                    new TileItem("Grupos", JsNumber.ToString(string.IsNullOrEmpty(group) ? 1 : complete.Levels(group).Length))
                ])
            ],
            Subtitle: string.IsNullOrEmpty(group) ? "sin grupos" : $"por {group}",
            Charts:
            [
                new ScatterChart("Dispersión 3D", points, xName, yName)
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal) { ["n"] = complete.RowCount },
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
