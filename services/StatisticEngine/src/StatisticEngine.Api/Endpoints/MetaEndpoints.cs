using System.Diagnostics;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
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

        group.MapGet("/endpoints", GetEndpoints)
            .WithName("GetEndpoints")
            .WithSummary("Inventario de rutas publicadas por el servicio")
            .Produces<EndpointInventory>();
        return group;
    }

    /// <summary>
    /// Recorre el enrutador en caliente: es la lista real de rutas, no una copia en un documento.
    /// </summary>
    private static IResult GetEndpoints(IEnumerable<EndpointDataSource> sources)
    {
        var items = new List<EndpointDescriptor>();
        foreach (var endpoint in sources.SelectMany(source => source.Endpoints))
        {
            if (endpoint is not RouteEndpoint route) continue;

            var pattern = route.RoutePattern.RawText;
            if (string.IsNullOrEmpty(pattern)) continue;

            var methods = route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"];
            var name = route.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
            var summary = route.Metadata.GetMetadata<IEndpointSummaryMetadata>()?.Summary;
            foreach (var method in methods)
            {
                items.Add(new EndpointDescriptor(method, "/" + pattern.TrimStart('/'), name, summary));
            }
        }

        return Results.Ok(new EndpointInventory(
            items.OrderBy(item => item.Path, StringComparer.Ordinal)
                 .ThenBy(item => item.Method, StringComparer.Ordinal)
                 .ToArray()));
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
