using System.Text.Json;

namespace StatisticEngine.Domain.Tests.Extensions;

/// <summary>Fixtures serializados desde <c>tests/analysis_data.py</c> de Sigma.</summary>
public static class SigmaFixtures
{
    private static readonly Lazy<IReadOnlyDictionary<string, Domain.Data.Frame>> LazyFrames = new(Load, true);

    public static Domain.Data.Frame Standard => Get("standard");

    public static Domain.Data.Frame Get(string name) =>
        LazyFrames.Value.TryGetValue(name, out var frame)
            ? frame
            : throw new InvalidOperationException($"No existe el fixture «{name}» en fixtures.json.");

    private static IReadOnlyDictionary<string, Domain.Data.Frame> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Extensions", "vectors", "fixtures.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var frames = new Dictionary<string, Domain.Data.Frame>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name is "sigmaCommit" || property.Value.ValueKind != JsonValueKind.Object) continue;
            var columns = property.Value.GetProperty("columns").EnumerateArray().ToArray();
            frames[property.Name] = BuildFrame(columns);
        }
        return frames;
    }

    private static Domain.Data.Frame BuildFrame(JsonElement[] columns)
    {
        var parsed = new List<Domain.Data.DataColumn>(columns.Length);
        var rowCount = 0;
        foreach (var column in columns)
        {
            var name = column.GetProperty("name").GetString() ?? string.Empty;
            var kind = column.GetProperty("kind").GetString() ?? "numeric";
            var values = column.GetProperty("values").EnumerateArray().ToArray();
            rowCount = Math.Max(rowCount, values.Length);
            switch (kind)
            {
                case "numeric":
                {
                    var numbers = new double[values.Length];
                    for (var index = 0; index < values.Length; index++)
                    {
                        numbers[index] = values[index].ValueKind == JsonValueKind.Null
                            ? double.NaN
                            : values[index].ValueKind == JsonValueKind.Number
                                ? values[index].GetDouble()
                                : ParseSpecial(values[index].GetString());
                    }
                    parsed.Add(Domain.Data.DataColumn.Numeric(name, numbers));
                    break;
                }
                case "text":
                {
                    var texts = new string?[values.Length];
                    for (var index = 0; index < values.Length; index++)
                    {
                        texts[index] = values[index].ValueKind == JsonValueKind.Null ? null : values[index].GetString();
                    }
                    parsed.Add(Domain.Data.DataColumn.Text(name, texts));
                    break;
                }
                default:
                {
                    var dates = new DateTime?[values.Length];
                    for (var index = 0; index < values.Length; index++)
                    {
                        dates[index] = values[index].ValueKind == JsonValueKind.Null
                            ? null
                            : DateTime.Parse(
                                values[index].GetString() ?? string.Empty,
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.RoundtripKind);
                    }
                    parsed.Add(Domain.Data.DataColumn.DateTime(name, dates));
                    break;
                }
            }
        }
        return new Domain.Data.Frame(parsed.Select(column => column.Resized(rowCount)), rowCount);
    }

    private static double ParseSpecial(string? text) =>
        text switch
        {
            "NaN" => double.NaN,
            "Infinity" => double.PositiveInfinity,
            "-Infinity" => double.NegativeInfinity,
            _ => double.NaN
        };
}
