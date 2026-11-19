namespace StatisticEngine.Domain.Data;

/// <summary>Grupo de filas de un frame: la etiqueta del nivel y los índices 0-based que le pertenecen.</summary>
public sealed record FrameGroup(string Label, int[] Rows);

/// <summary>
/// Agrupación por niveles (port de <c>level_order</c>/<c>groups_of</c> de Sigma): las etiquetas se
/// ordenan de forma determinista y los grupos vacíos no se incluyen.
/// </summary>
public static class Grouping
{
    /// <summary>Niveles distintos no nulos, ordenados, y sus índices de fila.</summary>
    public static IReadOnlyList<FrameGroup> By(Frame frame, string column)
    {
        var levels = frame.Levels(column);
        var source = frame.Column(column);
        var buckets = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var level in levels) buckets[level] = [];
        for (var row = 0; row < frame.RowCount; row++)
        {
            if (source.IsMissing(row)) continue;
            buckets[source.LabelAt(row)].Add(row);
        }
        return levels.Select(level => new FrameGroup(level, [.. buckets[level]])).ToArray();
    }

    /// <summary>
    /// Valores numéricos de una columna por cada nivel de la columna de grupo. Sin grupo devuelve una
    /// sola entrada con la columna completa (comportamiento de <c>groups_of</c>).
    /// </summary>
    public static IReadOnlyList<(string Label, double[] Values)> NumericByGroup(
        Frame frame,
        string valueColumn,
        string? groupColumn)
    {
        var values = frame.Numeric(valueColumn);
        if (string.IsNullOrEmpty(groupColumn))
        {
            var all = new List<double>(frame.RowCount);
            foreach (var value in values)
            {
                if (!double.IsNaN(value)) all.Add(value);
            }
            return [(valueColumn, [.. all])];
        }

        var output = new List<(string Label, double[] Values)>();
        foreach (var group in By(frame, groupColumn))
        {
            var part = new List<double>(group.Rows.Length);
            foreach (var row in group.Rows)
            {
                var value = values[row];
                if (!double.IsNaN(value)) part.Add(value);
            }
            if (part.Count > 0) output.Add((group.Label, [.. part]));
        }
        return output;
    }

    /// <summary>Filas completas de las columnas y valores numéricos alineados (descarta faltantes juntos).</summary>
    public static (Frame Frame, int Dropped) Complete(Frame frame, IReadOnlyList<string> columns)
    {
        var complete = frame.CompleteCases(columns, out var dropped);
        return (complete, dropped);
    }
}
