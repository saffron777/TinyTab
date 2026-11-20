using StatisticEngine.Domain.Analysis;
using StatisticEngine.Domain.Analysis.Analyses.Extensions;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Data.Operations;

/// <summary>Transformaciones de columnas: fórmulas, recodificación, intervalos, dummies, matemáticas, tiempo, fechas y texto.</summary>
public static class TransformOperations
{
    public static readonly AnalysisDefinition Compute = new(
        "compute",
        "Calcular variable (fórmula)",
        "Datos",
        "Crea una columna con una fórmula: aritmética, condiciones, texto, fechas, rezagos y funciones estadísticas.",
        [],
        [],
        [
            new("name", "Nombre de la columna nueva", AnalysisParamKinds.Text, "", [], false),
            new("expression", "Fórmula", AnalysisParamKinds.Code, "", [], false),
            new("overwrite", "Reemplazar si ya existe", AnalysisParamKinds.Boolean, false, [], false)
        ],
        RunCompute,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Crear y modificar variables",
        Outputs: [new AnalysisOutputSpec(RowOperations.ResultKey, "Datos calculados", Required: true)],
        NeedsDataFrame: true);

    public static readonly AnalysisDefinition Filter = new(
        "filter",
        "Filtrar filas",
        "Datos",
        "Conserva (o quita) las filas que cumplen una condición escrita como fórmula.",
        [],
        [],
        [
            new("condition", "Condición", AnalysisParamKinds.Code, "", [], false),
            new("action", "Qué hacer", AnalysisParamKinds.Select, "keep", [new("keep", "Conservar las filas que la cumplen"), new("drop", "Quitar las filas que la cumplen")], false)
        ],
        RunFilter,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Filas",
        Outputs: [new AnalysisOutputSpec(RowOperations.ResultKey, "Datos filtrados", Required: true)],
        NeedsDataFrame: true);

    public static readonly AnalysisDefinition Recode = new(
        "recode",
        "Recodificar valores",
        "Datos",
        "Cambia valores, rangos o faltantes de una columna por otros según reglas escritas una por línea.",
        [],
        [],
        [
            new("name", "Nombre de la columna nueva", AnalysisParamKinds.Text, "", [], false),
            new("rules", "Reglas", AnalysisParamKinds.Code, "", [], false),
            new("overwrite", "Reemplazar si ya existe", AnalysisParamKinds.Boolean, false, [], false)
        ],
        RunRecode,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Crear y modificar variables",
        Roles: [new ColumnRole("col", "Columna a recodificar", ColumnKinds.Any)],
        Outputs: [new AnalysisOutputSpec(RowOperations.ResultKey, "Datos recodificados", Required: true)],
        NeedsDataFrame: true);

    public static readonly AnalysisDefinition Bin = new(
        "bin",
        "Agrupar en intervalos (discretizar)",
        "Datos",
        "Convierte una columna numérica en categorías: intervalos de igual ancho, cuantiles o puntos de corte propios.",
        [],
        [],
        [
            new("name", "Nombre de la columna nueva", AnalysisParamKinds.Text, "", [], false),
            new("method", "Método", AnalysisParamKinds.Select, "width", [new("width", "Igual ancho"), new("quantile", "Cuantiles (igual cantidad de filas)"), new("custom", "Puntos de corte propios")], false),
            new("bins", "Número de intervalos", AnalysisParamKinds.Integer, 5.0, [], false, Minimum: 2, Maximum: 100),
            new("cuts", "Puntos de corte", AnalysisParamKinds.Text, "", [], false),
            new("labels", "Etiquetas", AnalysisParamKinds.Select, "interval", [new("interval", "El intervalo, p. ej. (10, 20]"), new("number", "El número del intervalo (1, 2, 3�?�)")], false),
            new("overwrite", "Reemplazar si ya existe", AnalysisParamKinds.Boolean, false, [], false)
        ],
        RunBin,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Crear y modificar variables",
        Roles: [new ColumnRole("col", "Columna numérica", ColumnKinds.Numeric)],
        Outputs: [new AnalysisOutputSpec(RowOperations.ResultKey, "Datos discretizados", Required: true)],
        NeedsDataFrame: true);

    public static readonly AnalysisDefinition Dummies = new(
        "dummies",
        "Variables ficticias (dummies)",
        "Datos",
        "Crea una columna 0/1 por cada categoría de las columnas elegidas (útil para modelos).",
        [],
        [],
        [
            new("drop_first", "Omitir la primera categoría (referencia)", AnalysisParamKinds.Boolean, true, [], false),
            new("keep_original", "Conservar las columnas originales", AnalysisParamKinds.Boolean, true, [], false),
            new("separator", "Separador del nombre", AnalysisParamKinds.Text, "_", [], false)
        ],
        RunDummies,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Crear y modificar variables",
        Roles: [new ColumnRole("cols", "Columnas categóricas", ColumnKinds.Categorical, Multiple: true)],
        Outputs: [new AnalysisOutputSpec(RowOperations.ResultKey, "Datos con dummies", Required: true)],
        NeedsDataFrame: true);

    public static readonly AnalysisDefinition MathTransform = new(
        "math",
        "Transformaciones matemáticas (log, �^s, Box-Cox�?�)",
        "Datos",
        "Aplica una función a columnas numéricas; Box-Cox y Yeo-Johnson estiman λ para reducir la asimetría.",
        [],
        [],
        [
            new("function", "Función", AnalysisParamKinds.Select, "log",
                [
                    new("log", "Logaritmo natural"), new("log10", "Logaritmo base 10"), new("log1p", "log(1 + x)"), new("sqrt", "Raíz cuadrada"),
                    new("cbrt", "Raíz cúbica"), new("square", "Cuadrado"), new("reciprocal", "Inverso 1/x"), new("exp", "Exponencial"),
                    new("abs", "Valor absoluto"), new("boxcox", "Box-Cox"), new("yeojohnson", "Yeo-Johnson")
                ], false),
            new("mode", "Resultado", AnalysisParamKinds.Select, "new", [new("new", "Añadir columnas nuevas"), new("replace", "Reemplazar las columnas originales")], false)
        ],
        RunMath,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Crear y modificar variables",
        Roles: [new ColumnRole("cols", "Columnas numéricas", ColumnKinds.Numeric, Multiple: true)],
        Outputs: [new AnalysisOutputSpec(RowOperations.ResultKey, "Datos transformados", Required: true)],
        NeedsDataFrame: true);

    public static readonly AnalysisDefinition TimeOps = new(
        "time-ops",
        "Rezagos, diferencias, tasas y acumulados",
        "Datos",
        "Rezagos y adelantos, diferencias, variaciones, acumulados y medias móviles, opcionalmente por grupo.",
        [],
        [],
        [
            new("operation", "Operación", AnalysisParamKinds.Select, "lag",
                [
                    new("lag", "Rezago"), new("lead", "Adelanto"), new("diff", "Diferencia"), new("pct_change", "Variación relativa"),
                    new("log_diff", "Diferencia de logaritmos"), new("cumsum", "Suma acumulada"), new("rolling_mean", "Media móvil"),
                    new("rolling_sum", "Suma móvil"), new("rolling_std", "Desviación estándar móvil"), new("ewma", "Media móvil exponencial")
                ], false),
            new("periods", "k (periodos o ventana)", AnalysisParamKinds.Integer, 1.0, [], false, Minimum: 1, Maximum: 10000),
            new("alpha", "α de la media exponencial", AnalysisParamKinds.Number, 0.3, [], false, Minimum: 0.001, Maximum: 1),
            new("mode", "Resultado", AnalysisParamKinds.Select, "new", [new("new", "Añadir columnas nuevas"), new("replace", "Reemplazar las columnas originales")], false)
        ],
        RunTimeOps,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Crear y modificar variables",
        Roles:
        [
            new ColumnRole("cols", "Columnas numéricas", ColumnKinds.Numeric, Multiple: true),
            new ColumnRole("time", "Ordenar por fecha (opcional)", ColumnKinds.DateTime, Optional: true),
            new ColumnRole("by", "Calcular por grupo (opcional)", ColumnKinds.Categorical, Optional: true)
        ],
        Outputs: [new AnalysisOutputSpec(RowOperations.ResultKey, "Datos transformados", Required: true)],
        NeedsDataFrame: true);

    public static readonly AnalysisDefinition DateParts = new(
        "date-parts",
        "Extraer partes de una fecha",
        "Datos",
        "Año, trimestre, mes, semana, día de la semana, fin de semana�?� como columnas nuevas.",
        [],
        [],
        [
            new("parts", "Partes", AnalysisParamKinds.Multiselect, new[] { "year", "month" },
                [
                    new("year", "Año"), new("quarter", "Trimestre"), new("month", "Mes (1-12)"), new("month_name", "Nombre del mes"),
                    new("week", "Semana ISO"), new("day", "Día del mes"), new("dayofyear", "Día del año"),
                    new("weekday", "Día de la semana (1 = lunes)"), new("is_weekend", "Fin de semana (1/0)"), new("hour", "Hora"), new("year_month", "Año-mes (texto AAAA-MM)")
                ], false)
        ],
        RunDateParts,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Crear y modificar variables",
        Roles: [new ColumnRole("col", "Columna de fecha", ColumnKinds.DateTime)],
        Outputs: [new AnalysisOutputSpec(RowOperations.ResultKey, "Datos con partes de fecha", Required: true)],
        NeedsDataFrame: true);

    public static readonly AnalysisDefinition TextOps = new(
        "text-ops",
        "Limpiar texto",
        "Datos",
        "Quita espacios, unifica mayúsculas y tildes en columnas de texto para que las categorías coincidan.",
        [],
        [],
        [
            new("operations", "Operaciones", AnalysisParamKinds.Multiselect, new[] { "trim", "collapse" },
                [
                    new("trim", "Quitar espacios al inicio y al final"), new("collapse", "Reducir espacios repetidos a uno"),
                    new("lower", "Minúsculas"), new("upper", "Mayúsculas"), new("title", "Primera letra de cada palabra en mayúscula"),
                    new("no_accents", "Quitar tildes y diéresis")
                ], false),
            new("mode", "Resultado", AnalysisParamKinds.Select, "new", [new("new", "Añadir columnas nuevas"), new("replace", "Reemplazar las columnas originales")], false)
        ],
        RunTextOps,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Crear y modificar variables",
        Roles: [new ColumnRole("cols", "Columnas de texto", ColumnKinds.Categorical, Multiple: true)],
        Outputs: [new AnalysisOutputSpec(RowOperations.ResultKey, "Datos con texto limpio", Required: true)],
        NeedsDataFrame: true);

    private static Result<AnalysisResult> RunCompute(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var name = input.Pstr("name", "").Trim();
        if (name.Length == 0) return ExtendedHelpers.Invalid("Indique el nombre de la columna nueva.");
        if (frame.Has(name) && !input.Pbool("overwrite")) return ExtendedHelpers.Invalid($"Ya existe la columna «{name}».");
        var expression = input.Pstr("expression", "");
        var values = new double[frame.RowCount];
        try
        {
            for (var row = 0; row < frame.RowCount; row++)
            {
                values[row] = FormulaEvaluator.Evaluate(expression, column => frame.Numeric(column)[row]);
            }
        }
        catch (Exception exception)
        {
            return ExtendedHelpers.Invalid("No se pudo evaluar la fórmula: " + exception.Message);
        }
        var after = frame.With(DataColumn.Numeric(name, values));
        return CleaningOperations.Build("compute", "Datos calculados", frame, after, [["Columna creada", name, expression]], $"Se creó la columna «{name}».");
    }

    private static Result<AnalysisResult> RunFilter(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var condition = input.Pstr("condition", "");
        var keep = new List<int>();
        try
        {
            for (var row = 0; row < frame.RowCount; row++)
            {
                var truth = FormulaEvaluator.Evaluate(condition, column => frame.Numeric(column)[row]) != 0;
                if ((truth && input.Pstr("action", "keep") == "keep") || (!truth && input.Pstr("action", "keep") == "drop")) keep.Add(row);
            }
        }
        catch (Exception exception)
        {
            return ExtendedHelpers.Invalid("No se pudo evaluar la condición: " + exception.Message);
        }
        var after = frame.SubsetRows(keep);
        return CleaningOperations.Build("filter", "Datos filtrados", frame, after, [["Condición", condition, $"{after.RowCount} filas"]], $"Se conservaron {after.RowCount} de {frame.RowCount} filas.");
    }

    private static Result<AnalysisResult> RunRecode(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var column = ExtendedHelpers.FirstRole(input, "col", "Columna a recodificar");
        var source = frame.Column(column);
        var name = input.Pstr("name", "").Trim();
        if (name.Length == 0) name = column + "_recod";
        if (frame.Has(name) && !input.Pbool("overwrite")) return ExtendedHelpers.Invalid($"Ya existe la columna «{name}».");
        var rules = ParseRules(input.Pstr("rules", ""));
        var values = source.Kind == DataColumnKind.Numeric ? new double[source.Count] : [];
        var texts = source.Kind == DataColumnKind.Text ? new string?[source.Count] : [];
        for (var row = 0; row < source.Count; row++)
        {
            if (source.Kind == DataColumnKind.Numeric)
            {
                var value = source.NumberAt(row);
                var replacement = rules.FirstOrDefault(rule => rule.Matches(value));
                values[row] = replacement is null ? value : replacement.Value;
            }
            else
            {
                var value = source.TextAt(row);
                var replacement = rules.FirstOrDefault(rule => string.Equals(rule.From, value, StringComparison.Ordinal));
                texts[row] = replacement is null ? value : JsNumber.ToString(replacement.Value);
            }
        }
        var after = frame.With(source.Kind == DataColumnKind.Numeric ? DataColumn.Numeric(name, values) : DataColumn.Text(name, texts));
        return CleaningOperations.Build("recode", "Datos recodificados", frame, after, [["Reglas aplicadas", column, JsNumber.ToString(rules.Count)]], $"Se recodificó «{column}» en «{name}».");
    }

    private static List<RecodeRule> ParseRules(string text)
    {
        var rules = new List<RecodeRule>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.Contains("=>", StringComparison.Ordinal) ? "=>" : line.Contains("->", StringComparison.Ordinal) ? "->" : line.Contains('=') ? "=" : ":";
            var parts = line.Split(separator, 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)) continue;
            var range = parts[0].Split("..", 2, StringSplitOptions.TrimEntries);
            if (range.Length == 2 && double.TryParse(range[0], out var low) && double.TryParse(range[1], out var high)) rules.Add(new RecodeRule(null, value, low, high));
            else rules.Add(new RecodeRule(parts[0], value, double.NaN, double.NaN));
        }
        return rules;
    }

    private sealed record RecodeRule(string? From, double Value, double Low, double High)
    {
        public bool Matches(double value) => !double.IsNaN(Low) && value >= Low && value <= High;
    }

    private static Result<AnalysisResult> RunBin(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var column = ExtendedHelpers.FirstRole(input, "col", "Columna numérica");
        var values = frame.Numeric(column);
        var name = input.Pstr("name", "").Trim();
        if (name.Length == 0) name = column + "_bin";
        if (frame.Has(name) && !input.Pbool("overwrite")) return ExtendedHelpers.Invalid($"Ya existe la columna «{name}».");
        var method = input.Pstr("method", "width");
        var bins = input.Pint("bins", 5);
        var cuts = method == "custom"
            ? input.Pstr("cuts", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(value => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).OrderBy(value => value).ToArray()
            : Enumerable.Range(0, bins + 1).Select(index => method == "quantile" ? NumericMath.Quantile(values.Where(value => !double.IsNaN(value)).ToArray(), index / (double)bins) : values.Where(value => !double.IsNaN(value)).Min() + (values.Where(value => !double.IsNaN(value)).Max() - values.Where(value => !double.IsNaN(value)).Min()) * index / bins).ToArray();
        var labels = input.Pstr("labels", "interval") == "number";
        var result = new string?[values.Length];
        for (var row = 0; row < values.Length; row++)
        {
            if (double.IsNaN(values[row])) continue;
            var index = Array.FindIndex(cuts, cut => values[row] <= cut);
            if (index < 0) index = cuts.Length - 1;
            result[row] = labels ? JsNumber.ToString(Math.Max(1, index)) : index == 0 ? $"(-�^z, {ModelHelpers.N(cuts[0])}]" : index >= cuts.Length - 1 ? $"({ModelHelpers.N(cuts[^2])}, �^z)" : $"({ModelHelpers.N(cuts[index - 1])}, {ModelHelpers.N(cuts[index])}]";
        }
        var after = frame.With(DataColumn.Text(name, result));
        return CleaningOperations.Build("bin", "Datos discretizados", frame, after, [["Columna creada", name, JsNumber.ToString(bins)]], $"Se agruparon los valores de «{column}».");
    }

    private static Result<AnalysisResult> RunDummies(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var columns = input.RoleNames("cols", true);
        var dropFirst = input.Pbool("drop_first");
        var keep = input.Pbool("keep_original");
        var separator = input.Pstr("separator", "_");
        var after = keep ? frame : new Frame(frame.Columns.Where(column => !columns.Contains(column.Name)).ToArray(), frame.RowCount);
        foreach (var column in columns)
        {
            var levels = frame.Levels(column);
            var texts = frame.Texts(column);
            for (var index = dropFirst ? 1 : 0; index < levels.Length; index++)
            {
                var level = levels[index];
                var values = Enumerable.Range(0, frame.RowCount).Select(row => string.Equals(texts[row], level, StringComparison.Ordinal) ? 1.0 : 0.0).ToArray();
                after = after.With(DataColumn.Numeric(column + separator + level, values));
            }
        }
        return CleaningOperations.Build("dummies", "Datos con dummies", frame, after, [["Columnas originales", JsNumber.ToString(columns.Count), keep ? "conservadas" : "eliminadas"]], "Se crearon las variables ficticias.");
    }

    private static Result<AnalysisResult> RunMath(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var columns = input.RoleNames("cols", true);
        var function = input.Pstr("function", "log");
        var mode = input.Pstr("mode", "new");
        var after = frame;
        foreach (var column in columns)
        {
            var values = frame.Numeric(column);
            var transformed = new double[values.Length];
            var lambda = 1.0;
            if (function == "boxcox") lambda = EstimateLambda(values.Where(value => value > 0).ToArray(), true);
            if (function == "yeojohnson") lambda = EstimateLambda(values, false);
            for (var row = 0; row < values.Length; row++)
            {
                var value = values[row];
                transformed[row] = double.IsNaN(value) ? double.NaN : function switch
                {
                    "log" => value > 0 ? Math.Log(value) : double.NaN,
                    "log10" => value > 0 ? Math.Log10(value) : double.NaN,
                    "log1p" => value > -1 ? Math.Log(1 + value) : double.NaN,
                    "sqrt" => value >= 0 ? Math.Sqrt(value) : double.NaN,
                    "cbrt" => Math.Cbrt(value),
                    "square" => value * value,
                    "reciprocal" => value == 0 ? double.NaN : 1 / value,
                    "exp" => Math.Exp(value),
                    "abs" => Math.Abs(value),
                    "boxcox" => value > 0 ? Math.Pow(value, lambda) - 1 : double.NaN,
                    _ => value > 0 ? (Math.Pow(value, lambda) - 1) / lambda : double.NaN
                };
            }
            var name = mode == "replace" ? column : column + "_" + function;
            after = after.With(DataColumn.Numeric(name, transformed));
        }
        return CleaningOperations.Build("math", "Datos transformados", frame, after, [["Función", function, JsNumber.ToString(columns.Count)]], "Se aplicó la transformación matemática.");
    }

    private static double EstimateLambda(double[] values, bool positiveOnly)
    {
        if (values.Length < 3) return 1;
        var best = 1.0;
        var bestScore = double.PositiveInfinity;
        for (var lambda = -2.0; lambda <= 2.0001; lambda += 0.05)
        {
            var transformed = values.Select(value => positiveOnly ? Math.Pow(value, lambda) : value > 0 ? Math.Pow(value, lambda) - 1 : -Math.Pow(-value, 2 - lambda) + 1).ToArray();
            if (transformed.Any(value => double.IsNaN(value))) continue;
            var mean = transformed.Average();
            var sd = NumericMath.Sd(transformed);
            if (sd <= 0) continue;
            var skew = transformed.Sum(value => Math.Pow((value - mean) / sd, 3)) / transformed.Length;
            var score = Math.Abs(skew);
            if (score < bestScore) { bestScore = score; best = lambda; }
        }
        return best;
    }

    private static Result<AnalysisResult> RunTimeOps(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var columns = input.RoleNames("cols", true);
        var time = ExtendedHelpers.FirstRole(input, "time", "Ordenar por fecha");
        var by = ExtendedHelpers.FirstRole(input, "by", "Calcular por grupo");
        var order = Enumerable.Range(0, frame.RowCount).ToArray();
        if (time.Length > 0) Array.Sort(order, (left, right) => frame.Dates(time)[left].GetValueOrDefault().CompareTo(frame.Dates(time)[right].GetValueOrDefault()));
        var operation = input.Pstr("operation", "lag");
        var periods = input.Pint("periods", 1);
        var alpha = input.Pnum("alpha", 0.3);
        var after = frame;
        foreach (var column in columns)
        {
            var source = frame.Numeric(column);
            var result = new double[frame.RowCount];
            var groups = by.Length > 0 ? frame.Texts(by) : Enumerable.Repeat(string.Empty, frame.RowCount).ToArray();
            foreach (var group in groups.Distinct())
            {
                var indices = order.Where(index => groups[index] == group).ToArray();
                for (var position = 0; position < indices.Length; position++)
                {
                    var row = indices[position];
                    result[row] = operation switch
                    {
                        "lag" => position >= periods ? source[indices[position - periods]] : double.NaN,
                        "lead" => position + periods < indices.Length ? source[indices[position + periods]] : double.NaN,
                        "diff" => position >= periods ? source[row] - source[indices[position - periods]] : double.NaN,
                        "pct_change" => position >= periods && source[indices[position - periods]] != 0 ? source[row] / source[indices[position - periods]] - 1 : double.NaN,
                        "log_diff" => position >= periods && source[row] > 0 && source[indices[position - periods]] > 0 ? Math.Log(source[row] / source[indices[position - periods]]) : double.NaN,
                        "cumsum" => indices.Take(position + 1).Sum(index => source[index]),
                        "rolling_mean" => indices.Skip(Math.Max(0, position - periods + 1)).Take(periods).Select(index => source[index]).Average(),
                        "rolling_sum" => indices.Skip(Math.Max(0, position - periods + 1)).Take(periods).Select(index => source[index]).Sum(),
                        "rolling_std" => NumericMath.Sd(indices.Skip(Math.Max(0, position - periods + 1)).Take(periods).Select(index => source[index]).ToArray()),
                        _ => position == 0 ? source[row] : alpha * source[row] + (1 - alpha) * result[indices[position - 1]]
                    };
                }
            }
            after = after.With(DataColumn.Numeric(column + "_" + operation, result));
        }
        return CleaningOperations.Build("time-ops", "Datos transformados", frame, after, [["Operación", operation, JsNumber.ToString(periods)]], "Se aplicó la operación temporal.");
    }

    private static Result<AnalysisResult> RunDateParts(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var column = ExtendedHelpers.FirstRole(input, "col", "Columna de fecha");
        var parts = input.Plist("parts");
        var dates = frame.Dates(column);
        var after = frame;
        foreach (var part in parts)
        {
            if (part == "year_month")
            {
                after = after.With(DataColumn.Text("year_month", dates.Select(date => date?.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)).ToArray()));
                continue;
            }
            if (part == "month_name")
            {
                after = after.With(DataColumn.Text("month_name", dates.Select(date => date?.ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture)).ToArray()));
                continue;
            }
            var values = dates.Select(date => part switch
            {
                "year" => (double?)date?.Year,
                "quarter" => date is null ? null : (date.Value.Month - 1) / 3 + 1,
                "month" => date?.Month,
                "week" => date is null ? null : System.Globalization.ISOWeek.GetWeekOfYear(date.Value),
                "day" => date?.Day,
                "dayofyear" => date?.DayOfYear,
                "weekday" => date is null ? null : (int)date.Value.DayOfWeek == 0 ? 7 : (int)date.Value.DayOfWeek,
                "is_weekend" => date is null ? null : date.Value.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 1 : 0,
                "hour" => date?.Hour,
                _ => null
            }).Select(value => value ?? double.NaN).ToArray();
            after = after.With(DataColumn.Numeric(part, values));
        }
        return CleaningOperations.Build("date-parts", "Datos con partes de fecha", frame, after, [["Partes", string.Join(", ", parts), JsNumber.ToString(parts.Count)]], "Se extrajeron las partes de la fecha.");
    }

    private static Result<AnalysisResult> RunTextOps(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var columns = input.RoleNames("cols", true);
        var operations = input.Plist("operations");
        var mode = input.Pstr("mode", "new");
        var after = frame;
        foreach (var column in columns)
        {
            var values = frame.Texts(column);
            var transformed = values.Select(value =>
            {
                if (value is null) return null;
                var result = value;
                if (operations.Contains("trim")) result = result.Trim();
                if (operations.Contains("collapse")) result = string.Join(" ", result.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                if (operations.Contains("lower")) result = result.ToLowerInvariant();
                if (operations.Contains("upper")) result = result.ToUpperInvariant();
                if (operations.Contains("title")) result = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(result.ToLowerInvariant());
                if (operations.Contains("no_accents"))
                {
                    result = result.Normalize(System.Text.NormalizationForm.FormD);
                    result = new string(result.Where(character => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).Normalize(System.Text.NormalizationForm.FormC);
                }
                return (string?)result;
            }).ToArray();
            after = after.With(DataColumn.Text(mode == "replace" ? column : column + "_limpio", transformed));
        }
        return CleaningOperations.Build("text-ops", "Datos con texto limpio", frame, after, [["Operaciones", string.Join(", ", operations), JsNumber.ToString(columns.Count)]], "Se limpiaron las columnas de texto.");
    }
}
