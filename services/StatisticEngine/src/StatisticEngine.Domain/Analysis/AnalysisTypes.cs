using System.Text.Json.Serialization;
using StatisticEngine.Domain.Charts;

namespace StatisticEngine.Domain.Analysis;

/// <summary>Tipos de parámetro del diálogo de análisis (espeja <c>AnalysisParamKind</c>).</summary>
public static class AnalysisParamKinds
{
    public const string Number = "number";

    public const string Integer = "integer";

    public const string Select = "select";
}

public sealed record AnalysisParamOption(string Value, string Label);

/// <summary>
/// Definición de un parámetro. <see cref="DefaultValue"/> es un <c>double</c> o un <c>string</c>
/// según el tipo, igual que en el motor (se serializa por tipo en tiempo de ejecución).
/// </summary>
public sealed record AnalysisParamDefinition(
    string Key,
    string Label,
    string Kind,
    object DefaultValue,
    IReadOnlyList<AnalysisParamOption> Options,
    bool Optional);

public sealed record ColumnRequirement(string Key, string Label, bool Required, bool Multiple);

/// <summary>Entrada de un análisis: columnas numéricas, columnas de texto y parámetros.</summary>
public sealed class AnalysisInput
{
    private readonly List<string> _numericOrder;

    public AnalysisInput(
        IReadOnlyDictionary<string, double[]>? numeric = null,
        IReadOnlyDictionary<string, string[]>? text = null,
        IReadOnlyDictionary<string, object?>? parameters = null)
    {
        Numeric = numeric ?? new Dictionary<string, double[]>(StringComparer.Ordinal);
        Text = text ?? new Dictionary<string, string[]>(StringComparer.Ordinal);
        Params = parameters ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        _numericOrder = [.. Numeric.Keys];
    }

    public IReadOnlyDictionary<string, double[]> Numeric { get; }

    public IReadOnlyDictionary<string, string[]> Text { get; }

    public IReadOnlyDictionary<string, object?> Params { get; }

    /// <summary>Columnas numéricas en el orden de llegada, como <c>Object.keys(input.numeric)</c>.</summary>
    public IReadOnlyList<string> NumericKeys => _numericOrder;

    public double[] Num(string key) => Numeric.TryGetValue(key, out var values) ? (double[])values.Clone() : [];

    public string[] ColText(string key) => Text.TryGetValue(key, out var values) ? (string[])values.Clone() : [];

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
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TableSection), "table")]
[JsonDerivedType(typeof(TextSection), "text")]
public abstract record AnalysisSection(string Title);

public sealed record TableSection(
    string Title,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<string>? Align = null) : AnalysisSection(Title);

public sealed record TextSection(string Title, string Text) : AnalysisSection(Title);

/// <summary>Resultado de un análisis: secciones de tabla/texto y gráficos opcionales.</summary>
public sealed record AnalysisResult(
    string Id,
    string Title,
    IReadOnlyList<AnalysisSection> Sections,
    string? Subtitle = null,
    IReadOnlyList<ChartSpec>? Charts = null,
    string? Summary = null);

/// <summary>Definición declarativa de un análisis del catálogo.</summary>
public sealed record AnalysisDefinition(
    string Id,
    string Label,
    string Category,
    string Description,
    IReadOnlyList<ColumnRequirement> NumericColumns,
    IReadOnlyList<ColumnRequirement> TextColumns,
    IReadOnlyList<AnalysisParamDefinition> Params,
    [property: JsonIgnore] Func<AnalysisInput, Result<AnalysisResult>> Run);

/// <summary>Categorías del catálogo, en el orden del motor.</summary>
public static class AnalysisCategories
{
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
}
