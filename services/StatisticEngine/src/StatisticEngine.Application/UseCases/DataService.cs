using StatisticEngine.Domain;
using StatisticEngine.Domain.Analysis;

namespace StatisticEngine.Application.UseCases;

/// <summary>
/// Casos de uso de las operaciones de datos (<c>/api/v1/data</c>): catálogo, validación y ejecución
/// sobre frames tipados. Comparten el modelo de definición de los análisis y siempre validan roles y
/// parámetros antes de ejecutar.
/// </summary>
public static class DataService
{
    public static IReadOnlyList<AnalysisDefinition> Catalog() => DataCatalog.All;

    public static AnalysisDefinition? Find(string id) => DataCatalog.Find(id);

    public static Result<Unit> Validate(string id, AnalysisInput input)
    {
        var operation = DataCatalog.Find(id);
        if (operation is null)
        {
            return Result<Unit>.Err(DomainErrorCode.InvalidInput, $"Operación de datos desconocida: {id}");
        }
        return AnalysisValidator.Validate(operation, input);
    }

    public static Result<AnalysisResult> Run(string id, AnalysisInput input)
    {
        var operation = DataCatalog.Find(id);
        if (operation is null)
        {
            return Result<AnalysisResult>.Err(DomainErrorCode.InvalidInput, $"Operación de datos desconocida: {id}");
        }

        var validation = AnalysisValidator.Validate(operation, input);
        if (validation.IsError)
        {
            return Result<AnalysisResult>.Err(validation.Error.Code, validation.Error.Message);
        }

        return operation.Run(input);
    }
}
