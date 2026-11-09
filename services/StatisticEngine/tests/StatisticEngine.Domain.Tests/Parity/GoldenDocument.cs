using System.Text.Json;

namespace StatisticEngine.Domain.Tests.Parity;

public sealed class GoldenCase
{
    public GoldenCase(IReadOnlyList<JsonElement> args, JsonElement expected)
    {
        Args = args;
        Expected = expected;
    }

    public IReadOnlyList<JsonElement> Args { get; }

    public JsonElement Expected { get; }

    public int Count => Args.Count;
}

/// <summary>
/// Vector dorado generado por <c>tools/golden</c> a partir del motor TypeScript.
/// Un archivo por módulo; cada función expone una lista de casos ordenados.
/// </summary>
public sealed class GoldenDocument
{
    private readonly Dictionary<string, List<GoldenCase>> _functions;

    private GoldenDocument(string module, Dictionary<string, List<GoldenCase>> functions)
    {
        Module = module;
        _functions = functions;
    }

    public string Module { get; }

    public IReadOnlyCollection<string> FunctionNames => _functions.Keys;

    public static GoldenDocument Load(string module)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Parity", "vectors", $"{module}.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"No se encontró el vector dorado '{module}'. Regénere los vectores con tools/golden. Ruta: {path}",
                path);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var functions = new Dictionary<string, List<GoldenCase>>(StringComparer.Ordinal);
        foreach (var property in root.GetProperty("functions").EnumerateObject())
        {
            var cases = new List<GoldenCase>();
            foreach (var item in property.Value.EnumerateArray())
            {
                var args = item.GetProperty("args").EnumerateArray().Select(element => element.Clone()).ToArray();
                var expected = item.GetProperty("expected").Clone();
                cases.Add(new GoldenCase(args, expected));
            }
            functions[property.Name] = cases;
        }

        return new GoldenDocument(root.GetProperty("module").GetString() ?? module, functions);
    }

    public IReadOnlyList<GoldenCase> Cases(string function) =>
        _functions.TryGetValue(function, out var list) ? list : Array.Empty<GoldenCase>();

    public bool Has(string function) => _functions.ContainsKey(function);

    public int Count => _functions.Values.Sum(list => list.Count);
}
