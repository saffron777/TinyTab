using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Data;

/// <summary>Tipo de almacenamiento de una columna de un frame.</summary>
public enum DataColumnKind
{
    Numeric,
    Text,
    DateTime
}

/// <summary>
/// Columna tipada de un frame. Los valores numéricos faltantes se representan con <c>NaN</c>; los de
/// texto y fecha, con <c>null</c>. Equivale a una columna de un DataFrame de Sigma.
/// </summary>
public sealed class DataColumn
{
    private readonly double[]? _numbers;
    private readonly string?[]? _texts;
    private readonly DateTime?[]? _dates;

    private DataColumn(string name, DataColumnKind kind, double[]? numbers, string?[]? texts, DateTime?[]? dates, int count)
    {
        Name = name;
        Kind = kind;
        _numbers = numbers;
        _texts = texts;
        _dates = dates;
        Count = count;
    }

    public string Name { get; }

    public DataColumnKind Kind { get; }

    public int Count { get; }

    public bool IsNumeric => Kind == DataColumnKind.Numeric;

    public bool IsText => Kind == DataColumnKind.Text;

    public bool IsDateTime => Kind == DataColumnKind.DateTime;

    public static DataColumn Numeric(string name, double[] values) =>
        new(name, DataColumnKind.Numeric, values, null, null, values.Length);

    public static DataColumn Text(string name, string?[] values) =>
        new(name, DataColumnKind.Text, null, values, null, values.Length);

    public static DataColumn DateTime(string name, DateTime?[] values) =>
        new(name, DataColumnKind.DateTime, null, null, values, values.Length);

    /// <summary>Valores numéricos tal cual (con <c>NaN</c> en los faltantes); vacío si no es numérica.</summary>
    public double[] Numbers => _numbers ?? [];

    /// <summary>Valores de texto (con <c>null</c> en los faltantes); vacío si no es de texto.</summary>
    public string?[] Texts => _texts ?? [];

    /// <summary>Valores de fecha (con <c>null</c> en los faltantes); vacío si no es de fecha.</summary>
    public DateTime?[] Dates => _dates ?? [];

    public double NumberAt(int index) => _numbers is not null ? _numbers[index] : double.NaN;

    public string? TextAt(int index) => _texts is not null ? _texts[index] : null;

    public DateTime? DateAt(int index) => _dates is not null ? _dates[index] : null;

    public bool IsMissing(int index) =>
        Kind switch
        {
            DataColumnKind.Numeric => double.IsNaN(_numbers![index]),
            DataColumnKind.Text => string.IsNullOrEmpty(_texts![index]),
            _ => _dates![index] is null
        };

    /// <summary>Etiqueta del valor para agrupar o tabular (números con el formato del motor).</summary>
    public string LabelAt(int index) =>
        Kind switch
        {
            DataColumnKind.Numeric => JsNumber.ToString(_numbers![index]),
            DataColumnKind.Text => _texts![index] ?? string.Empty,
            _ => _dates![index]?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
        };

    /// <summary>Copia con otro nombre (renombrar columnas).</summary>
    public DataColumn Renamed(string name) => new(name, Kind, _numbers, _texts, _dates, Count);

    /// <summary>Copia rellenando con faltantes hasta <paramref name="count"/> filas.</summary>
    public DataColumn Resized(int count)
    {
        if (count <= Count) return this;
        return Kind switch
        {
            DataColumnKind.Numeric => Numeric(Name, [.. _numbers!, .. Enumerable.Repeat(double.NaN, count - Count)]),
            DataColumnKind.Text => Text(Name, [.. _texts!, .. Enumerable.Repeat((string?)null, count - Count)]),
            _ => DateTime(Name, [.. _dates!, .. Enumerable.Repeat((DateTime?)null, count - Count)])
        };
    }
}
