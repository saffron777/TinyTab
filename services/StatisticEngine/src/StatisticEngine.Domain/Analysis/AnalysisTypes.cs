using System.Text.Json.Serialization;
using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;

namespace StatisticEngine.Domain.Analysis;

/// <summary>Tipos de parámetro del diálogo de análisis (espeja <c>AnalysisParamKind</c>).</summary>
public static class AnalysisParamKinds
{
    public const string Number = "number";

    public const string Integer = "integer";

    public const string Select = "select";

    public const string Boolean = "boolean";

    public const string Multiselect = "multiselect";

    public const string Text = "text";

    public const string Code = "code";

    public const string Dataset = "dataset";
}

/// <summary>Tipos de columna que acepta un rol de análisis (port de <c>sigma.domain.analysis.spec.RoleSpec</c>).</summary>
public static class ColumnKinds
{
    public const string Numeric = "numeric";

    public const string Categorical = "categorical";

    public const string DateTime = "datetime";

    public const string Any = "any";
}

public sealed record AnalysisParamOption(string Value, string Label);

/// <summary>Condición de visibilidad de un parámetro: sólo se usa si otro parámetro vale uno de estos valores.</summary>
public sealed record AnalysisVisibleIf(string Key, IReadOnlyList<string> Values);

/// <summary>
/// Definición de un parámetro. <see cref="DefaultValue"/> es un <c>double</c>, <c>bool</c>,
/// <c>string</c> o <c>string[]</c> según el tipo, igual que en el motor (se serializa por tipo en
/// tiempo de ejecución).
/// </summary>
public sealed record AnalysisParamDefinition(
    string Key,
    string Label,
    string Kind,
    object? DefaultValue,
    IReadOnlyList<AnalysisParamOption> Options,
    bool Optional,
    double? Minimum = null,
    double? Maximum = null,
    AnalysisVisibleIf? VisibleIf = null,
    string? Help = null);

public sealed record ColumnRequirement(string Key, string Label, bool Required, bool Multiple);

/// <summary>
/// Rol de columna de los análisis extendidos (port de <c>RoleSpec</c>): tipo, multiplicidad,
/// obligatoriedad y número mínimo/máximo de columnas.
/// </summary>
public sealed record ColumnRole(
    string Key,
    string Label,
    string Kind = ColumnKinds.Numeric,
    bool Multiple = false,
    bool Optional = false,
    int MinCount = 1,
    int? MaxCount = null,
    string? Help = null);

/// <summary>Tabla o dataset que el análisis puede dejar como resultado (port de <c>OutputSpec</c>).</summary>
public sealed record AnalysisOutputSpec(string Key, string Label, bool Required = false);

/// <summary>Entrada de un análisis: columnas numéricas, columnas de texto y parámetros.</summary>
public sealed class AnalysisInput
{
    private readonly List<string> _numericOrder;

    public AnalysisInput(
        IReadOnlyDictionary<string, double[]>? numeric = null,
        IReadOnlyDictionary<string, string[]>? text = null,
        IReadOnlyDictionary<string, object?>? parameters = null,
        Frame? frame = null,
        IReadOnlyDictionary<string, Frame>? datasets = null,
        IReadOnlyDictionary<string, string[]>? roles = null)
    {
        Numeric = numeric ?? new Dictionary<string, double[]>(StringComparer.Ordinal);
        Text = text ?? new Dictionary<string, string[]>(StringComparer.Ordinal);
        Params = parameters ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        Columns = frame;
        Datasets = datasets ?? new Dictionary<string, Frame>(StringComparer.Ordinal);
        Roles = roles ?? new Dictionary<string, string[]>(StringComparer.Ordinal);
        _numericOrder = [.. Numeric.Keys];
    }

    public IReadOnlyDictionary<string, double[]> Numeric { get; }

    public IReadOnlyDictionary<string, string[]> Text { get; }

    public IReadOnlyDictionary<string, object?> Params { get; }

    /// <summary>Frame tipado de la petición; si falta, se construye a partir de <see cref="Numeric"/> y <see cref="Text"/>.</summary>
    public Frame? Columns { get; }

    /// <summary>Frames auxiliares para operaciones con dos datasets (merge, append…).</summary>
    public IReadOnlyDictionary<string, Frame> Datasets { get; }

    /// <summary>Enlaces explícitos rol → columna(s) para los análisis con roles declarados.</summary>
    public IReadOnlyDictionary<string, string[]> Roles { get; }

    /// <summary>Columnas numéricas en el orden de llegada, como <c>Object.keys(input.numeric)</c>.</summary>
    public IReadOnlyList<string> NumericKeys => _numericOrder;

    public double[] Num(string key) => Numeric.TryGetValue(key, out var values) ? (double[])values.Clone() : [];

    public string[] ColText(string key) => Text.TryGetValue(key, out var values) ? (string[])values.Clone() : [];

    /// <summary>Frame de la petición: usa <see cref="Columns"/> o convierte los mapas numérico/texto.</summary>
    public Frame AsFrame()
    {
        if (Columns is not null) return Columns;

        var columns = new List<DataColumn>(Numeric.Count + Text.Count);
        var rowCount = 0;
        foreach (var (key, values) in Numeric) rowCount = Math.Max(rowCount, values.Length);
        foreach (var (key, values) in Text) rowCount = Math.Max(rowCount, values.Length);

        foreach (var (key, values) in Numeric)
        {
            columns.Add(DataColumn.Numeric(key, Pad(values, rowCount)));
        }
        foreach (var (key, values) in Text)
        {
            if (Numeric.ContainsKey(key)) continue;
            columns.Add(DataColumn.Text(key, Pad(values, rowCount)));
        }
        return new Frame(columns, rowCount);
    }

    public Frame Dataset(string name) =>
        Datasets.TryGetValue(name, out var frame)
            ? frame
            : throw new InvalidOperationException($"La petición no incluye el dataset \"{name}\".");

    /// <summary>Columnas enlazadas a un rol; si no hay enlace, la columna del mismo nombre.</summary>
    public IReadOnlyList<string> RoleNames(string role)
    {
        if (Roles.TryGetValue(role, out var names) && names.Length > 0) return names;
        if (Numeric.ContainsKey(role) || Text.ContainsKey(role)) return [role];
        return [];
    }

    /// <summary>
    /// Columnas de un rol múltiple: si no hay enlace, usa todas las columnas numéricas y luego las
    /// de texto. Los clientes deberían enviar <c>roles</c> para fijar el orden.
    /// </summary>
    public IReadOnlyList<string> RoleNames(string role, bool multiple)
    {
        var names = RoleNames(role);
        if (names.Count > 0 || !multiple) return names;
        return [.. Numeric.Keys, .. Text.Keys.Where(key => !Numeric.ContainsKey(key))];
    }

    /// <summary>Frame restringido a las columnas del rol (en el orden de <see cref="RoleNames(string, bool)"/>).</summary>
    public Frame RoleFrame(string role, bool multiple = false)
    {
        var source = AsFrame();
        var names = RoleNames(role, multiple);
        var missing = names.Where(name => !source.Has(name)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"La columna \"{missing[0]}\" del rol \"{role}\" no existe.");
        }
        return source.Subset(names);
    }

    /// <summary>Primera columna del rol, o vacío si no hay ninguna.</summary>
    public double[] RoleNumeric(string role)
    {
        var names = RoleNames(role);
        if (names.Count == 0) return [];
        var frame = AsFrame();
        return frame.Has(names[0]) ? frame.Numeric(names[0]) : [];
    }

    /// <summary>Textos de la primera columna del rol.</summary>
    public string?[] RoleText(string role)
    {
        var names = RoleNames(role);
        if (names.Count == 0) return [];
        var frame = AsFrame();
        return frame.Has(names[0]) ? frame.Texts(names[0]) : [];
    }

    /// <summary>Paridad con <c>pnum</c>: falta o cadena vacía → valor por defecto; se exige finito.</summary>
    public double Pnum(string key, double fallback = double.NaN)
    {
        if (!Params.TryGetValue(key, out var value) || value is null) return fallback;
        if (value is string empty && empty.Length == 0) return fallback;
        var parsed = value switch
        {
            double number => number,
            int number => number,
            long number => number,
            bool boolean => boolean ? 1 : 0,
            string rawText => Numerics.JsNumber.Parse(rawText),
            _ => double.NaN
        };
        return double.IsFinite(parsed) ? parsed : fallback;
    }

    /// <summary>Paridad con <c>pstr</c>.</summary>
    public string Pstr(string key, string fallback = "")
    {
        if (!Params.TryGetValue(key, out var value) || value is null) return fallback;
        return value switch
        {
            string text => text,
            double number => Numerics.JsNumber.ToString(number),
            bool boolean => boolean ? "true" : "false",
            _ => fallback
        };
    }

    /// <summary>Texto libre de un parámetro (alias de <see cref="Pstr"/>).</summary>
    public string Ptext(string key, string fallback = "") => Pstr(key, fallback);

    /// <summary>Entero de un parámetro, truncado hacia abajo como en el motor.</summary>
    public int Pint(string key, int fallback = 0)
    {
        var value = Pnum(key, double.NaN);
        return double.IsFinite(value) ? (int)Math.Floor(value) : fallback;
    }

    /// <summary>Paridad con <c>pbool</c>: verdadero para "true", 1 y "1".</summary>
    public bool Pbool(string key, bool fallback = false)
    {
        if (!Params.TryGetValue(key, out var value) || value is null) return fallback;
        return value switch
        {
            string text => text == "true" || text == "1",
            double number => number == 1,
            int number => number == 1,
            bool boolean => boolean,
            _ => fallback
        };
    }

    /// <summary>Selección múltiple de un parámetro (<c>multiselect</c>).</summary>
    public IReadOnlyList<string> Plist(string key)
    {
        if (!Params.TryGetValue(key, out var value) || value is null) return [];
        return value switch
        {
            string[] items => items,
            IReadOnlyList<string> items => [.. items],
            string text => text.Length == 0 ? [] : [text],
            _ => []
        };
    }

    /// <summary>Lista numérica de un parámetro.</summary>
    public IReadOnlyList<double> PnumList(string key)
    {
        if (!Params.TryGetValue(key, out var value) || value is null) return [];
        return value switch
        {
            double[] items => items,
            IReadOnlyList<double> items => [.. items],
            double number => [number],
            string text when text.Length > 0 => [Numerics.JsNumber.Parse(text)],
            _ => []
        };
    }

    private static double[] Pad(double[] values, int count) =>
        values.Length >= count ? values : [.. values, .. Enumerable.Repeat(double.NaN, count - values.Length)];

    private static string?[] Pad(string[] values, int count)
    {
        if (values.Length >= count) return values;
        var output = new string?[count];
        for (var index = 0; index < values.Length; index++) output[index] = values[index];
        return output;
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TableSection), "table")]
[JsonDerivedType(typeof(TextSection), "text")]
[JsonDerivedType(typeof(HeadingSection), "heading")]
[JsonDerivedType(typeof(TilesSection), "tiles")]
[JsonDerivedType(typeof(AlertSection), "alert")]
public abstract record AnalysisSection(string Title);

public sealed record TableSection(
    string Title,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<string>? Align = null,
    string? Note = null) : AnalysisSection(Title);

public sealed record TextSection(string Title, string Text) : AnalysisSection(Title);

/// <summary>Subtítulo interno de un informe (port del bloque <c>heading</c> de Sigma).</summary>
public sealed record HeadingSection(string Title) : AnalysisSection(Title);

public sealed record TileItem(string Label, string Value, string? Hint = null);

/// <summary>Cifras destacadas del resultado (port del bloque <c>tiles</c> de Sigma).</summary>
public sealed record TilesSection(string Title, IReadOnlyList<TileItem> Items) : AnalysisSection(Title);

/// <summary>Conclusión o aviso con énfasis (port del bloque <c>alert</c> de Sigma; severity: info|success|warning|danger).</summary>
public sealed record AlertSection(string Title, string Severity, string Message) : AnalysisSection(Title);

/// <summary>Dataset tabular que el análisis deja como resultado (columnas tipadas).</summary>
public sealed record DatasetResult(string Label, IReadOnlyList<DataColumn> Columns);

/// <summary>Resultado de un análisis: secciones de tabla/texto y gráficos opcionales.</summary>
public sealed record AnalysisResult(
    string Id,
    string Title,
    IReadOnlyList<AnalysisSection> Sections,
    string? Subtitle = null,
    IReadOnlyList<ChartSpec>? Charts = null,
    string? Summary = null,
    IReadOnlyDictionary<string, DatasetResult>? Datasets = null,
    IReadOnlyDictionary<string, object?>? KeyResults = null,
    string? Origin = null);

/// <summary>Definición declarativa de un análisis del catálogo.</summary>
public sealed record AnalysisDefinition(
    string Id,
    string Label,
    string Category,
    string Description,
    IReadOnlyList<ColumnRequirement> NumericColumns,
    IReadOnlyList<ColumnRequirement> TextColumns,
    IReadOnlyList<AnalysisParamDefinition> Params,
    [property: JsonIgnore] Func<AnalysisInput, Result<AnalysisResult>> Run,
    string Origin = "tinytab",
    string? Group = null,
    IReadOnlyList<ColumnRole>? Roles = null,
    IReadOnlyList<AnalysisOutputSpec>? Outputs = null,
    bool Heavy = false,
    int? MaxRows = null,
    bool NeedsDataFrame = false);

/// <summary>Categorías del catálogo, en el orden del motor.</summary>
public static class AnalysisCategories
{
    /// <summary>Categorías de los 60 análisis con paridad TinyTab (orden congelado).</summary>
    public static readonly IReadOnlyList<string> All =
    [
        "Estadísticas básicas",
        "Distribuciones",
        "Tablas",
        "Pruebas de hipótesis",
        "No paramétricos",
        "ANOVA",
        "Regresión",
        "Control de calidad",
        "Capacidad",
        "Sistemas de medida",
        "DOE",
        "Series temporales",
        "Fiabilidad",
        "Multivariante",
        "Potencia y tamaño de muestra"
    ];

    /// <summary>Categorías nuevas que sólo aparecen si algún análisis extendido las usa.</summary>
    public static readonly IReadOnlyList<string> Extensions =
    [
        "Pruebas adicionales",
        "Econometría",
        "Supervivencia",
        "Encuestas",
        "Optimización",
        "Remuestreo",
        "Gráficos"
    ];

    /// <summary>Origen de los análisis del motor original TypeScript.</summary>
    public const string TinyTabOrigin = "tinytab";

    /// <summary>Origen de los análisis portados desde Sigma.</summary>
    public const string SigmaOrigin = "sigma";
}
