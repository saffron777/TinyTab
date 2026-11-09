using StatisticEngine.Domain.Random;

namespace StatisticEngine.Domain.Distributions;

/// <summary>Naturaleza de la variable aleatoria de una distribución.</summary>
public enum DistributionKind
{
    Continuous,
    Discrete
}

/// <summary>Nombre "wire" del tipo de distribución, idéntico al del motor TypeScript.</summary>
public static class DistributionKinds
{
    public static string ToWire(DistributionKind kind) =>
        kind == DistributionKind.Discrete ? "discrete" : "continuous";
}

/// <summary>
/// Metadatos de un parámetro de distribución (equivalente a <c>DistParamDef</c> de
/// <c>distributions.ts</c>). Los límites ausentes son <c>null</c> y se serializan como
/// propiedad explícita en los vectores dorados.
/// </summary>
public sealed record DistParamDef(
    string Key,
    string Label,
    double DefaultValue,
    bool Integer = false,
    double? MinExclusive = null,
    double? MinInclusive = null,
    double? MaxInclusive = null);

/// <summary>
/// Valores de los parámetros de una distribución. Igual que el motor TypeScript, una clave
/// ausente equivale a <c>undefined</c> y por tanto a <c>NaN</c> (<c>p[key] ?? NaN</c>).
/// </summary>
public sealed class DistParams
{
    private readonly Dictionary<string, double> _values;

    public DistParams(IReadOnlyDictionary<string, double> values) =>
        _values = new Dictionary<string, double>(values, StringComparer.Ordinal);

    /// <summary>Acceso por clave; devuelve <c>NaN</c> si la clave no existe.</summary>
    public double this[string key] => _values.TryGetValue(key, out var value) ? value : double.NaN;

    /// <summary>Diccionario subyacente en orden de inserción.</summary>
    public IReadOnlyDictionary<string, double> Values => _values;
}

/// <summary>Soporte de la distribución (equivalente al <c>{lower, upper}</c> del motor TS).</summary>
public readonly record struct DistributionSupport(double Lower, double Upper);

/// <summary>
/// Contrato de una distribución de probabilidad (equivalente a la interfaz <c>Distribution</c>
/// de <c>distributions.ts</c>).
/// </summary>
public interface IDistribution
{
    /// <summary>Identificador del catálogo (<c>normal</c>, <c>t</c>, <c>chi2</c>, …).</summary>
    string Id { get; }

    /// <summary>Etiqueta legible, idéntica a la del motor.</summary>
    string Label { get; }

    /// <summary>Continua o discreta.</summary>
    DistributionKind Kind { get; }

    /// <summary>Definición ordenada de los parámetros.</summary>
    IReadOnlyList<DistParamDef> Parameters { get; }

    /// <summary>Función de masa/densidad.</summary>
    double Pdf(double x, DistParams p);

    /// <summary>Función de distribución acumulada.</summary>
    double Cdf(double x, DistParams p);

    /// <summary>Inversa de la CDF (cuantil).</summary>
    double InvCdf(double q, DistParams p);

    /// <summary>Una extracción con el generador indicado.</summary>
    double Random(Xoshiro128 rng, DistParams p);

    /// <summary>Validación de parámetros; errores de usuario, nunca excepciones.</summary>
    Result<Unit> Validate(DistParams p);

    /// <summary>Media teórica (NaN si no existe).</summary>
    double Mean(DistParams p);

    /// <summary>Desviación estándar teórica (NaN si no existe).</summary>
    double Sd(DistParams p);

    /// <summary>Soporte de la variable.</summary>
    DistributionSupport Support(DistParams p);
}
