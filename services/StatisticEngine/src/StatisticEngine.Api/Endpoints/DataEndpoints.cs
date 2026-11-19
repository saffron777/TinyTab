using Microsoft.Extensions.Options;
using StatisticEngine.Api.Contracts;
using StatisticEngine.Api.Infrastructure;
using StatisticEngine.Application.UseCases;
using StatisticEngine.Domain.Analysis;

namespace StatisticEngine.Api.Endpoints;

/// <summary>Endpoints de las operaciones de datos (frames tipados, transformaciones y datasets de salida).</summary>
public static class DataEndpoints
{
    public static RouteGroupBuilder MapDataEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/data", GetCatalog)
            .WithName("GetDataOperations")
            .WithSummary("Catálogo de operaciones de datos")
            .Produces<DataCatalogResponse>();

        group.MapGet("/data/{id}", GetOperation)
            .WithName("GetDataOperation")
            .WithSummary("Definición de una operación de datos")
            .Produces<AnalysisDefinitionDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/data/{id}/run", Run)
            .WithName("RunDataOperation")
            .WithSummary("Ejecuta una operación sobre un frame tipado")
            .Produces<AnalysisResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithRequestTimeout("heavy");

        group.MapPost("/data/{id}/validate", Validate)
            .WithName("ValidateDataOperation")
            .WithSummary("Valida la entrada de una operación de datos sin ejecutarla")
            .Produces<ValidateResponse>()
            .WithRequestTimeout("heavy");

        return group;
    }

    private static IResult GetCatalog() =>
        Results.Ok(new DataCatalogResponse(
            AnalysisMapping.Engine(),
            AnalysisMapping.ToDtos(DataCatalog.All)));

    private static IResult GetOperation(string id, HttpContext context)
    {
        var operation = DataCatalog.Find(id);
        return operation is null
            ? ApiErrors.UnknownDataOperation(id, context)
            : Results.Ok(AnalysisMapping.ToDto(operation));
    }

    private static IResult Run(
        string id,
        DataRunRequest? body,
        string? format,
        HttpContext context,
        IOptions<StatisticEngineOptions> options)
    {
        if (DataCatalog.Find(id) is null) return ApiErrors.UnknownDataOperation(id, context);

        var limitError = RequestLimits.CheckData(body, options.Value.Limits);
        if (limitError is not null)
        {
            return ApiErrors.Create(
                StatusCodes.Status413PayloadTooLarge,
                ApiErrors.PayloadTooLarge,
                limitError,
                context,
                id);
        }

        var inputResult = AnalysisMapping.ToInput(body);
        if (inputResult.IsError) return ApiErrors.FromDomain(inputResult.Error, context, id);

        var result = DataService.Run(id, inputResult.Value);
        if (result.IsError) return ApiErrors.FromDomain(result.Error, context, id);

        return string.Equals(format, "text", StringComparison.OrdinalIgnoreCase)
            ? Results.Text(AnalysisTextFormatter.Format(result.Value), "text/plain; charset=utf-8")
            : Results.Ok(result.Value);
    }

    private static IResult Validate(string id, DataRunRequest? body, HttpContext context)
    {
        if (DataCatalog.Find(id) is null) return ApiErrors.UnknownDataOperation(id, context);

        var inputResult = AnalysisMapping.ToInput(body);
        if (inputResult.IsError)
        {
            return Results.Ok(new ValidateResponse(
                false,
                new ValidationErrorDto(inputResult.Error.WireCode, inputResult.Error.Message)));
        }

        var result = DataService.Validate(id, inputResult.Value);
        return Results.Ok(new ValidateResponse(
            result.IsOk,
            result.IsOk ? null : new ValidationErrorDto(result.Error.WireCode, result.Error.Message)));
    }
}
