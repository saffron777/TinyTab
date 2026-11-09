using System.Text.Json;
using Xunit;

namespace StatisticEngine.Domain.Tests.Parity;

/// <summary>
/// Lectura de argumentos y valores de los vectores dorados. Acepta las tres formas en que
/// se codifican los no finitos: <c>{"$num":"NaN"}</c>, <c>"NaN"</c> (System.Text.Json) y número.
/// </summary>
public static class GoldenReader
{
    public static bool IsUndefined(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty("$undefined", out _);

    public static double Number(JsonElement element)
    {
        if (TryNumber(element, out var value)) return value;
        throw new InvalidOperationException($"El argumento no es numérico: {element.ValueKind} {element.GetRawText()}");
    }

    public static bool TryNumber(JsonElement element, out double value)
    {
        value = 0;
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                value = element.GetDouble();
                return true;
            case JsonValueKind.String:
                return TrySpecial(element.GetString(), out value, out _);
            case JsonValueKind.Object when element.TryGetProperty("$num", out var special):
                return TrySpecial(special.GetString(), out value, out _);
            default:
                return false;
        }
    }

    public static double[] Numbers(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Se esperaba un arreglo y llegó {element.ValueKind}.");
        }
        var output = new double[element.GetArrayLength()];
        var index = 0;
        foreach (var item in element.EnumerateArray()) output[index++] = Number(item);
        return output;
    }

    public static double[][] Matrix(JsonElement element) =>
        element.EnumerateArray().Select(Numbers).ToArray();

    public static int Int(JsonElement element) => (int)Number(element);

    public static string Text(JsonElement element) => element.GetString() ?? string.Empty;

    public static string[] Texts(JsonElement element) => element.EnumerateArray().Select(Text).ToArray();

    public static bool Flag(JsonElement element) => element.GetBoolean();

    public static bool TrySpecial(string? text, out double value, out bool negativeZero)
    {
        negativeZero = false;
        switch (text)
        {
            case "NaN":
                value = double.NaN;
                return true;
            case "Infinity":
                value = double.PositiveInfinity;
                return true;
            case "-Infinity":
                value = double.NegativeInfinity;
                return true;
            case "-0":
                value = -0.0;
                negativeZero = true;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    /// <summary>Comprueba que la cantidad de argumentos no finitos sea la esperada.</summary>
    public static void ExpectArgs(GoldenCase goldenCase, int expected)
    {
        if (goldenCase.Args.Count != expected)
        {
            throw new InvalidOperationException(
                $"El caso declara {goldenCase.Args.Count} argumentos y se esperaban {expected}.");
        }
    }

    internal static void Fail(string message) => Assert.Fail(message);
}
