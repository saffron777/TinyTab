using StatisticEngine.Domain;
using StatisticEngine.Domain.Distributions;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Random;

namespace StatisticEngine.Application.UseCases;

public sealed record DistributionSupportInfo(double Lower, double Upper);

public sealed record DistributionEvaluation(
    double[] Pdf,
    double[] Cdf,
    double[] InvCdf,
    double Mean,
    double Sd,
    DistributionSupportInfo Support);

public sealed record DistributionSample(double[] Values, int Count, uint Seed);

/// <summary>
/// Casos de uso de las 21 distribuciones: catálogo, evaluación de pdf/cdf/inversa y muestreo
/// determinista con semilla explícita.
/// </summary>
public static class DistributionService
{
    public const uint DefaultSeed = Xoshiro128.DefaultSeed;

    public static IReadOnlyList<IDistribution> All => DistributionCatalog.All;

    public static IDistribution? Find(string id) =>
        DistributionCatalog.All.FirstOrDefault(distribution => string.Equals(distribution.Id, id, StringComparison.Ordinal));

    public static Result<DistributionEvaluation> Evaluate(
        IDistribution distribution,
        IReadOnlyDictionary<string, double>? parameters,
        double[] xs,
        double[] probabilities)
    {
        var resolved = Resolve(distribution, parameters);
        if (resolved.IsError)
        {
            return Result<DistributionEvaluation>.Err(resolved.Error.Code, resolved.Error.Message);
        }

        var values = resolved.Value;
        var pdf = xs.Select(x => distribution.Pdf(x, values)).ToArray();
        var cdf = xs.Select(x => distribution.Cdf(x, values)).ToArray();
        var invCdf = probabilities.Select(p => distribution.InvCdf(p, values)).ToArray();
        var support = distribution.Support(values);
        return Result<DistributionEvaluation>.Ok(new DistributionEvaluation(
            pdf,
            cdf,
            invCdf,
            distribution.Mean(values),
            distribution.Sd(values),
            new DistributionSupportInfo(support.Lower, support.Upper)));
    }

    public static Result<DistributionSample> Sample(
        IDistribution distribution,
        IReadOnlyDictionary<string, double>? parameters,
        int count,
        uint seed)
    {
        if (count <= 0)
        {
            return Result<DistributionSample>.Err(DomainErrorCode.InvalidParameter, "La cantidad de muestras debe ser positiva.");
        }

        var resolved = Resolve(distribution, parameters);
        if (resolved.IsError)
        {
            return Result<DistributionSample>.Err(resolved.Error.Code, resolved.Error.Message);
        }

        var values = resolved.Value;
        var rng = new Xoshiro128(seed);
        var output = new double[count];
        for (var index = 0; index < count; index++) output[index] = distribution.Random(rng, values);
        return Result<DistributionSample>.Ok(new DistributionSample(output, count, seed));
    }

    /// <summary>Parámetros por defecto del motor sobrescritos por los del cliente, ya validados.</summary>
    private static Result<DistParams> Resolve(IDistribution distribution, IReadOnlyDictionary<string, double>? parameters)
    {
        var values = DistributionCatalog.DefaultParams(distribution).Values
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (parameters is not null)
        {
            foreach (var (key, value) in parameters) values[key] = value;
        }

        var resolved = new DistParams(values);
        var validation = DistributionCatalog.Validate(distribution, resolved);
        return validation.IsOk
            ? Result<DistParams>.Ok(resolved)
            : Result<DistParams>.Err(validation.Error.Code, validation.Error.Message);
    }

    /// <summary>Formatea un valor con <c>fmt</c> (utilidad para diagnósticos).</summary>
    public static string Format(double value) => NumberFormat.Fmt(value);
}
