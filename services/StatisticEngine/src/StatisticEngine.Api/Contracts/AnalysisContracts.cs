using System.Text.Json;
using StatisticEngine.Domain.Analysis;

namespace StatisticEngine.Api.Contracts;

/// <summary>Cuerpo de <c>POST /api/v1/analyses/{id}/run</c> y <c>/validate</c>.</summary>
public sealed record AnalysisRunRequest(
    Dictionary<string, double[]>? Numeric,
    Dictionary<string, string[]>? Text,
    Dictionary<string, JsonElement>? Params);

public sealed record ColumnRequirementDto(string Key, string Label, bool Required, bool Multiple);

public sealed record AnalysisParamOptionDto(string Value, string Label);

public sealed record AnalysisParamDto(
    string Key,
    string Label,
    string Kind,
    object DefaultValue,
    IReadOnlyList<AnalysisParamOptionDto> Options,
    bool Optional);

public sealed record AnalysisDefinitionDto(
    string Id,
    string Label,
    string Category,
    string Description,
    IReadOnlyList<ColumnRequirementDto> NumericColumns,
    IReadOnlyList<ColumnRequirementDto> TextColumns,
    IReadOnlyList<AnalysisParamDto> Params);

public sealed record EngineDto(string Name, string Version, string ParityBaseline);

public sealed record CategoryGroupDto(string Category, IReadOnlyList<string> Ids);

public sealed record CatalogResponse(
    EngineDto Engine,
    IReadOnlyList<string> Categories,
    IReadOnlyList<AnalysisDefinitionDto> Analyses,
    IReadOnlyList<CategoryGroupDto> ByCategory);

public sealed record MetaResponse(
    EngineDto Engine,
    IReadOnlyDictionary<string, object?> Limits,
    int RequestTimeoutSeconds,
    double UptimeSeconds);

public sealed record ValidateResponse(bool Valid, ValidationErrorDto? Error);

public sealed record ValidationErrorDto(string Code, string Message);

/// <summary>Proyección de las definiciones del dominio al contrato público.</summary>
public static class AnalysisMapping
{
    public static EngineDto Engine() => new(Application.EngineInfo.Name, Application.EngineInfo.Version, Application.EngineInfo.ParityBaseline);

    public static AnalysisDefinitionDto ToDto(AnalysisDefinition definition) =>
        new(
            definition.Id,
            definition.Label,
            definition.Category,
            definition.Description,
            definition.NumericColumns.Select(ToDto).ToArray(),
            definition.TextColumns.Select(ToDto).ToArray(),
            definition.Params.Select(ToDto).ToArray());

    public static IReadOnlyList<AnalysisDefinitionDto> ToDtos(IEnumerable<AnalysisDefinition> definitions) =>
        definitions.Select(ToDto).ToArray();

    public static AnalysisInput ToInput(AnalysisRunRequest? request)
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

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (request?.Params is { } requestParams)
        {
            foreach (var (key, value) in requestParams) parameters[key] = ParamValue(value);
        }

        return new AnalysisInput(numeric, text, parameters);
    }

    private static ColumnRequirementDto ToDto(ColumnRequirement requirement) =>
        new(requirement.Key, requirement.Label, requirement.Required, requirement.Multiple);

    private static AnalysisParamDto ToDto(AnalysisParamDefinition parameter) =>
        new(
            parameter.Key,
            parameter.Label,
            parameter.Kind,
            parameter.DefaultValue,
            parameter.Options.Select(option => new AnalysisParamOptionDto(option.Value, option.Label)).ToArray(),
            parameter.Optional);

    private static object? ParamValue(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
}
