using Microsoft.Extensions.Options;
using StatisticEngine.Api.Contracts;
using StatisticEngine.Api.Infrastructure;
using StatisticEngine.Application.Ports;
using StatisticEngine.Application.UseCases;
using StatisticEngine.Domain.Analysis;

namespace StatisticEngine.Api.Endpoints;

/// <summary>Endpoints del catálogo y de ejecución de análisis.</summary>
public static class AnalysesEndpoints
{
    public static RouteGroupBuilder MapAnalysesEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/analyses", GetCatalog)
            .WithName("GetAnalyses")
            .WithSummary("Catálogo de análisis disponibles")
            .Produces<CatalogResponse>();

        group.MapGet("/analyses/{id}", GetAnalysis)
            .WithName("GetAnalysis")
            .Produces<AnalysisDefinitionDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/analyses/{id}/run", Run)
            .WithName("RunAnalysis")
            .WithSummary("Ejecuta un análisis del catálogo")
            .Produces<AnalysisResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/analyses/{id}/validate", Validate)
            .WithName("ValidateAnalysis")
            .Produces<ValidateResponse>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return group;
    }

    private static IResult GetCatalog(string? category)
    {
        IReadOnlyList<AnalysisDefinition> analyses = AnalysisCatalog.All;
        if (!string.IsNullOrWhiteSpace(category))
        {
            analyses = analyses
                .Where(analysis => string.Equals(analysis.Category, category, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        var response = new CatalogResponse(
            AnalysisMapping.Engine(),
            AnalysisCatalog.Categories,
            AnalysisMapping.ToDtos(analyses),
            AnalysisCatalog.Categories
                .Select(category => new CategoryGroupDto(
                    category,
                    analyses.Where(analysis => analysis.Category == category).Select(analysis => analysis.Id).ToArray()))
                .Where(group => group.Ids.Count > 0)
                .ToArray());

        return Results.Ok(response);
    }

    private static IResult GetAnalysis(string id, HttpContext context)
    {
        var definition = AnalysisCatalog.Find(id);
        return definition is null
            ? ApiErrors.UnknownAnalysis(id, context)
            : Results.Ok(AnalysisMapping.ToDto(definition));
    }

    private static IResult Run(
        string id,
        AnalysisRunRequest? body,
        string? format,
        HttpContext context,
        IAnalysisRunner runner,
        IOptions<StatisticEngineOptions> options)
    {
        if (AnalysisCatalog.Find(id) is null) return ApiErrors.UnknownAnalysis(id, context);

        var limitError = RequestLimits.CheckAnalysis(body, options.Value.Limits);
        if (limitError is not null)
        {
            return ApiErrors.Create(
                StatusCodes.Status413PayloadTooLarge,
                ApiErrors.PayloadTooLarge,
                limitError,
                context,
                id);
        }

        var result = runner.Run(id, AnalysisMapping.ToInput(body));
        if (result.IsError) return ApiErrors.FromDomain(result.Error, context, id);

        return string.Equals(format, "text", StringComparison.OrdinalIgnoreCase)
            ? Results.Text(AnalysisTextFormatter.Format(result.Value), "text/plain; charset=utf-8")
            : Results.Ok(result.Value);
    }

    private static IResult Validate(string id, AnalysisRunRequest? body, HttpContext context)
    {
        if (AnalysisCatalog.Find(id) is null) return ApiErrors.UnknownAnalysis(id, context);

        var result = AnalysisService.Validate(id, AnalysisMapping.ToInput(body));
        return Results.Ok(new ValidateResponse(
            result.IsOk,
            result.IsOk ? null : new ValidationErrorDto(result.Error.WireCode, result.Error.Message)));
    }
}
