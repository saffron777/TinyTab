namespace StatisticEngine.Application;

/// <summary>Identidad del motor expuesta por el API y por el catálogo.</summary>
public static class EngineInfo
{
    public const string Name = "StatisticEngine";

    /// <summary>Versión del API: 2.0.0 añade el catálogo extendido Sigma y los endpoints de datos.</summary>
    public const string Version = "2.0.0";

    /// <summary>Versión del motor TypeScript contra la que se generaron los vectores de paridad.</summary>
    public const string ParityBaseline = "tinytab@0.1.0/src/domain";

    /// <summary>Versión de Sigma contra la que se generan los vectores de los análisis extendidos.</summary>
    public const string ExtensionsBaseline = "sigma@5c378d7";
}
