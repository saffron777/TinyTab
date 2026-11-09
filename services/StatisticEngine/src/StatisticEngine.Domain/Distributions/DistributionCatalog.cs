namespace StatisticEngine.Domain.Distributions;

/// <summary>
/// Catálogo de las 21 distribuciones del motor, en el mismo orden que
/// <c>DISTRIBUTIONS</c> de <c>distributions.ts</c>.
/// </summary>
public static class DistributionCatalog
{
    /// <summary>Todas las distribuciones en el orden del catálogo TypeScript.</summary>
    public static IReadOnlyList<IDistribution> All { get; } =
    [
        new NormalDistribution(),
        new StudentTDistribution(),
        new ChiSquareDistribution(),
        new FisherFDistribution(),
        new GammaDistribution(),
        new BetaDistribution(),
        new ExponentialDistribution(),
        new WeibullDistribution(),
        new LogNormalDistribution(),
        new UniformDistribution(),
        new LogisticDistribution(),
        new CauchyDistribution(),
        new LaplaceDistribution(),
        new TriangularDistribution(),
        new SmallestExtremeDistribution(),
        new LargestExtremeDistribution(),
        new FoldedNormalDistribution(),
        new BinomialDistribution(),
        new PoissonDistribution(),
        new HypergeometricDistribution(),
        new NegativeBinomialDistribution()
    ];

    /// <summary>Busca una distribución por identificador; error de programación si no existe.</summary>
    public static IDistribution Get(string id)
    {
        foreach (var distribution in All)
        {
            if (string.Equals(distribution.Id, id, StringComparison.Ordinal)) return distribution;
        }
        throw new InvalidOperationException($"Distribución desconocida: {id}");
    }

    /// <summary><c>defaultParams</c>: valores por defecto en el orden de los parámetros.</summary>
    public static DistParams DefaultParams(IDistribution distribution)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var parameter in distribution.Parameters) values[parameter.Key] = parameter.DefaultValue;
        return new DistParams(values);
    }

    /// <summary><c>validateDistribution</c>: delega la validación en la distribución.</summary>
    public static Result<Unit> Validate(IDistribution distribution, DistParams parameters) =>
        distribution.Validate(parameters);
}
