using System.Globalization;
using System.Text.Json;
using StatisticEngine.Domain.Analysis;
using StatisticEngine.Domain.Tests.Parity;

namespace StatisticEngine.Domain.Tests.Extensions;

/// <summary>Compara la declaración de un análisis C# con la declaración del spec de Sigma.</summary>
public static class SigmaCatalogCompare
{
    public static void Compare(JsonElement expected, AnalysisDefinition actual)
    {
        CompareText("label", expected.GetProperty("label").GetString(), actual.Label);
        CompareText("description", expected.GetProperty("description").GetString(), actual.Description);
        CompareText("group", expected.GetProperty("group").GetString(), actual.Group);

        var roles = expected.GetProperty("roles").EnumerateArray().ToArray();
        var actualRoles = actual.Roles ?? [];
        if (roles.Length != actualRoles.Count) Fail($"roles: se esperaban {roles.Length} y hay {actualRoles.Count}.");
        for (var index = 0; index < roles.Length; index++)
        {
            var role = roles[index];
            var current = actualRoles[index];
            var path = $"roles[{index}]";
            CompareText($"{path}.key", role.GetProperty("name").GetString(), current.Key);
            CompareText($"{path}.label", role.GetProperty("label").GetString(), current.Label);
            CompareText($"{path}.kind", role.GetProperty("kind").GetString(), current.Kind);
            CompareFlag($"{path}.multiple", role.GetProperty("multiple").GetBoolean(), current.Multiple);
            CompareFlag($"{path}.optional", role.GetProperty("optional").GetBoolean(), current.Optional);
            CompareInt($"{path}.min_count", role.GetProperty("min_count").GetInt32(), current.MinCount);
            var max = role.GetProperty("max_count");
            if (max.ValueKind == JsonValueKind.Null)
            {
                if (current.MaxCount is not null) Fail($"{path}.max_count: se esperaba null y hay {current.MaxCount}.");
            }
            else if (current.MaxCount != max.GetInt32())
            {
                Fail($"{path}.max_count: se esperaba {max.GetInt32()} y hay {current.MaxCount}.");
            }
        }

        var parameters = expected.GetProperty("params").EnumerateArray().ToArray();
        if (parameters.Length != actual.Params.Count)
        {
            Fail($"params: se esperaban {parameters.Length} y hay {actual.Params.Count}.");
        }
        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            var current = actual.Params[index];
            var path = $"params[{index}]";
            CompareText($"{path}.key", parameter.GetProperty("name").GetString(), current.Key);
            CompareText($"{path}.label", parameter.GetProperty("label").GetString(), current.Label);
            CompareText($"{path}.kind", parameter.GetProperty("kind").GetString(), current.Kind);
            CompareFlag($"{path}.optional", parameter.GetProperty("optional").GetBoolean(), current.Optional);
            CompareDefault($"{path}.default", parameter.GetProperty("default"), current.DefaultValue);
            CompareOptions($"{path}.options", parameter.GetProperty("options"), current.Options);
            CompareOptionalNumber($"{path}.minimum", parameter.GetProperty("minimum"), current.Minimum);
            CompareOptionalNumber($"{path}.maximum", parameter.GetProperty("maximum"), current.Maximum);
            CompareVisibleIf($"{path}.visible_if", parameter.GetProperty("visible_if"), current.VisibleIf);
        }

        var outputs = expected.GetProperty("outputs").EnumerateArray().ToArray();
        var actualOutputs = actual.Outputs ?? [];
        if (outputs.Length != actualOutputs.Count) Fail($"outputs: se esperaban {outputs.Length} y hay {actualOutputs.Count}.");
        for (var index = 0; index < outputs.Length; index++)
        {
            CompareText($"outputs[{index}].key", outputs[index].GetProperty("key").GetString(), actualOutputs[index].Key);
            CompareText($"outputs[{index}].label", outputs[index].GetProperty("label").GetString(), actualOutputs[index].Label);
            CompareFlag($"outputs[{index}].required", outputs[index].GetProperty("required").GetBoolean(), actualOutputs[index].Required);
        }

        CompareFlag("heavy", expected.GetProperty("heavy").GetBoolean(), actual.Heavy);
    }

    private static void CompareDefault(string path, JsonElement expected, object? actual)
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.Null:
                if (actual is not null) Fail($"{path}: se esperaba null y hay {actual}.");
                return;
            case JsonValueKind.Number:
                if (actual is not (double or int or long) || Convert.ToDouble(actual, CultureInfo.InvariantCulture) != expected.GetDouble())
                {
                    Fail($"{path}: se esperaba {expected.GetDouble()} y hay {actual}.");
                }
                return;
            case JsonValueKind.String:
                if (!string.Equals(expected.GetString(), actual as string, StringComparison.Ordinal))
                {
                    Fail($"{path}: se esperaba \"{expected.GetString()}\" y hay \"{actual}\".");
                }
                return;
            case JsonValueKind.True:
            case JsonValueKind.False:
                if (actual is not bool flag || flag != expected.GetBoolean())
                {
                    Fail($"{path}: se esperaba {expected.GetBoolean()} y hay {actual}.");
                }
                return;
            case JsonValueKind.Array:
            {
                var items = expected.EnumerateArray().Select(item => item.GetString()).ToArray();
                if (actual is not string[] values || !items.SequenceEqual(values, StringComparer.Ordinal))
                {
                    Fail($"{path}: el valor por defecto no coincide.");
                }
                return;
            }
            default:
                Fail($"{path}: tipo por defecto no soportado.");
                return;
        }
    }

    private static void CompareOptions(string path, JsonElement expected, IReadOnlyList<AnalysisParamOption> actual)
    {
        var options = expected.EnumerateArray().ToArray();
        if (options.Length != actual.Count) Fail($"{path}: se esperaban {options.Length} opciones y hay {actual.Count}.");
        for (var index = 0; index < options.Length; index++)
        {
            var option = options[index].EnumerateArray().ToArray();
            CompareText($"{path}[{index}].value", option[0].GetString(), actual[index].Value);
            CompareText($"{path}[{index}].label", option[1].GetString(), actual[index].Label);
        }
    }

    private static void CompareOptionalNumber(string path, JsonElement expected, double? actual)
    {
        if (expected.ValueKind == JsonValueKind.Null)
        {
            if (actual is not null) Fail($"{path}: se esperaba null y hay {actual}.");
            return;
        }
        if (actual is null || actual.Value != expected.GetDouble())
        {
            Fail($"{path}: se esperaba {expected.GetDouble()} y hay {actual}.");
        }
    }

    private static void CompareVisibleIf(string path, JsonElement expected, AnalysisVisibleIf? actual)
    {
        if (expected.ValueKind == JsonValueKind.Null)
        {
            if (actual is not null) Fail($"{path}: se esperaba null y hay {actual}.");
            return;
        }
        var parts = expected.EnumerateArray().ToArray();
        if (actual is null) Fail($"{path}: se esperaba una condición de visibilidad.");
        CompareText($"{path}.key", parts[0].GetString(), actual!.Key);
        var values = parts[1].EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
        if (!values.SequenceEqual(actual.Values, StringComparer.Ordinal))
        {
            Fail($"{path}.values: la lista de valores no coincide.");
        }
    }

    private static void CompareText(string path, string? expected, string? actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            Fail($"{path}: se esperaba \"{expected}\" y hay \"{actual}\".");
        }
    }

    private static void CompareFlag(string path, bool expected, bool actual)
    {
        if (expected != actual) Fail($"{path}: se esperaba {expected} y hay {actual}.");
    }

    private static void CompareInt(string path, int expected, int actual)
    {
        if (expected != actual) Fail($"{path}: se esperaba {expected} y hay {actual}.");
    }

    private static void Fail(string message) => throw new ParityMismatchException(message);
}
