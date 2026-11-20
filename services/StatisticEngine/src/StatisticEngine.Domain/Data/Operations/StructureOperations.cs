using StatisticEngine.Domain.Analysis;
using StatisticEngine.Domain.Analysis.Analyses.Extensions;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Data.Operations;

/// <summary>Muestras, particiones, agregación, remodelado y uniones de datasets.</summary>
public static class StructureOperations
{
    private static readonly double[] Intercept = [1.0];
    public static readonly AnalysisDefinition Sample = new(
        "sample",
        "Muestra aleatoria",
        "Datos",
        "Extrae una muestra de filas, simple o estratificada proporcionalmente, con semilla para repetirla.",
        [],
        [],
        [
            new("mode", "El tamaño se indica como", AnalysisParamKinds.Select, "n", [new("n", "Número de filas"), new("fraction", "Fracción de las filas")], false),
            new("size", "Tamaño", AnalysisParamKinds.Number, 100.0, [], false, Minimum: 0.0001),
            new("replace", "Con reemplazo (bootstrap)", AnalysisParamKinds.Boolean, false, [], false),
            new("seed", "Semilla", AnalysisParamKinds.Integer, 123.0, [], false, Minimum: 0, Maximum: 2147483647)
        ],
        RunSample,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Filas",
        Roles: [new ColumnRole("strata", "Estratos (opcional)", ColumnKinds.Categorical, Optional: true)],
        Outputs: [new AnalysisOutputSpec(RowOperations.ResultKey, "Muestra", Required: true)],
        NeedsDataFrame: true);

    public static readonly AnalysisDefinition Partition = new(
        "partition",
        "Partición entrenamiento / prueba",
        "Datos",
        "Divide el dataset en entrenamiento, prueba (y validación), al azar o por orden temporal.",
        [],
        [],
        [
            new("method", "Método", AnalysisParamKinds.Select, "random", [new("random", "Aleatoria"), new("sequential", "Secuencial")], false),
            new("test_fraction", "Fracción de prueba", AnalysisParamKinds.Number, 0.3, [], false, Minimum: 0.02, Maximum: 0.9),
            new("validation_fraction", "Fracción de validación (0 = sin validación)", AnalysisParamKinds.Number, 0.0, [], false, Minimum: 0, Maximum: 0.9),
            new("seed", "Semilla", AnalysisParamKinds.Integer, 123.0, [], false, Minimum: 0, Maximum: 2147483647)
        ],
        RunPartition,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Filas",
        Roles:
        [
            new ColumnRole("strata", "Estratos (opcional)", ColumnKinds.Categorical, Optional: true),
            new ColumnRole("time", "Ordenar por fecha (solo secuencial)", ColumnKinds.DateTime, Optional: true)
        ],
        Outputs: [new AnalysisOutputSpec(RowOperations.ResultKey, "Datos con partición", Required: true)],
        NeedsDataFrame: true);

    public static readonly AnalysisDefinition Aggregate = new(
        "aggregate",
        "Agregar por grupos (resumen)",
        "Datos",
        "Resume columnas numéricas por grupos y entrega una tabla con una fila por grupo.",
        [],
        [],
        [
            new("functions", "Funciones", AnalysisParamKinds.Multiselect, new[] { "count", "mean" },
                [
                    new("count", "Cantidad de datos"), new("mean", "Media"), new("median", "Mediana"), new("sum", "Suma"),
                    new("min", "Mínimo"), new("max", "Máximo"), new("std", "Desv. estándar"), new("var", "Varianza"),
                    new("q1", "Primer cuartil"), new("q3", "Tercer cuartil"), new("nunique", "Valores distintos"),
                    new("first", "Primer valor"), new("last", "�sltimo valor")
                ], false),
            new("include_n", "Añadir el tamaño del grupo (n)", AnalysisParamKinds.Boolean, true, [], false),
            new("dropna", "Omitir el grupo de valores faltantes", AnalysisParamKinds.Boolean, true, [], false)
        ],
        RunAggregate,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Estructura",
        Roles:
        [
            new ColumnRole("by", "Agrupar por", ColumnKinds.Any, Multiple: true),
            new ColumnRole("values", "Columnas a resumir", ColumnKinds.Numeric, Multiple: true)
        ],
        Outputs: [new AnalysisOutputSpec(RowOperations.ResultKey, "Resumen por grupos", Required: true)],
        NeedsDataFrame: true);

    public static readonly AnalysisDefinition Melt = new(
        "melt",
        "Apilar columnas (ancho �?' largo)",
        "Datos",
        "Convierte varias columnas de medidas en dos columnas (nombre y valor).",
        [],
        [],
        [
            new("var_name", "Nombre de la columna con los nombres", AnalysisParamKinds.Text, "variable", [], false),
            new("value_name", "Nombre de la columna con los valores", AnalysisParamKinds.Text, "valor", [], false)
        ],
        RunMelt,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Estructura",
        Roles:
        [
            new ColumnRole("ids", "Columnas de identificación", ColumnKinds.Any, Multiple: true, Optional: true),
            new ColumnRole("values", "Columnas a apilar", ColumnKinds.Any, Multiple: true, MinCount: 2)
        ],
        Outputs: [new AnalysisOutputSpec(RowOperations.ResultKey, "Datos apilados", Required: true)],
        NeedsDataFrame: true);

    public static readonly AnalysisDefinition Pivot = new(
        "pivot",
        "Pivotar (largo �?' ancho)",
        "Datos",
        "Convierte las categorías de una columna en columnas, resumiendo los valores repetidos.",
        [],
        [],
        [
            new("aggfunc", "Si hay varios valores", AnalysisParamKinds.Select, "mean",
                [new("mean", "Media"), new("sum", "Suma"), new("median", "Mediana"), new("min", "Mínimo"), new("max", "Máximo"), new("count", "Cantidad"), new("first", "Primero")], false)
        ],
        RunPivot,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Estructura",
        Roles:
        [
            new ColumnRole("index", "Filas del resultado", ColumnKinds.Any, Multiple: true),
            new ColumnRole("columns", "Categorías que pasan a columnas", ColumnKinds.Categorical),
            new ColumnRole("values", "Valores", ColumnKinds.Numeric, Multiple: true)
        ],
        Outputs: [new AnalysisOutputSpec(RowOperations.ResultKey, "Datos pivotados", Required: true)],
        NeedsDataFrame: true);

    public static readonly AnalysisDefinition Merge = new(
        "merge",
        "Unir con otro dataset (merge)",
        "Datos",
        "Añade las columnas de otro dataset emparejando filas por una o varias claves.",
        [],
        [],
        [
            new("other", "El otro dataset", AnalysisParamKinds.Dataset, "", [], false),
            new("how", "Qué filas conservar", AnalysisParamKinds.Select, "left",
                [new("inner", "Solo las filas con pareja en los dos"), new("left", "Todas las de este dataset"), new("right", "Todas las del otro dataset"), new("outer", "Todas las filas de los dos")], false),
            new("relation", "Relación esperada entre las filas", AnalysisParamKinds.Select, "m:1",
                [new("m:1", "Muchas a una"), new("1:1", "Una a una"), new("1:m", "Una a muchas"), new("m:m", "Sin comprobar")], false),
            new("suffix", "Añadir a los nombres repetidos del otro dataset", AnalysisParamKinds.Text, "_otro", [], false),
            new("keep_origin", "Añadir una columna «origen»", AnalysisParamKinds.Boolean, false, [], false)
        ],
        RunMerge,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Estructura",
        Roles:
        [
            new ColumnRole("keys", "Clave(s) en este dataset", ColumnKinds.Any, Multiple: true),
            new ColumnRole("other_keys", "Clave(s) en el otro dataset", ColumnKinds.Any, Multiple: true)
        ],
        Outputs: [new AnalysisOutputSpec(RowOperations.ResultKey, "Datos unidos", Required: true)],
        NeedsDataFrame: true);

    public static readonly AnalysisDefinition Append = new(
        "append",
        "Apilar otro dataset (añadir filas)",
        "Datos",
        "Pone las filas de otro dataset debajo de las de este (columnas con el mismo nombre y tipo).",
        [],
        [],
        [
            new("other", "El otro dataset", AnalysisParamKinds.Dataset, "", [], false),
            new("mode", "Columnas del resultado", AnalysisParamKinds.Select, "union", [new("union", "Todas"), new("common", "Solo las comunes")], false),
            new("source", "Añadir una columna que indique de dónde viene cada fila", AnalysisParamKinds.Boolean, true, [], false),
            new("source_name", "Nombre de esa columna", AnalysisParamKinds.Text, "origen", [], false),
            new("label_main", "Valor para las filas de este dataset", AnalysisParamKinds.Text, "actual", [], false),
            new("label_other", "Valor para las filas del otro", AnalysisParamKinds.Text, "añadido", [], false)
        ],
        RunAppend,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Estructura",
        Outputs: [new AnalysisOutputSpec(RowOperations.ResultKey, "Datos apilados", Required: true)],
        NeedsDataFrame: true);

    public static readonly AnalysisDefinition ImputeModel = new(
        "impute-model",
        "Imputación multivariante (KNN / MICE)",
        "Datos",
        "Estima los faltantes numéricos a partir de las demás columnas: vecinos más cercanos o regresiones encadenadas.",
        [],
        [],
        [
            new("method", "Método", AnalysisParamKinds.Select, "mice", [new("mice", "MICE (regresiones encadenadas)"), new("knn", "K vecinos más cercanos")], false),
            new("k", "Vecinos (k)", AnalysisParamKinds.Integer, 5.0, [], false, Minimum: 1, Maximum: 100),
            new("max_iter", "Iteraciones", AnalysisParamKinds.Integer, 10.0, [], false, Minimum: 1, Maximum: 100),
            new("seed", "Semilla", AnalysisParamKinds.Integer, 123.0, [], false, Minimum: 0, Maximum: 2147483647)
        ],
        RunImpute,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Calidad de los datos",
        Roles:
        [
            new ColumnRole("cols", "Columnas con faltantes a imputar", ColumnKinds.Numeric, Multiple: true),
            new ColumnRole("predictors", "Otras columnas que ayudan a predecir", ColumnKinds.Numeric, Multiple: true, Optional: true)
        ],
        Outputs: [new AnalysisOutputSpec(RowOperations.ResultKey, "Datos imputados", Required: true)],
        NeedsDataFrame: true);

    private static Result<AnalysisResult> RunSample(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var random = new System.Random(input.Pint("seed", 123));
        var strata = ExtendedHelpers.FirstRole(input, "strata", "Estratos");
        var size = input.Pnum("size", 100);
        var mode = input.Pstr("mode", "n");
        var replace = input.Pbool("replace");
        var rows = new List<int>();
        if (strata.Length > 0)
        {
            var labels = frame.Texts(strata);
            foreach (var level in frame.Levels(strata))
            {
                var candidates = Enumerable.Range(0, frame.RowCount).Where(index => labels[index] == level).ToArray();
                var take = mode == "fraction" ? (int)Math.Round(candidates.Length * size) : (int)Math.Round(size * candidates.Length / (double)frame.RowCount);
                rows.AddRange(Pick(candidates, Math.Min(take, replace ? int.MaxValue : candidates.Length), replace, random));
            }
        }
        else
        {
            var all = Enumerable.Range(0, frame.RowCount).ToArray();
            var take = mode == "fraction" ? (int)Math.Round(frame.RowCount * size) : (int)Math.Round(size);
            rows.AddRange(Pick(all, Math.Min(take, replace ? int.MaxValue : all.Length), replace, random));
        }
        var after = frame.SubsetRows(rows);
        return CleaningOperations.Build("sample", "Muestra", frame, after, [["Semilla", JsNumber.ToString(input.Pint("seed", 123)), JsNumber.ToString(after.RowCount)]], "Se extrajo la muestra aleatoria.");
    }

    private static IEnumerable<int> Pick(int[] values, int count, bool replace, System.Random random)
    {
        if (replace) return Enumerable.Range(0, count).Select(_ => values[random.Next(values.Length)]);
        return values.OrderBy(_ => random.Next()).Take(count);
    }

    private static Result<AnalysisResult> RunPartition(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var random = new System.Random(input.Pint("seed", 123));
        var test = input.Pnum("test_fraction", 0.3);
        var validation = input.Pnum("validation_fraction", 0);
        var labels = new string[frame.RowCount];
        if (input.Pstr("method", "random") == "sequential")
        {
            var time = ExtendedHelpers.FirstRole(input, "time", "Ordenar por fecha");
            var order = Enumerable.Range(0, frame.RowCount).ToArray();
            if (time.Length > 0) Array.Sort(order, (left, right) => frame.Dates(time)[left].GetValueOrDefault().CompareTo(frame.Dates(time)[right].GetValueOrDefault()));
            var testCount = (int)Math.Round(frame.RowCount * test);
            var validationCount = (int)Math.Round(frame.RowCount * validation);
            for (var position = 0; position < order.Length; position++) labels[order[position]] = position < validationCount ? "validacion" : position >= order.Length - testCount ? "prueba" : "entrenamiento";
        }
        else
        {
            var order = Enumerable.Range(0, frame.RowCount).OrderBy(_ => random.Next()).ToArray();
            var testCount = (int)Math.Round(frame.RowCount * test);
            var validationCount = (int)Math.Round(frame.RowCount * validation);
            for (var position = 0; position < order.Length; position++) labels[order[position]] = position < testCount ? "prueba" : position < testCount + validationCount ? "validacion" : "entrenamiento";
        }
        var after = frame.With(DataColumn.Text("particion", labels));
        var counts = labels.GroupBy(value => value).OrderBy(group => group.Key).Select(group => (IReadOnlyList<string>)new[] { group.Key, JsNumber.ToString(group.Count()), ModelHelpers.N(100.0 * group.Count() / labels.Length) }).ToArray();
        return Result.Ok(new AnalysisResult(
            "partition",
            "Partición entrenamiento / prueba",
            [
                new TilesSection("Resumen", [new TileItem("Filas", JsNumber.ToString(frame.RowCount)), new TileItem("Prueba", $"{test * 100:0.#} %"), new TileItem("Validación", $"{(validation > 0 ? validation * 100 : 0):0.#} %")]),
                new TableSection("Particiones", ["Partición", "n", "%"], counts)
            ],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal) { [RowOperations.ResultKey] = new DatasetResult("Datos con partición", after.Columns) },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal) { ["n_rows"] = frame.RowCount, ["partitions"] = labels.Distinct().Count() },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunAggregate(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var by = input.RoleNames("by", true);
        var values = input.RoleNames("values", true);
        var functions = input.Plist("functions");
        var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var row = 0; row < frame.RowCount; row++)
        {
            var key = string.Join("\u001f", by.Select(name => frame.Texts(name)[row] ?? string.Empty));
            if (!groups.TryGetValue(key, out var rows)) groups[key] = rows = [];
            rows.Add(row);
        }
        var columns = new List<DataColumn>();
        var keysList = groups.Keys.ToArray();
        for (var index = 0; index < by.Count; index++)
        {
            var part = index;
            columns.Add(DataColumn.Text(by[index], keysList.Select(key => key.Split('\u001f')[part]).ToArray()));
        }
        if (input.Pbool("include_n")) columns.Add(DataColumn.Numeric("n", groups.Values.Select(rows => (double)rows.Count).ToArray()));
        foreach (var value in values)
        {
            foreach (var function in functions)
            {
                var computed = groups.Values.Select(rows => AggregateValues(frame.Numeric(value), rows, function)).ToArray();
                columns.Add(DataColumn.Numeric(value + "_" + function, computed));
            }
        }
        var result = new Frame(columns, groups.Count);
        return CleaningOperations.Build("aggregate", "Resumen por grupos", frame, result, [["Grupos", JsNumber.ToString(groups.Count), JsNumber.ToString(functions.Count)]], "Se resumieron las columnas por grupo.");
    }

    private static double AggregateValues(double[] values, List<int> rows, string function)
    {
        var selected = rows.Select(index => values[index]).Where(value => !double.IsNaN(value)).ToArray();
        return function switch
        {
            "count" => selected.Length,
            "sum" => selected.Sum(),
            "median" => NumericMath.Quantile(selected, 0.5),
            "min" => selected.DefaultIfEmpty(double.NaN).Min(),
            "max" => selected.DefaultIfEmpty(double.NaN).Max(),
            "std" => NumericMath.Sd(selected),
            "var" => Math.Pow(NumericMath.Sd(selected), 2),
            "q1" => NumericMath.Quantile(selected, 0.25),
            "q3" => NumericMath.Quantile(selected, 0.75),
            "nunique" => selected.Distinct().Count(),
            "first" => selected.FirstOrDefault(),
            "last" => selected.LastOrDefault(),
            _ => selected.DefaultIfEmpty(double.NaN).Average()
        };
    }

    private static Result<AnalysisResult> RunMelt(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var ids = input.RoleNames("ids");
        var values = input.RoleNames("values", true);
        var varName = input.Pstr("var_name", "variable");
        var valueName = input.Pstr("value_name", "valor");
        var rows = new List<int>();
        var names = new List<string>();
        for (var row = 0; row < frame.RowCount; row++)
        {
            foreach (var value in values)
            {
                rows.Add(row);
                names.Add(value);
            }
        }
        var columns = new List<DataColumn>();
        foreach (var id in ids) columns.Add(CloneColumn(frame.Column(id), rows));
        columns.Add(DataColumn.Text(varName, names.ToArray()));
        var numeric = rows.Select((row, index) => frame.Numeric(names[index])[row]).ToArray();
        columns.Add(DataColumn.Numeric(valueName, numeric));
        var after = new Frame(columns, rows.Count);
        return CleaningOperations.Build("melt", "Datos apilados", frame, after, [["Columnas apiladas", JsNumber.ToString(values.Count), JsNumber.ToString(after.RowCount)]], "Se apilaron las columnas seleccionadas.");
    }

    private static DataColumn CloneColumn(DataColumn column, List<int> rows) => column.Kind switch
    {
        DataColumnKind.Numeric => DataColumn.Numeric(column.Name, rows.Select(column.NumberAt).ToArray()),
        DataColumnKind.Text => DataColumn.Text(column.Name, rows.Select(column.TextAt).ToArray()),
        _ => DataColumn.DateTime(column.Name, rows.Select(column.DateAt).ToArray())
    };

    private static Result<AnalysisResult> RunPivot(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var index = input.RoleNames("index", true);
        var column = ExtendedHelpers.FirstRole(input, "columns", "Categorías que pasan a columnas");
        var values = input.RoleNames("values", true);
        var levels = frame.Levels(column);
        var keys = Enumerable.Range(0, frame.RowCount).Select(row => string.Join("\u001f", index.Select(name => frame.Texts(name)[row] ?? string.Empty))).ToArray();
        var groups = Enumerable.Range(0, frame.RowCount).GroupBy(row => keys[row]).OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
        var columns = new List<DataColumn>();
        foreach (var name in index) columns.Add(frame.Column(name));
        foreach (var level in levels)
        {
            foreach (var value in values)
            {
                var numbers = frame.Numeric(value);
                var computed = groups.Select(group =>
                {
                    var selected = group.Where(row => frame.Texts(column)[row] == level).Select(row => numbers[row]).Where(number => !double.IsNaN(number)).ToArray();
                    return AggregateValues(selected, Enumerable.Range(0, selected.Length).ToList(), input.Pstr("aggfunc", "mean"));
                }).ToArray();
                columns.Add(DataColumn.Numeric(value + "_" + level, computed));
            }
        }
        var after = new Frame(columns, groups.Length);
        return CleaningOperations.Build("pivot", "Datos pivotados", frame, after, [["Filas", JsNumber.ToString(groups.Length), JsNumber.ToString(levels.Length)]], "Se pivotaron las categorías.");
    }

    private static Result<AnalysisResult> RunMerge(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var otherName = input.Pstr("other", "");
        if (string.IsNullOrEmpty(otherName) || !input.Datasets.ContainsKey(otherName)) return ExtendedHelpers.Invalid("Indique el otro dataset.");
        var other = input.Dataset(otherName);
        var keys = input.RoleNames("keys", true);
        var otherKeys = input.RoleNames("other_keys", true);
        if (keys.Count != otherKeys.Count) return ExtendedHelpers.Invalid("Las claves de los dos datasets deben tener el mismo número de columnas.");
        var how = input.Pstr("how", "left");
        var suffix = input.Pstr("suffix", "_otro");
        var pairs = new List<(int? Left, int? Right)>();
        var rightUsed = new HashSet<int>();
        for (var left = 0; left < frame.RowCount; left++)
        {
            var matches = Enumerable.Range(0, other.RowCount).Where(right => keys.Select((key, index) => frame.Texts(key)[left] == other.Texts(otherKeys[index])[right]).All(value => value)).ToArray();
            if (matches.Length == 0 && how is "left" or "outer") pairs.Add((left, null));
            foreach (var right in matches) { pairs.Add((left, right)); rightUsed.Add(right); }
        }
        if (how is "right" or "outer")
        {
            for (var right = 0; right < other.RowCount; right++) if (!rightUsed.Contains(right)) pairs.Add((null, right));
        }
        var columns = new List<DataColumn>();
        foreach (var column in frame.Columns) columns.Add(ColumnFor(column, pairs.Select(pair => pair.Left).ToArray(), frame));
        foreach (var column in other.Columns)
        {
            if (otherKeys.Contains(column.Name) && frame.Has(column.Name)) continue;
            var name = frame.Has(column.Name) ? column.Name + suffix : column.Name;
            columns.Add(ColumnFor(column, pairs.Select(pair => pair.Right).ToArray(), other).Renamed(name));
        }
        if (input.Pbool("keep_origin")) columns.Add(DataColumn.Text("origen", pairs.Select(pair => pair.Left is null ? "otro" : "actual").ToArray()));
        var after = new Frame(columns, pairs.Count);
        return CleaningOperations.Build("merge", "Datos unidos", frame, after, [["Filas", JsNumber.ToString(after.RowCount), how]], "Se unieron los datasets por las claves.");
    }

    private static DataColumn ColumnFor(DataColumn column, int?[] rows, Frame source)
    {
        return column.Kind switch
        {
            DataColumnKind.Numeric => DataColumn.Numeric(column.Name, rows.Select(row => row is null ? double.NaN : source.Numeric(column.Name)[row.Value]).ToArray()),
            DataColumnKind.Text => DataColumn.Text(column.Name, rows.Select(row => row is null ? null : source.Texts(column.Name)[row.Value]).ToArray()),
            _ => DataColumn.DateTime(column.Name, rows.Select(row => row is null ? null : source.Dates(column.Name)[row.Value]).ToArray())
        };
    }

    private static Result<AnalysisResult> RunAppend(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var otherName = input.Pstr("other", "");
        if (string.IsNullOrEmpty(otherName) || !input.Datasets.ContainsKey(otherName)) return ExtendedHelpers.Invalid("Indique el otro dataset.");
        var other = input.Dataset(otherName);
        var common = input.Pstr("mode", "union") == "common";
        var names = common ? frame.Columns.Select(column => column.Name).Where(other.Has).ToArray() : frame.Columns.Select(column => column.Name).Concat(other.Columns.Select(column => column.Name)).Distinct().ToArray();
        var columns = new List<DataColumn>();
        foreach (var name in names)
        {
            var main = frame.Has(name) ? frame.Column(name) : null;
            var second = other.Has(name) ? other.Column(name) : null;
            var kind = main?.Kind ?? second?.Kind ?? DataColumnKind.Numeric;
            columns.Add(kind switch
            {
                DataColumnKind.Numeric => DataColumn.Numeric(name, [.. main?.Numbers ?? Enumerable.Repeat(double.NaN, frame.RowCount).ToArray(), .. second?.Numbers ?? Enumerable.Repeat(double.NaN, other.RowCount).ToArray()]),
                DataColumnKind.Text => DataColumn.Text(name, [.. main?.Texts ?? Enumerable.Repeat((string?)null, frame.RowCount).ToArray(), .. second?.Texts ?? Enumerable.Repeat((string?)null, other.RowCount).ToArray()]),
                _ => DataColumn.DateTime(name, [.. main?.Dates ?? Enumerable.Repeat((DateTime?)null, frame.RowCount).ToArray(), .. second?.Dates ?? Enumerable.Repeat((DateTime?)null, other.RowCount).ToArray()])
            });
        }
        if (input.Pbool("source"))
        {
            var sourceName = input.Pstr("source_name", "origen");
            columns.Add(DataColumn.Text(sourceName, [.. Enumerable.Repeat(input.Pstr("label_main", "actual"), frame.RowCount), .. Enumerable.Repeat(input.Pstr("label_other", "añadido"), other.RowCount)]));
        }
        var after = new Frame(columns, frame.RowCount + other.RowCount);
        return CleaningOperations.Build("append", "Datos apilados", frame, after, [["Filas añadidas", JsNumber.ToString(other.RowCount), JsNumber.ToString(after.RowCount)]], "Se apilaron los datasets.");
    }

    private static Result<AnalysisResult> RunImpute(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var columns = input.RoleNames("cols", true);
        var predictors = input.RoleNames("predictors");
        var method = input.Pstr("method", "mice");
        var after = frame;
        if (method == "knn")
        {
            var k = input.Pint("k", 5);
            var usable = columns.Concat(predictors).Distinct().Where(frame.Has).ToArray();
            var standardized = usable.ToDictionary(name => name, name => Standardize(frame.Numeric(name)), StringComparer.Ordinal);
            foreach (var column in columns)
            {
                var values = (double[])frame.Numeric(column).Clone();
                var completeRows = Enumerable.Range(0, frame.RowCount).Where(row => usable.All(name => !double.IsNaN(standardized[name][row]))).ToArray();
                for (var row = 0; row < frame.RowCount; row++)
                {
                    if (!double.IsNaN(values[row])) continue;
                    var nearest = completeRows.OrderBy(other => usable.Sum(name => Math.Pow(standardized[name][row] - standardized[name][other], 2))).Take(k).ToArray();
                    values[row] = nearest.Length > 0 ? nearest.Average(index => frame.Numeric(column)[index]) : frame.Numeric(column).Where(value => !double.IsNaN(value)).DefaultIfEmpty(0).Average();
                }
                after = after.With(DataColumn.Numeric(column, values));
            }
        }
        else
        {
            for (var iteration = 0; iteration < input.Pint("max_iter", 10); iteration++)
            {
                foreach (var column in columns)
                {
                    var values = after.Numeric(column);
                    var complete = Enumerable.Range(0, frame.RowCount).Where(row => !double.IsNaN(values[row])).ToArray();
                    if (complete.Length < 3) continue;
                    var usable = predictors.Where(after.Has).Concat(columns.Where(name => name != column)).Distinct().ToArray();
                    if (usable.Length == 0) continue;
                    var design = complete.Select(row => usable.Select(name => after.Numeric(name)[row]).ToArray()).ToArray();
                    var fit = LinearModel.Fit(design.Select(row => Intercept.Concat(row).ToArray()).ToArray(), complete.Select(row => values[row]).ToArray());
                    if (fit.IsError) continue;
                    for (var row = 0; row < frame.RowCount; row++)
                    {
                        if (!double.IsNaN(values[row])) continue;
                        if (usable.Any(name => double.IsNaN(after.Numeric(name)[row]))) continue;
                        values[row] = fit.Value.Coefficients[0] + usable.Select((name, index) => fit.Value.Coefficients[index + 1] * after.Numeric(name)[row]).Sum();
                    }
                    after = after.With(DataColumn.Numeric(column, values));
                }
            }
        }
        return CleaningOperations.Build("impute-model", "Datos imputados", frame, after, [["Método", method, JsNumber.ToString(columns.Count)]], "Se imputaron los faltantes numéricos.");
    }

    private static double[] Standardize(double[] values)
    {
        var mean = values.Where(value => !double.IsNaN(value)).DefaultIfEmpty(0).Average();
        var sd = NumericMath.Sd(values.Where(value => !double.IsNaN(value)).ToArray());
        return values.Select(value => double.IsNaN(value) || sd == 0 ? value : (value - mean) / sd).ToArray();
    }
}
