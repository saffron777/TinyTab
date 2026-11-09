namespace StatisticEngine.Application;

/// <summary>Identidad del motor expuesta por el API y por el catálogo.</summary>
public static class EngineInfo
{
    public const string Name = "StatisticEngine";

    public const string Version = "1.0.0";

    /// <summary>Versión del motor TypeScript contra la que se generaron los vectores de paridad.</summary>
    public const string ParityBaseline = "tinytab@0.1.0/src/domain";
}
