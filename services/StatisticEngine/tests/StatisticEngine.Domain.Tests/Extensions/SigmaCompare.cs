using System.Globalization;
using System.Text.Json;
using StatisticEngine.Domain;
using StatisticEngine.Domain.Analysis;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Tests.Parity;

namespace StatisticEngine.Domain.Tests.Extensions;

/// <summary>
/// Comparador de los vectores generados desde Sigma. Las tablas se almacenan en Sigma como números
/// crudos y en el motor C# como texto formateado: se comparan parseando la celda. Los textos de
/// conclusión y los metadatos se comparan exactos.
/// </summary>
public static class SigmaCompare
{
    public static void Example(SigmaCase item, Result<AnalysisResult> result)
    {
        var expected = item.Element.GetProperty("expected");
        if (expected.TryGetProperty("error", out var sigmaError))
        {
            if (!result.IsError)
            {
                Fail(item, $"Sigma falló ({sigmaError.GetString()}) y el motor devolvió un resultado.");
            }
            return;
        }

        if (result.IsError)
        {
            Fail(item, $"se esperaba un resultado y el motor devolvió {result.Error.WireCode}: {result.Error.Message}");
        }

        var value = result.Value;
        CompareText(item, "title", expected.GetProperty("title").GetString(), value.Title);
        CompareText(item, "subtitle", NullableString(expected, "subtitle"), value.Subtitle);
        CompareBlocks(item, expected.GetProperty("blocks"), value);
        CompareDatasets(item, expected.GetProperty("datasets"), value.Datasets);
        CompareResults(item, expected.GetProperty("result"), value.KeyResults);
    }

    public static void Error(SigmaCase item, Result<AnalysisResult> result)
    {
        if (!result.IsError)
        {
            Fail(item, "se esperaba un error de validación y el motor devolvió un resultado.");
        }

        var expected = Normalize(item.Element.GetProperty("error").GetString() ?? string.Empty);
        var actual = Normalize(result.Error.Message);
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            Fail(item, $"mensaje de error: se esperaba \"{expected}\" y se obtuvo \"{actual}\".");
        }
    }

    private static void CompareBlocks(SigmaCase item, JsonElement expectedBlocks, AnalysisResult actual)
    {
        var blocks = expectedBlocks.EnumerateArray().ToArray();
        var nonChart = blocks.Where(block => block.GetProperty("type").GetString() != "chart").ToArray();
        var charts = blocks.Count(block => block.GetProperty("type").GetString() == "chart");

        if (nonChart.Length != actual.Sections.Count)
        {
            Fail(item, $"se esperaban {nonChart.Length} secciones y hay {actual.Sections.Count}.");
        }

        for (var index = 0; index < nonChart.Length; index++)
        {
            var block = nonChart[index];
            var section = actual.Sections[index];
            var type = block.GetProperty("type").GetString();
            var path = $"sections[{index}]";
            switch (type)
            {
                case "table":
                {
                    if (section is not TableSection table)
                    {
                        Fail(item, $"{path}: se esperaba una tabla.");
                        return;
                    }
                    CompareText(item, $"{path}.title", NullableString(block, "title"), table.Title);
                    CompareList(item, $"{path}.columns", block.GetProperty("columns"), table.Headers);
                    var rows = block.GetProperty("rows").EnumerateArray().ToArray();
                    if (rows.Length != table.Rows.Count)
                    {
                        Fail(item, $"{path}: se esperaban {rows.Length} filas y hay {table.Rows.Count}.");
                    }
                    for (var row = 0; row < rows.Length; row++)
                    {
                        var cells = rows[row].EnumerateArray().ToArray();
                        if (cells.Length != table.Rows[row].Count)
                        {
                            Fail(item, $"{path}.rows[{row}]: se esperaban {cells.Length} celdas y hay {table.Rows[row].Count}.");
                        }
                        for (var cell = 0; cell < cells.Length; cell++)
                        {
                            CompareCell(item, $"{path}.rows[{row}][{cell}]", cells[cell], table.Rows[row][cell]);
                        }
                    }
                    CompareText(item, $"{path}.note", NullableString(block, "note"), table.Note);
                    break;
                }
                case "tiles":
                {
                    if (section is not TilesSection tiles)
                    {
                        Fail(item, $"{path}: se esperaban cifras destacadas.");
                        return;
                    }
                    var items = block.GetProperty("items").EnumerateArray().ToArray();
                    if (items.Length != tiles.Items.Count)
                    {
                        Fail(item, $"{path}: se esperaban {items.Length} cifras y hay {tiles.Items.Count}.");
                    }
                    for (var tile = 0; tile < items.Length; tile++)
                    {
                        var parts = items[tile].EnumerateArray().ToArray();
                        CompareText(item, $"{path}[{tile}].label", parts[0].GetString(), tiles.Items[tile].Label);
                        CompareCell(item, $"{path}[{tile}].value", parts[1], tiles.Items[tile].Value);
                    }
                    break;
                }
                case "text":
                {
                    if (section is not TextSection text)
                    {
                        Fail(item, $"{path}: se esperaba texto.");
                        return;
                    }
                    var paragraphs = string.Join("\n", block.GetProperty("paragraphs").EnumerateArray().Select(p => p.GetString()));
                    CompareText(item, $"{path}.text", paragraphs, text.Text);
                    break;
                }
                case "alert":
                {
                    if (section is not AlertSection alert)
                    {
                        Fail(item, $"{path}: se esperaba una alerta.");
                        return;
                    }
                    CompareText(item, $"{path}.severity", block.GetProperty("severity").GetString(), alert.Severity);
                    CompareText(item, $"{path}.message", block.GetProperty("message").GetString(), alert.Message);
                    break;
                }
                case "heading":
                {
                    if (section is not HeadingSection heading)
                    {
                        Fail(item, $"{path}: se esperaba un subtítulo.");
                        return;
                    }
                    CompareText(item, $"{path}.text", block.GetProperty("text").GetString(), heading.Title);
                    break;
                }
                default:
                    Fail(item, $"{path}: bloque desconocido «{type}».");
                    break;
            }
        }

        var actualCharts = actual.Charts?.Count ?? 0;
        if (charts != actualCharts)
        {
            Fail(item, $"se esperaban {charts} gráficos y hay {actualCharts}.");
        }
    }

    private static void CompareDatasets(
        SigmaCase item,
        JsonElement expected,
        IReadOnlyDictionary<string, DatasetResult>? actual)
    {
        var entries = expected.EnumerateObject().ToArray();
        var count = actual?.Count ?? 0;
        if (entries.Length != count)
        {
            Fail(item, $"se esperaban {entries.Length} datasets y hay {count}.");
        }
        if (actual is null) return;

        foreach (var entry in entries)
        {
            if (!actual.TryGetValue(entry.Name, out var dataset))
            {
                Fail(item, $"falta el dataset «{entry.Name}».");
            }
            CompareText(item, $"datasets.{entry.Name}.label", entry.Value.GetProperty("label").GetString(), dataset.Label);
            var columns = entry.Value.GetProperty("columns").EnumerateArray().ToArray();
            if (columns.Length != dataset.Columns.Count)
            {
                Fail(item, $"datasets.{entry.Name}: se esperaban {columns.Length} columnas y hay {dataset.Columns.Count}.");
            }
            for (var index = 0; index < columns.Length; index++)
            {
                var expectedColumn = columns[index];
                var column = dataset.Columns[index];
                CompareText(item, $"datasets.{entry.Name}[{index}].name", expectedColumn.GetProperty("name").GetString(), column.Name);
                CompareText(item, $"datasets.{entry.Name}[{index}].kind", expectedColumn.GetProperty("kind").GetString(), KindName(column.Kind));
                var values = expectedColumn.GetProperty("values").EnumerateArray().ToArray();
                if (values.Length != column.Count)
                {
                    Fail(item, $"datasets.{entry.Name}[{index}]: se esperaban {values.Length} valores y hay {column.Count}.");
                }
                for (var row = 0; row < values.Length; row++)
                {
                    CompareColumnValue(item, $"datasets.{entry.Name}[{index}][{row}]", values[row], column, row);
                }
            }
        }
    }

    private static void CompareColumnValue(SigmaCase item, string path, JsonElement expected, DataColumn column, int row)
    {
        switch (column.Kind)
        {
            case DataColumnKind.Numeric:
            {
                var actual = column.NumberAt(row);
                if (expected.ValueKind == JsonValueKind.Null)
                {
                    if (!double.IsNaN(actual)) Fail(item, $"{path}: se esperaba un faltante y hay {actual}.");
                    return;
                }
                if (expected.ValueKind != JsonValueKind.Number)
                {
                    Fail(item, $"{path}: valor esperado no numérico.");
                }
                CompareNumber(item, path, expected.GetDouble(), actual, 1e-6);
                return;
            }
            case DataColumnKind.Text:
            {
                var actual = column.TextAt(row);
                var expectedText = expected.ValueKind == JsonValueKind.Null ? null : expected.GetString();
                if (!string.Equals(expectedText, actual, StringComparison.Ordinal))
                {
                    Fail(item, $"{path}: se esperaba \"{expectedText}\" y se obtuvo \"{actual}\".");
                }
                return;
            }
            default:
            {
                var actual = column.DateAt(row);
                var expectedText = expected.ValueKind == JsonValueKind.Null ? null : expected.GetString();
                var actualText = actual?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (!string.Equals(expectedText, actualText, StringComparison.Ordinal))
                {
                    Fail(item, $"{path}: se esperaba \"{expectedText}\" y se obtuvo \"{actualText}\".");
                }
                return;
            }
        }
    }

    private static void CompareResults(
        SigmaCase item,
        JsonElement expected,
        IReadOnlyDictionary<string, object?>? actual)
    {
        if (expected.ValueKind != JsonValueKind.Object) return;
        actual ??= new Dictionary<string, object?>();
        foreach (var property in expected.EnumerateObject())
        {
            if (!actual.TryGetValue(property.Name, out var value))
            {
                Fail(item, $"result.{property.Name}: falta la cifra clave.");
            }
            CompareObject(item, $"result.{property.Name}", property.Value, value);
        }
    }

    private static void CompareObject(SigmaCase item, string path, JsonElement expected, object? actual)
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.Null:
                if (actual is not null) Fail(item, $"{path}: se esperaba null y hay {actual}.");
                return;
            case JsonValueKind.Number:
            {
                var number = actual switch
                {
                    double value => value,
                    int value => value,
                    long value => value,
                    float value => value,
                    _ => double.NaN
                };
                if (actual is not (double or int or long or float))
                {
                    Fail(item, $"{path}: se esperaba un número y hay {actual}.");
                    return;
                }
                CompareNumber(item, path, expected.GetDouble(), number, 1e-5);
                return;
            }
            case JsonValueKind.String:
                if (!string.Equals(expected.GetString(), actual as string, StringComparison.Ordinal))
                {
                    Fail(item, $"{path}: se esperaba \"{expected.GetString()}\" y hay \"{actual}\".");
                }
                return;
            case JsonValueKind.True:
            case JsonValueKind.False:
                if (actual is not bool flag || flag != expected.GetBoolean())
                {
                    Fail(item, $"{path}: se esperaba {expected.GetBoolean()} y hay {actual}.");
                }
                return;
            case JsonValueKind.Array:
            {
                var items = expected.EnumerateArray().ToArray();
                if (actual is string || actual is not System.Collections.IEnumerable enumerable)
                {
                    Fail(item, $"{path}: se esperaba una lista.");
                    return;
                }
                var list = enumerable.Cast<object?>().ToArray();
                if (items.Length != list.Length) Fail(item, $"{path}: se esperaban {items.Length} elementos y hay {list.Length}.");
                for (var index = 0; index < items.Length; index++)
                {
                    CompareObject(item, $"{path}[{index}]", items[index], list[index]);
                }
                return;
            }
            case JsonValueKind.Object:
            {
                if (actual is not IReadOnlyDictionary<string, object?> map)
                {
                    Fail(item, $"{path}: se esperaba un objeto.");
                    return;
                }
                foreach (var property in expected.EnumerateObject())
                {
                    if (!map.TryGetValue(property.Name, out var value))
                    {
                        Fail(item, $"{path}.{property.Name}: falta el valor.");
                    }
                    CompareObject(item, $"{path}.{property.Name}", property.Value, value);
                }
                return;
            }
        }
    }

    private static void CompareCell(SigmaCase item, string path, JsonElement expected, string actual)
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.Null:
                if (actual is not ("*" or "")) Fail(item, $"{path}: se esperaba un faltante y hay \"{actual}\".");
                return;
            case JsonValueKind.String:
                if (!string.Equals(expected.GetString(), actual, StringComparison.Ordinal))
                {
                    Fail(item, $"{path}: se esperaba \"{expected.GetString()}\" y hay \"{actual}\".");
                }
                return;
            case JsonValueKind.Number:
            {
                if (actual == "*")
                {
                    Fail(item, $"{path}: se esperaba {expected.GetDouble()} y la celda está vacía.");
                }
                if (actual.StartsWith('<'))
                {
                    if (!double.TryParse(actual[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var bound))
                    {
                        Fail(item, $"{path}: no se pudo interpretar \"{actual}\".");
                    }
                    if (expected.GetDouble() >= bound)
                    {
                        Fail(item, $"{path}: se esperaba {expected.GetDouble()} y la celda dice \"{actual}\".");
                    }
                    return;
                }
                if (!double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    Fail(item, $"{path}: no se pudo interpretar \"{actual}\" como número.");
                }
                CompareNumber(item, path, expected.GetDouble(), number, 2e-4);
                return;
            }
            case JsonValueKind.True:
            case JsonValueKind.False:
                if (!string.Equals(expected.GetBoolean() ? "true" : "false", actual, StringComparison.Ordinal))
                {
                    Fail(item, $"{path}: se esperaba \"{expected.GetRawText()}\" y hay \"{actual}\".");
                }
                return;
        }
    }

    private static void CompareNumber(SigmaCase item, string path, double expected, double actual, double relative)
    {
        if (double.IsNaN(expected) || double.IsNaN(actual))
        {
            if (!double.IsNaN(expected) || !double.IsNaN(actual)) Fail(item, $"{path}: se esperaba {expected} y hay {actual}.");
            return;
        }
        if (double.IsInfinity(expected) || double.IsInfinity(actual))
        {
            if (!expected.Equals(actual)) Fail(item, $"{path}: se esperaba {expected} y hay {actual}.");
            return;
        }
        var scale = Math.Max(1.0, Math.Max(Math.Abs(expected), Math.Abs(actual)));
        if (Math.Abs(expected - actual) > relative * scale)
        {
            Fail(item, $"{path}: se esperaba {expected:R} y hay {actual:R}.");
        }
    }

    private static void CompareList(SigmaCase item, string path, JsonElement expected, IReadOnlyList<string> actual)
    {
        var items = expected.EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray();
        if (items.Length != actual.Count) Fail(item, $"{path}: se esperaban {items.Length} elementos y hay {actual.Count}.");
        for (var index = 0; index < items.Length; index++)
        {
            if (!string.Equals(items[index], actual[index], StringComparison.Ordinal))
            {
                Fail(item, $"{path}[{index}]: se esperaba \"{items[index]}\" y hay \"{actual[index]}\".");
            }
        }
    }

    private static void CompareText(SigmaCase item, string path, string? expected, string? actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            Fail(item, $"{path}: se esperaba \"{expected}\" y hay \"{actual}\".");
        }
    }

    private static string? NullableString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string KindName(DataColumnKind kind) =>
        kind switch
        {
            DataColumnKind.Numeric => "numeric",
            DataColumnKind.Text => "text",
            _ => "datetime"
        };

    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(SigmaCase item, string message) =>
        throw new ParityMismatchException($"{item.Module}/{item.Id} ({item.Kind}): {message}");
}
