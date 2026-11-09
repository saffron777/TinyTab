using System.Diagnostics;
using Microsoft.Extensions.Options;
using StatisticEngine.Api.Contracts;
using StatisticEngine.Api.Infrastructure;

namespace StatisticEngine.Api.Endpoints;

/// <summary>Metadatos operativos del servicio.</summary>
public static class MetaEndpoints
{
    private static readonly long StartedTicks = Stopwatch.GetTimestamp();

    public static RouteGroupBuilder MapMetaEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/meta", GetMeta)
            .WithName("GetMeta")
            .WithSummary("Versión, límites efectivos y tiempo en marcha")
            .Produces<MetaResponse>();
        return group;
    }

    private static IResult GetMeta(IOptions<StatisticEngineOptions> options)
    {
        var limits = options.Value.Limits;
        var effectiveLimits = new Dictionary<string, object?>
        {
            ["maxNumericValues"] = limits.MaxNumericValues,
            ["maxColumnLength"] = limits.MaxColumnLength,
            ["maxTextCharacters"] = limits.MaxTextCharacters,
            ["maxParameters"] = limits.MaxParameters,
            ["maxDistributionPoints"] = limits.MaxDistributionPoints,
            ["maxSampleCount"] = limits.MaxSampleCount,
            ["maxRequestBodyBytes"] = limits.MaxRequestBodyBytes,
            ["rateLimitEnabled"] = options.Value.RateLimit.Enabled,
            ["rateLimitPermitLimit"] = options.Value.RateLimit.PermitLimit,
            ["rateLimitWindowSeconds"] = options.Value.RateLimit.WindowSeconds
        };

        var response = new MetaResponse(
            AnalysisMapping.Engine(),
            effectiveLimits,
            options.Value.RequestTimeoutSeconds,
            Stopwatch.GetElapsedTime(StartedTicks).TotalSeconds);

        return Results.Ok(response);
    }
}
