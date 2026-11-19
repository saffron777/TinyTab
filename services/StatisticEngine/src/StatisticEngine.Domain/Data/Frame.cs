using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Data;

/// <summary>
/// Tabla de columnas alineadas por fila: la unidad de trabajo de las operaciones de datos y de los
/// análisis extendidos (equivale al DataFrame de Sigma). Todas las columnas tienen el mismo número
/// de filas; los faltantes son <c>NaN</c> (numéricas) o <c>null</c> (texto/fecha).
/// </summary>
public sealed class Frame
{
    public Frame(IEnumerable<DataColumn> columns, int? rowCount = null)
    {
        var list = columns.ToArray();
        Columns = list;
        var inferred = rowCount ?? (list.Length > 0 ? list[0].Count : 0);
        foreach (var column in list)
        {
            if (column.Count != inferred)
            {
                throw new ArgumentException(
                    $"La columna \"{column.Name}\" tiene {column.Count} filas y el frame {inferred}.",
                    nameof(columns));
            }
        }
        RowCount = inferred;
    }

    public IReadOnlyList<DataColumn> Columns { get; }

    public int RowCount { get; }

    public int ColumnCount => Columns.Count;

    public bool Has(string name) => Find(name) is not null;

    public DataColumn? Find(string name)
    {
        foreach (var column in Columns)
        {
            if (string.Equals(column.Name, name, StringComparison.Ordinal)) return column;
        }
        return null;
    }

    public DataColumn Column(string name) =>
        Find(name) ?? throw new ArgumentException($"La columna \"{name}\" no existe en el frame.", nameof(name));

    /// <summary>Subconjunto de columnas en el orden pedido (error si alguna falta).</summary>
    public Frame Subset(IEnumerable<string> names) => new(names.Select(Column).ToArray(), RowCount);

    public Frame Subset(params string[] names) => Subset((IEnumerable<string>)names);

    /// <summary>Subconjunto de filas (índices 0-based, en el orden dado).</summary>
    public Frame SubsetRows(IReadOnlyList<int> rows)
    {
        var columns = new List<DataColumn>(Columns.Count);
        foreach (var column in Columns)
        {
            switch (column.Kind)
            {
                case DataColumnKind.Numeric:
                {
                    var values = new double[rows.Count];
                    for (var index = 0; index < rows.Count; index++) values[index] = column.NumberAt(rows[index]);
                    columns.Add(DataColumn.Numeric(column.Name, values));
                    break;
                }
                case DataColumnKind.Text:
                {
                    var values = new string?[rows.Count];
                    for (var index = 0; index < rows.Count; index++) values[index] = column.TextAt(rows[index]);
                    columns.Add(DataColumn.Text(column.Name, values));
                    break;
                }
                default:
                {
                    var values = new DateTime?[rows.Count];
                    for (var index = 0; index < rows.Count; index++) values[index] = column.DateAt(rows[index]);
                    columns.Add(DataColumn.DateTime(column.Name, values));
                    break;
                }
            }
        }
        return new Frame(columns, rows.Count);
    }

    /// <summary>Índices de las filas sin faltantes en las columnas indicadas.</summary>
    public int[] CompleteCaseIndices(IEnumerable<string> names)
    {
        var selected = names.Select(Column).ToArray();
        var rows = new List<int>(RowCount);
        for (var row = 0; row < RowCount; row++)
        {
            var complete = true;
            foreach (var column in selected)
            {
                if (column.IsMissing(row))
                {
                    complete = false;
                    break;
                }
            }
            if (complete) rows.Add(row);
        }
        return [.. rows];
    }

    /// <summary>Frame sólo con las filas completas de las columnas indicadas.</summary>
    public Frame CompleteCases(IEnumerable<string> names, out int dropped)
    {
        var rows = CompleteCaseIndices(names);
        dropped = RowCount - rows.Length;
        return SubsetRows(rows);
    }

    public Frame CompleteCases(IEnumerable<string> names) => CompleteCases(names, out _);

    /// <summary>Valores numéricos de una columna (con coerción de texto como <c>pd.to_numeric</c>).</summary>
    public double[] Numeric(string name)
    {
        var column = Column(name);
        return column.Kind switch
        {
            DataColumnKind.Numeric => (double[])column.Numbers.Clone(),
            DataColumnKind.Text => column.Texts.Select(text => JsNumber.Parse(text)).ToArray(),
            _ => throw new ArgumentException($"La columna \"{name}\" es de fecha y no se puede usar como número.", nameof(name))
        };
    }

    /// <summary>Valores de texto de una columna (los numéricos se convierten con el formato del motor).</summary>
    public string?[] Texts(string name)
    {
        var column = Column(name);
        return column.Kind switch
        {
            DataColumnKind.Text => (string?[])column.Texts.Clone(),
            DataColumnKind.Numeric => column.Numbers.Select(value => double.IsNaN(value) ? null : JsNumber.ToString(value)).ToArray(),
            _ => column.Dates.Select(value => value?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).ToArray()
        };
    }

    public DateTime?[] Dates(string name) => (DateTime?[])Column(name).Dates.Clone();

    /// <summary>Niveles distintos no nulos, ordenados (port de <c>level_order</c>).</summary>
    public string[] Levels(string name)
    {
        var column = Column(name);
        if (column.Kind == DataColumnKind.Numeric)
        {
            var numbers = column.Numbers.Where(value => !double.IsNaN(value)).Distinct().OrderBy(value => value);
            return [.. numbers.Select(value => JsNumber.ToString(value))];
        }
        if (column.Kind == DataColumnKind.DateTime)
        {
            var dates = column.Dates.Where(value => value is not null).Select(value => value!.Value).Distinct().OrderBy(value => value);
            return [.. dates.Select(value => value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))];
        }

        var labels = new List<string>(RowCount);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var row = 0; row < RowCount; row++)
        {
            if (column.IsMissing(row)) continue;
            var label = column.LabelAt(row);
            if (seen.Add(label)) labels.Add(label);
        }
        labels.Sort(StringComparer.Ordinal);
        return [.. labels];
    }

    /// <summary>Frame nuevo con una columna añadida o reemplazada por nombre.</summary>
    public Frame With(DataColumn column)
    {
        var columns = new List<DataColumn>(Columns.Count + 1);
        var replaced = false;
        foreach (var existing in Columns)
        {
            if (string.Equals(existing.Name, column.Name, StringComparison.Ordinal))
            {
                columns.Add(column);
                replaced = true;
            }
            else
            {
                columns.Add(existing);
            }
        }
        if (!replaced) columns.Add(column);
        return new Frame(columns, RowCount);
    }

    /// <summary>Frame con las columnas indicadas (por nombre), opcionalmente reordenadas.</summary>
    public Frame Select(IReadOnlyList<string> names, bool strict = true)
    {
        var columns = new List<DataColumn>(names.Count);
        foreach (var name in names)
        {
            var column = Find(name);
            if (column is null)
            {
                if (strict) throw new ArgumentException($"La columna \"{name}\" no existe en el frame.", nameof(names));
                continue;
            }
            columns.Add(column);
        }
        return new Frame(columns, RowCount);
    }

    /// <summary>Frame sin las columnas indicadas.</summary>
    public Frame Drop(IReadOnlyCollection<string> names) =>
        new(Columns.Where(column => !names.Contains(column.Name, StringComparer.Ordinal)).ToArray(), RowCount);

    public static Frame Empty(IEnumerable<string>? columnNames = null, int rowCount = 0)
    {
        var columns = (columnNames ?? []).Select(name => DataColumn.Numeric(name, new double[rowCount]));
        return new Frame(columns, rowCount);
    }
}
