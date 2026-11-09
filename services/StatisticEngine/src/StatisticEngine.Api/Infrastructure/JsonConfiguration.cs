using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StatisticEngine.Api.Infrastructure;

/// <summary>
/// Configuración JSON del API: camelCase, no finitos como literales con nombre
/// (<c>"NaN"</c>, <c>"Infinity"</c>), propiedades nulas omitidas y escape relajado para
/// que los textos en español salgan legibles.
/// </summary>
public static class JsonConfiguration
{
    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = null;
        options.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    }
}
