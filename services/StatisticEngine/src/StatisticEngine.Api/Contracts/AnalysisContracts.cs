using System.Globalization;
using System.Text.Json;
using StatisticEngine.Domain;
using StatisticEngine.Domain.Analysis;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Api.Contracts;

/// <summary>Cuerpo de <c>POST /api/v1/analyses/{id}/run</c> y <c>/validate</c>.</summary>
public sealed record AnalysisRunRequest(
    Dictionary<string, double[]>? Numeric,
    Dictionary<string, string[]>? Text,
    Dictionary<string, JsonElement>? Params,
    IReadOnlyList<DataColumnDto>? Columns,
    Dictionary<string, JsonElement>? Roles,
    Dictionary<string, string[]>? Datetime);

/// <summary>Columna tipada de un frame: <c>numeric</c> (números o null), <c>text</c> o <c>datetime</c> (ISO 8601).</summary>
public sealed record DataColumnDto(string Name, string Kind, JsonElement[]? Values);

public sealed record ColumnRequirementDto(string Key, string Label, bool Required, bool Multiple);

public sealed record ColumnRoleDto(
    string Key,
    string Label,
    string Kind,
    bool Multiple,
    bool Optional,
    int MinCount,
    int? MaxCount,
    string? Help);

public sealed record AnalysisParamOptionDto(string Value, string Label);

public sealed record AnalysisVisibleIfDto(string Key, IReadOnlyList<string> Values);

public sealed record AnalysisParamDto(
    string Key,
    string Label,
    string Kind,
    object? DefaultValue,
    IReadOnlyList<AnalysisParamOptionDto> Options,
    bool Optional,
    double? Minimum,
    double? Maximum,
    AnalysisVisibleIfDto? VisibleIf,
    string? Help);

public sealed record AnalysisOutputDto(string Key, string Label, bool Required);

public sealed record AnalysisDefinitionDto(
    string Id,
    string Label,
    string Category,
    string Description,
    IReadOnlyList<ColumnRequirementDto> NumericColumns,
    IReadOnlyList<ColumnRequirementDto> TextColumns,
    IReadOnlyList<AnalysisParamDto> Params,
    string Origin,
    string? Group,
    IReadOnlyList<ColumnRoleDto> Roles,
    IReadOnlyList<AnalysisOutputDto> Outputs,
    bool Heavy,
    bool NeedsDataFrame);

public sealed record EngineDto(string Name, string Version, string ParityBaseline, string ExtensionsBaseline);

public sealed record CategoryGroupDto(string Category, IReadOnlyList<string> Ids);

public sealed record CatalogResponse(
    EngineDto Engine,
    IReadOnlyList<string> Categories,
    IReadOnlyList<AnalysisDefinitionDto> Analyses,
    IReadOnlyList<CategoryGroupDto> ByCategory);

public sealed record DataCatalogResponse(EngineDto Engine, IReadOnlyList<AnalysisDefinitionDto> Operations);

public sealed record MetaResponse(
    EngineDto Engine,
    IReadOnlyDictionary<string, object?> Limits,
    int RequestTimeoutSeconds,
    double UptimeSeconds);

public sealed record ValidateResponse(bool Valid, ValidationErrorDto? Error);

public sealed record ValidationErrorDto(string Code, string Message);

/// <summary>Cuerpo de <c>POST /api/v1/data/{id}/run</c> y <c>/validate</c>.</summary>
public sealed record DataRunRequest(
    IReadOnlyList<DataColumnDto>? Columns,
    Dictionary<string, DataFrameDto>? Datasets,
    Dictionary<string, JsonElement>? Params,
    Dictionary<string, JsonElement>? Roles);

public sealed record DataFrameDto(IReadOnlyList<DataColumnDto>? Columns);

/// <summary>Proyección de las definiciones del dominio al contrato público.</summary>
public static class AnalysisMapping
{
    public static EngineDto Engine() => new(
        Application.EngineInfo.Name,
        Application.EngineInfo.Version,
        Application.EngineInfo.ParityBaseline,
        Application.EngineInfo.ExtensionsBaseline);

    public static AnalysisDefinitionDto ToDto(AnalysisDefinition definition) =>
        new(
            definition.Id,
            definition.Label,
            definition.Category,
            definition.Description,
            definition.NumericColumns.Select(ToDto).ToArray(),
            definition.TextColumns.Select(ToDto).ToArray(),
            definition.Params.Select(ToDto).ToArray(),
            definition.Origin,
            definition.Group,
            (definition.Roles ?? []).Select(ToDto).ToArray(),
            (definition.Outputs ?? []).Select(ToDto).ToArray(),
            definition.Heavy,
            definition.NeedsDataFrame);

    public static IReadOnlyList<AnalysisDefinitionDto> ToDtos(IEnumerable<AnalysisDefinition> definitions) =>
        definitions.Select(ToDto).ToArray();

    /// <summary>Convierte el cuerpo de una petición de análisis al modelo del dominio.</summary>
    public static Result<AnalysisInput> ToInput(AnalysisRunRequest? request)
    {
        var numeric = new Dictionary<string, double[]>(StringComparer.Ordinal);
        if (request?.Numeric is { } requestNumeric)
        {
            foreach (var (key, values) in requestNumeric) numeric[key] = values;
        }

        var text = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request?.Text is { } requestText)
        {
            foreach (var (key, values) in requestText) text[key] = values;
        }

        var parameters = ToParameters(request?.Params);
        var roles = ToRoleBindings(request?.Roles);

        var frameResult = DataFrameMapping.Build(request?.Columns, request?.Datetime, numeric, text);
        if (frameResult.IsError)
        {
            return Result.Err<AnalysisInput>(frameResult.Error.Code, frameResult.Error.Message);
        }

        return Result.Ok(new AnalysisInput(numeric, text, parameters, frameResult.Value, null, roles));
    }

    /// <summary>Convierte el cuerpo de una operación de datos al modelo del dominio.</summary>
    public static Result<AnalysisInput> ToInput(DataRunRequest? request)
    {
        var parameters = ToParameters(request?.Params);
        var roles = ToRoleBindings(request?.Roles);

        var frameResult = DataFrameMapping.Build(request?.Columns, null, null, null);
        if (frameResult.IsError)
        {
            return Result.Err<AnalysisInput>(frameResult.Error.Code, frameResult.Error.Message);
        }

        var datasets = new Dictionary<string, Frame>(StringComparer.Ordinal);
        if (request?.Datasets is { } requestDatasets)
        {
            foreach (var (key, dataset) in requestDatasets)
            {
                var datasetResult = DataFrameMapping.Build(dataset?.Columns, null, null, null);
                if (datasetResult.IsError)
                {
                    return Result.Err<AnalysisInput>(
                        datasetResult.Error.Code,
                        $"Dataset «{key}»: {datasetResult.Error.Message}");
                }
                datasets[key] = datasetResult.Value;
            }
        }

        return Result.Ok(new AnalysisInput(
            null,
            null,
            parameters,
            frameResult.Value,
            datasets,
            roles));
    }

    private static Dictionary<string, string[]>? ToRoleBindings(Dictionary<string, JsonElement>? roles)
    {
        if (roles is null) return null;
        var bindings = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (key, value) in roles)
        {
            bindings[key] = value.ValueKind switch
            {
                JsonValueKind.String => [value.GetString() ?? string.Empty],
                JsonValueKind.Array => value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString() ?? string.Empty)
                    .ToArray(),
                _ => []
            };
        }
        return bindings;
    }

    private static Dictionary<string, object?> ToParameters(Dictionary<string, JsonElement>? requestParams)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (requestParams is null) return parameters;
        foreach (var (key, value) in requestParams) parameters[key] = ParamValue(value);
        return parameters;
    }

    private static ColumnRequirementDto ToDto(ColumnRequirement requirement) =>
        new(requirement.Key, requirement.Label, requirement.Required, requirement.Multiple);

    private static ColumnRoleDto ToDto(ColumnRole role) =>
        new(
            role.Key,
            role.Label,
            role.Kind,
            role.Multiple,
            role.Optional,
            role.MinCount,
            role.MaxCount,
            role.Help);

    private static AnalysisOutputDto ToDto(AnalysisOutputSpec output) =>
        new(output.Key, output.Label, output.Required);

    private static AnalysisParamDto ToDto(AnalysisParamDefinition parameter) =>
        new(
            parameter.Key,
            parameter.Label,
            parameter.Kind,
            parameter.DefaultValue,
            parameter.Options.Select(option => new AnalysisParamOptionDto(option.Value, option.Label)).ToArray(),
            parameter.Optional,
            parameter.Minimum,
            parameter.Maximum,
            parameter.VisibleIf is null
                ? null
                : new AnalysisVisibleIfDto(parameter.VisibleIf.Key, parameter.VisibleIf.Values),
            parameter.Help);

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

/// <summary>Construcción de frames tipados a partir del contrato HTTP.</summary>
public static class DataFrameMapping
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>
    /// Construye un frame alineado con las columnas tipadas, las fechas y los mapas numérico/texto.
    /// Las columnas de distinto largo se rellenan con faltantes hasta la fila máxima.
    /// </summary>
    public static Result<Frame> Build(
        IReadOnlyList<DataColumnDto>? columns,
        IReadOnlyDictionary<string, string[]>? datetime,
        IReadOnlyDictionary<string, double[]>? numeric,
        IReadOnlyDictionary<string, string[]>? text)
    {
        var parsed = new List<DataColumn>();
        var rowCount = 0;

        if (columns is not null)
        {
            foreach (var column in columns)
            {
                var result = ToDataColumn(column);
                if (result.IsError) return Result.Err<Frame>(result.Error.Code, result.Error.Message);
                parsed.Add(result.Value);
                rowCount = Math.Max(rowCount, result.Value.Count);
            }
        }

        if (datetime is not null)
        {
            foreach (var (name, values) in datetime)
            {
                if (parsed.Any(column => column.Name == name)) continue;
                var dates = new DateTime?[values.Length];
                for (var index = 0; index < values.Length; index++)
                {
                    dates[index] = ParseDate(values[index], out var valid);
                    if (!valid)
                    {
                        return Result.Err<Frame>(
                            DomainErrorCode.InvalidInput,
                            $"El valor {index + 1} de la columna de fecha «{name}» no es una fecha ISO 8601.");
                    }
                }
                parsed.Add(DataColumn.DateTime(name, dates));
                rowCount = Math.Max(rowCount, values.Length);
            }
        }

        if (numeric is not null)
        {
            foreach (var (name, values) in numeric)
            {
                if (parsed.Any(column => column.Name == name)) continue;
                parsed.Add(DataColumn.Numeric(name, values));
                rowCount = Math.Max(rowCount, values.Length);
            }
        }

        if (text is not null)
        {
            foreach (var (name, values) in text)
            {
                if (parsed.Any(column => column.Name == name)) continue;
                parsed.Add(DataColumn.Text(name, values));
                rowCount = Math.Max(rowCount, values.Length);
            }
        }

        return Result.Ok(new Frame(parsed.Select(column => column.Resized(rowCount)), rowCount));
    }

    private static Result<DataColumn> ToDataColumn(DataColumnDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return Result.Err<DataColumn>(DomainErrorCode.InvalidInput, "Toda columna del frame necesita un nombre.");
        }

        var kind = (dto.Kind ?? string.Empty).Trim().ToLowerInvariant();
        var values = dto.Values ?? [];

        switch (kind)
        {
            case "numeric":
            case "number":
            {
                var numbers = new double[values.Length];
                for (var index = 0; index < values.Length; index++)
                {
                    var element = values[index];
                    if (element.ValueKind == JsonValueKind.Null)
                    {
                        numbers[index] = double.NaN;
                        continue;
                    }
                    if (element.ValueKind == JsonValueKind.Number)
                    {
                        numbers[index] = element.GetDouble();
                        continue;
                    }
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        var parsed = ParseNumber(element.GetString());
                        if (double.IsNaN(parsed))
                        {
                            return Result.Err<DataColumn>(
                                DomainErrorCode.InvalidInput,
                                $"El valor {index + 1} de la columna «{dto.Name}» no es numérico.");
                        }
                        numbers[index] = parsed;
                        continue;
                    }
                    return Result.Err<DataColumn>(
                        DomainErrorCode.InvalidInput,
                        $"El valor {index + 1} de la columna «{dto.Name}» no es numérico.");
                }
                return Result.Ok(DataColumn.Numeric(dto.Name, numbers));
            }
            case "text":
            case "categorical":
            case "string":
            {
                var texts = new string?[values.Length];
                for (var index = 0; index < values.Length; index++)
                {
                    var element = values[index];
                    texts[index] = element.ValueKind switch
                    {
                        JsonValueKind.Null => null,
                        JsonValueKind.String => element.GetString(),
                        JsonValueKind.Number => element.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => null
                    };
                }
                return Result.Ok(DataColumn.Text(dto.Name, texts));
            }
            case "datetime":
            case "date":
            {
                var dates = new DateTime?[values.Length];
                for (var index = 0; index < values.Length; index++)
                {
                    var element = values[index];
                    if (element.ValueKind == JsonValueKind.Null)
                    {
                        dates[index] = null;
                        continue;
                    }
                    if (element.ValueKind != JsonValueKind.String)
                    {
                        return Result.Err<DataColumn>(
                            DomainErrorCode.InvalidInput,
                            $"El valor {index + 1} de la columna «{dto.Name}» no es una fecha ISO 8601.");
                    }
                    dates[index] = ParseDate(element.GetString(), out var valid);
                    if (!valid)
                    {
                        return Result.Err<DataColumn>(
                            DomainErrorCode.InvalidInput,
                            $"El valor {index + 1} de la columna «{dto.Name}» no es una fecha ISO 8601.");
                    }
                }
                return Result.Ok(DataColumn.DateTime(dto.Name, dates));
            }
            default:
                return Result.Err<DataColumn>(
                    DomainErrorCode.InvalidInput,
                    $"Tipo de columna desconocido «{dto.Kind}» en «{dto.Name}» (use numeric, text o datetime).");
        }
    }

    private static DateTime? ParseDate(string? text, out bool valid)
    {
        valid = false;
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateTime.TryParse(
                text,
                Invariant,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            valid = true;
            return parsed;
        }
        return null;
    }

    private static double ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return double.NaN;
        if (text.Trim() is "NaN") return double.NaN;
        return JsNumber.Parse(text);
    }
}
