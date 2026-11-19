namespace StatisticEngine.Api.Infrastructure;

/// <summary>Configuración del servicio (sección <c>StatisticEngine</c> de appsettings o variables de entorno).</summary>
public sealed class StatisticEngineOptions
{
    public const string SectionName = "StatisticEngine";

    public LimitsOptions Limits { get; set; } = new();

    public RateLimitOptions RateLimit { get; set; } = new();

    public AuthOptions Auth { get; set; } = new();

    public CorsOptions Cors { get; set; } = new();

    public ProxyOptions Proxy { get; set; } = new();

    /// <summary>Documentación de la API: documento OpenAPI y UI Scalar.</summary>
    public ApiExplorerOptions ApiExplorer { get; set; } = new();

    /// <summary>Tiempo máximo de una petición antes de responder 504.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;
}

/// <summary>Publicación del contrato OpenAPI y de la documentación interactiva.</summary>
public sealed class ApiExplorerOptions
{
    public bool Enabled { get; set; } = true;
}

/// <summary>Confianza en cabeceras <c>X-Forwarded-*</c> cuando el servicio está detrás de un proxy.</summary>
public sealed class ProxyOptions
{
    public bool Enabled { get; set; }
}

public sealed class LimitsOptions
{
    public int MaxNumericValues { get; set; } = 1_000_000;

    public int MaxColumnLength { get; set; } = 250_000;

    public int MaxTextCharacters { get; set; } = 2_000_000;

    public int MaxParameters { get; set; } = 50;

    public int MaxDistributionPoints { get; set; } = 10_000;

    public int MaxSampleCount { get; set; } = 100_000;

    /// <summary>Máximo de filas de un frame tipado (análisis extendidos y operaciones de datos).</summary>
    public int MaxDataRows { get; set; } = 250_000;

    /// <summary>Máximo de valores totales de un frame (incluidos los datasets auxiliares).</summary>
    public long MaxDatasetValues { get; set; } = 4_000_000;

    /// <summary>Máximo de filas de una tabla de salida antes de recortarla.</summary>
    public int MaxTableRows { get; set; } = 20_000;

    /// <summary>Máximo de puntos de una serie de un gráfico de salida.</summary>
    public int MaxChartPoints { get; set; } = 10_000;

    /// <summary>Longitud máxima de una expresión del lenguaje de fórmulas.</summary>
    public int MaxExpressionLength { get; set; } = 1_000;

    /// <summary>Tiempo máximo de los análisis marcados como pesados.</summary>
    public int HeavyRequestTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Tamaño máximo del cuerpo. Coherente con 1.000.000 de valores numéricos en JSON
    /// (≈ 10-24 bytes por valor): 32 MiB deja margen sobre el peor caso realista.
    /// </summary>
    public long MaxRequestBodyBytes { get; set; } = 32L * 1024 * 1024;
}

public sealed class RateLimitOptions
{
    public bool Enabled { get; set; } = true;

    public int PermitLimit { get; set; } = 60;

    public int WindowSeconds { get; set; } = 60;
}

public sealed class AuthOptions
{
    /// <summary>Si está vacío, el API no exige autenticación.</summary>
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class CorsOptions
{
    /// <summary>Orígenes permitidos; vacío deshabilita CORS.</summary>
    public string[] AllowedOrigins { get; set; } = [];
}
