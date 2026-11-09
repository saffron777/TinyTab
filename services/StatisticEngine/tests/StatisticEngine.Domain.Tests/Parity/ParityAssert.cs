using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace StatisticEngine.Domain.Tests.Parity;

/// <summary>Discrepancia de paridad detectada al comparar un vector dorado con el motor C#.</summary>
public sealed class ParityMismatchException(string message) : Exception(message);

/// <summary>
/// Comparación profunda entre un valor esperado (codificado por <c>tools/golden</c>) y el
/// resultado de la implementación C#. Los números se comparan con tolerancia relativa;
/// los textos, claves y estructuras deben coincidir exactamente.
/// </summary>
public static class ParityAssert
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void Matches(JsonElement expected, object? actual, double tolerance, string path)
    {
        var node = actual is null ? null : JsonSerializer.SerializeToNode(actual, actual.GetType(), SerializerOptions);
        Compare(expected, node, tolerance, path);
    }

    private static void Compare(JsonElement expected, JsonNode? actual, double tolerance, string path)
    {
        if (expected.ValueKind == JsonValueKind.Object && expected.TryGetProperty("$undefined", out _))
        {
            if (actual is not null) throw Mismatch(path, "un valor ausente", actual.ToJsonString());
            return;
        }

        if (GoldenReader.TryNumber(expected, out var expectedNumber))
        {
            if (!TryNodeNumber(actual, out var actualNumber))
            {
                throw Mismatch(path, $"número {expectedNumber:R}", actual?.ToJsonString() ?? "null");
            }
            if (!NumbersEqual(expectedNumber, actualNumber, tolerance))
            {
                throw Mismatch(
                    path,
                    expectedNumber.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    actualNumber.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            return;
        }

        switch (expected.ValueKind)
        {
            case JsonValueKind.String:
            {
                var expectedText = expected.GetString() ?? string.Empty;
                var actualText = actual is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
                if (!string.Equals(expectedText, actualText, StringComparison.Ordinal))
                {
                    throw Mismatch(path, $"\"{expectedText}\"", actualText is null ? actual?.ToJsonString() ?? "null" : $"\"{actualText}\"");
                }
                return;
            }
            case JsonValueKind.True:
            case JsonValueKind.False:
            {
                var expectedFlag = expected.GetBoolean();
                var actualFlag = actual is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : (bool?)null;
                if (actualFlag != expectedFlag) throw Mismatch(path, expectedFlag.ToString(), actualFlag?.ToString() ?? "null");
                return;
            }
            case JsonValueKind.Null:
                if (actual is not null) throw Mismatch(path, "null", actual.ToJsonString());
                return;
            case JsonValueKind.Array:
            {
                if (actual is not JsonArray actualArray) throw Mismatch(path, "arreglo", actual?.ToJsonString() ?? "null");
                var expectedLength = expected.GetArrayLength();
                if (actualArray.Count != expectedLength)
                {
                    throw Mismatch(path, $"arreglo de {expectedLength}", $"arreglo de {actualArray.Count}");
                }
                var index = 0;
                foreach (var item in expected.EnumerateArray())
                {
                    Compare(item, actualArray[index], tolerance, $"{path}[{index}]");
                    index++;
                }
                return;
            }
            case JsonValueKind.Object:
            {
                if (actual is not JsonObject actualObject) throw Mismatch(path, "objeto", actual?.ToJsonString() ?? "null");
                var expectedKeys = expected.EnumerateObject().Select(property => property.Name).ToArray();
                foreach (var property in expected.EnumerateObject())
                {
                    if (!actualObject.TryGetPropertyValue(property.Name, out var actualValue))
                    {
                        throw Mismatch($"{path}.{property.Name}", "propiedad presente", "propiedad ausente");
                    }
                    Compare(property.Value, actualValue, tolerance, $"{path}.{property.Name}");
                }
                foreach (var actualKey in actualObject.Select(pair => pair.Key))
                {
                    if (!expectedKeys.Contains(actualKey, StringComparer.Ordinal))
                    {
                        throw Mismatch($"{path}.{actualKey}", "propiedad ausente", "propiedad presente");
                    }
                }
                return;
            }
            default:
                throw Mismatch(path, expected.ValueKind.ToString(), actual?.ToJsonString() ?? "null");
        }
    }

    private static bool TryNodeNumber(JsonNode? node, out double value)
    {
        value = 0;
        if (node is not JsonValue jsonValue) return false;
        if (jsonValue.TryGetValue<double>(out var number))
        {
            value = number;
            return true;
        }
        if (jsonValue.TryGetValue<string>(out var text)) return GoldenReader.TrySpecial(text, out value, out _);
        return false;
    }

    private static bool NumbersEqual(double expected, double actual, double tolerance)
    {
        if (double.IsNaN(expected) || double.IsNaN(actual)) return double.IsNaN(expected) && double.IsNaN(actual);
        if (double.IsInfinity(expected) || double.IsInfinity(actual)) return expected.Equals(actual);
        if (expected == actual) return true;
        if (tolerance <= 0) return false;
        var scale = Math.Max(1.0, Math.Max(Math.Abs(expected), Math.Abs(actual)));
        return Math.Abs(expected - actual) <= tolerance * scale;
    }

    private static ParityMismatchException Mismatch(string path, string expected, string actual) =>
        new($"{path}: se esperaba {expected} pero se obtuvo {actual}");
}
