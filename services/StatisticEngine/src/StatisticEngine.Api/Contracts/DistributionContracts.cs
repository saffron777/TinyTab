using StatisticEngine.Domain.Distributions;

namespace StatisticEngine.Api.Contracts;

public sealed record DistributionParameterDto(
    string Key,
    string Label,
    double DefaultValue,
    bool Integer,
    double? MinExclusive,
    double? MinInclusive,
    double? MaxInclusive);

public sealed record DistributionInfoDto(
    string Id,
    string Label,
    string Kind,
    IReadOnlyList<DistributionParameterDto> Params);

public sealed record DistributionListResponse(EngineDto Engine, IReadOnlyList<DistributionInfoDto> Distributions);

public sealed record DistributionEvaluateRequest(
    Dictionary<string, double>? Params,
    double[]? X,
    double[]? Probabilities);

public sealed record DistributionSampleRequest(Dictionary<string, double>? Params, int Count, uint? Seed);

public static class DistributionMapping
{
    public static DistributionInfoDto ToDto(IDistribution distribution) =>
        new(
            distribution.Id,
            distribution.Label,
            DistributionKinds.ToWire(distribution.Kind),
            distribution.Parameters
                .Select(parameter => new DistributionParameterDto(
                    parameter.Key,
                    parameter.Label,
                    parameter.DefaultValue,
                    parameter.Integer,
                    parameter.MinExclusive,
                    parameter.MinInclusive,
                    parameter.MaxInclusive))
                .ToArray());
}
