using Microsoft.Extensions.Options;
using StatisticEngine.Api.Contracts;
using StatisticEngine.Api.Infrastructure;
using StatisticEngine.Application.UseCases;

namespace StatisticEngine.Api.Endpoints;

/// <summary>Endpoints de las 21 distribuciones de probabilidad.</summary>
public static class DistributionEndpoints
{
    public static RouteGroupBuilder MapDistributionEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/distributions", GetDistributions)
            .WithName("GetDistributions")
            .WithSummary("Catálogo de distribuciones con sus parámetros")
            .Produces<DistributionListResponse>();

        group.MapGet("/distributions/{id}", GetDistribution)
            .WithName("GetDistribution")
            .WithSummary("Metadatos de una distribución")
            .Produces<DistributionInfoDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/distributions/{id}/evaluate", Evaluate)
            .WithName("EvaluateDistribution")
            .WithSummary("PDF/PMF, CDF, inversa y momentos para listas de x y de probabilidades")
            .Produces<DistributionEvaluation>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/distributions/{id}/sample", Sample)
            .WithName("SampleDistribution")
            .WithSummary("Muestreo determinista con semilla")
            .Produces<DistributionSample>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        return group;
    }

    private static IResult GetDistributions() =>
        Results.Ok(new DistributionListResponse(
            AnalysisMapping.Engine(),
            DistributionService.All.Select(DistributionMapping.ToDto).ToArray()));

    private static IResult GetDistribution(string id, HttpContext context)
    {
        var distribution = DistributionService.Find(id);
        return distribution is null
            ? ApiErrors.UnknownDistribution(id, context)
            : Results.Ok(DistributionMapping.ToDto(distribution));
    }

    private static IResult Evaluate(string id, DistributionEvaluateRequest? body, HttpContext context, IOptions<StatisticEngineOptions> options)
    {
        var distribution = DistributionService.Find(id);
        if (distribution is null) return ApiErrors.UnknownDistribution(id, context);

        var xs = body?.X ?? [];
        var probabilities = body?.Probabilities ?? [];
        var limitError = RequestLimits.CheckDistributionPoints(xs.Length, options.Value.Limits)
                         ?? RequestLimits.CheckDistributionPoints(probabilities.Length, options.Value.Limits);
        if (limitError is not null)
        {
            return ApiErrors.Create(StatusCodes.Status413PayloadTooLarge, ApiErrors.PayloadTooLarge, limitError, context);
        }

        var result = DistributionService.Evaluate(distribution, body?.Params, xs, probabilities);
        return result.IsOk ? Results.Ok(result.Value) : ApiErrors.FromDomain(result.Error, context);
    }

    private static IResult Sample(string id, DistributionSampleRequest? body, HttpContext context, IOptions<StatisticEngineOptions> options)
    {
        var distribution = DistributionService.Find(id);
        if (distribution is null) return ApiErrors.UnknownDistribution(id, context);

        var count = body?.Count ?? 0;
        if (count <= 0)
        {
            return ApiErrors.Create(
                StatusCodes.Status400BadRequest,
                ApiErrors.BadRequest,
                "La cantidad de muestras debe ser positiva.",
                context);
        }

        var limitError = RequestLimits.CheckSampleCount(count, options.Value.Limits);
        if (limitError is not null)
        {
            return ApiErrors.Create(StatusCodes.Status413PayloadTooLarge, ApiErrors.PayloadTooLarge, limitError, context);
        }

        var seed = body?.Seed ?? DistributionService.DefaultSeed;
        var result = DistributionService.Sample(distribution, body?.Params, count, seed);
        return result.IsOk ? Results.Ok(result.Value) : ApiErrors.FromDomain(result.Error, context);
    }
}
