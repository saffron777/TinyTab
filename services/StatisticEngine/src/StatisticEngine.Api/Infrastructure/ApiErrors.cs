using StatisticEngine.Domain;

namespace StatisticEngine.Api.Infrastructure;

/// <summary>Mapeo de errores de dominio y de límites a respuestas RFC 7807.</summary>
public static class ApiErrors
{
    public const string NotFound = "NOT_FOUND";

    public const string PayloadTooLarge = "API_PAYLOAD_TOO_LARGE";

    public const string RateLimited = "API_RATE_LIMITED";

    public const string Unauthorized = "API_UNAUTHORIZED";

    public const string Timeout = "API_TIMEOUT";

    public const string BadRequest = "API_BAD_REQUEST";

    /// <summary>Código "wire" para respuestas generadas por middleware (429, 504, 413, 404…).</summary>
    public static string CodeForStatus(int status) =>
        status switch
        {
            StatusCodes.Status400BadRequest => BadRequest,
            StatusCodes.Status401Unauthorized => Unauthorized,
            StatusCodes.Status404NotFound => NotFound,
            StatusCodes.Status413PayloadTooLarge => PayloadTooLarge,
            StatusCodes.Status429TooManyRequests => RateLimited,
            StatusCodes.Status500InternalServerError => "UNKNOWN",
            StatusCodes.Status504GatewayTimeout => Timeout,
            _ => $"HTTP_{status}"
        };

    public static IResult FromDomain(DomainError error, HttpContext context, string? analysisId = null)
    {
        var status = error.Code switch
        {
            DomainErrorCode.InsufficientData => StatusCodes.Status422UnprocessableEntity,
            DomainErrorCode.SingularMatrix => StatusCodes.Status422UnprocessableEntity,
            DomainErrorCode.NonConvergence => StatusCodes.Status422UnprocessableEntity,
            DomainErrorCode.NotSupported => StatusCodes.Status422UnprocessableEntity,
            DomainErrorCode.Unknown => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status400BadRequest
        };
        return Create(status, error.WireCode, error.Message, context, analysisId, error.Details);
    }

    public static IResult Create(
        int status,
        string code,
        string title,
        HttpContext context,
        string? analysisId = null,
        IReadOnlyDictionary<string, object?>? details = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = code,
            ["traceId"] = context.TraceIdentifier
        };
        if (analysisId is not null) extensions["analysisId"] = analysisId;
        if (details is { Count: > 0 }) extensions["details"] = details;

        return Results.Problem(
            type: $"https://statisticengine.local/errors/{code.ToLowerInvariant()}",
            title: title,
            statusCode: status,
            extensions: extensions);
    }

    public static IResult UnknownAnalysis(string analysisId, HttpContext context) =>
        Create(
            StatusCodes.Status404NotFound,
            NotFound,
            $"Análisis desconocido: {analysisId}",
            context,
            analysisId);

    public static IResult UnknownDistribution(string distributionId, HttpContext context) =>
        Create(
            StatusCodes.Status404NotFound,
            NotFound,
            $"Distribución desconocida: {distributionId}",
            context);
}
