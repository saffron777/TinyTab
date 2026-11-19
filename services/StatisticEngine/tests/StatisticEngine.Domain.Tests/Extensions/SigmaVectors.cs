using System.Text.Json;
using StatisticEngine.Domain;
using StatisticEngine.Domain.Analysis;

namespace StatisticEngine.Domain.Tests.Extensions;

/// <summary>Un caso de un vector dorado generado desde Sigma.</summary>
public sealed class SigmaCase
{
    public SigmaCase(string module, JsonElement element)
    {
        Module = module;
        Element = element;
    }

    public string Module { get; }

    public JsonElement Element { get; }

    public string Id => Element.GetProperty("id").GetString() ?? string.Empty;

    public string SigmaId => Element.GetProperty("sigma_id").GetString() ?? string.Empty;

    public string Kind => Element.GetProperty("kind").GetString() ?? "example";

    public bool Deterministic => !Element.TryGetProperty("deterministic", out var flag) || flag.GetBoolean();

    public bool IsCatalogCase => !Element.TryGetProperty("catalog", out var flag) || flag.GetBoolean();
}

/// <summary>Lectura de los vectores de Sigma (un archivo por módulo) y del catálogo declarativo.</summary>
public static class SigmaVectors
{
    private static readonly Lazy<IReadOnlyList<SigmaCase>> LazyCases = new(LoadCases, true);

    private static readonly Lazy<JsonDocument> LazyCatalog = new(LoadCatalog, true);

    public static IReadOnlyList<SigmaCase> Cases => LazyCases.Value;

    public static JsonElement Catalog => LazyCatalog.Value.RootElement;

    public static IEnumerable<SigmaCase> Examples => Cases.Where(item => item.Kind == "example");

    public static IEnumerable<SigmaCase> Errors => Cases.Where(item => item.Kind == "error");

    public static SigmaCase? Example(string id) =>
        Examples.FirstOrDefault(item => item.Id == id);

    private static IReadOnlyList<SigmaCase> LoadCases()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Extensions", "vectors");
        var cases = new List<SigmaCase>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (fileName is "fixtures" or "extensions-catalog") continue;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var module = document.RootElement.GetProperty("module").GetString() ?? fileName;
            foreach (var element in document.RootElement.GetProperty("cases").EnumerateArray())
            {
                cases.Add(new SigmaCase(module, element.Clone()));
            }
        }
        return cases;
    }

    private static JsonDocument LoadCatalog()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Extensions", "vectors", "extensions-catalog.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    /// <summary>Construye la entrada del motor a partir del caso y el fixture que declara.</summary>
    public static AnalysisInput BuildInput(SigmaCase item)
    {
        var frameName = item.Element.TryGetProperty("frame", out var frameProperty)
            ? frameProperty.GetString() ?? "standard"
            : "standard";
        var frame = SigmaFixtures.Get(frameName);

        var roles = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (item.Element.TryGetProperty("roles", out var rolesElement))
        {
            foreach (var property in rolesElement.EnumerateObject())
            {
                roles[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => [property.Value.GetString() ?? string.Empty],
                    JsonValueKind.Array => property.Value.EnumerateArray()
                        .Select(value => value.GetString() ?? string.Empty)
                        .ToArray(),
                    _ => []
                };
            }
        }

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (item.Element.TryGetProperty("params", out var paramsElement))
        {
            foreach (var property in paramsElement.EnumerateObject())
            {
                parameters[property.Name] = ParamValue(property.Value);
            }
        }

        return new AnalysisInput(frame: frame, datasets: null, roles: roles, parameters: parameters);
    }

    private static object? ParamValue(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.GetRawText())
                .ToArray(),
            _ => element.GetRawText()
        };
}
