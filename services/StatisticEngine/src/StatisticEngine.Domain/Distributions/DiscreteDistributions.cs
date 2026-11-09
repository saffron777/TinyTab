using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Random;

namespace StatisticEngine.Domain.Distributions;

// Port 1:1 de las distribuciones discretas de src/domain/stats/distributions.ts.

/// <summary>Binomial(n, p).</summary>
public sealed class BinomialDistribution : IDistribution
{
    public string Id => "binomial";

    public string Label => "Binomial";

    public DistributionKind Kind => DistributionKind.Discrete;

    public IReadOnlyList<DistParamDef> Parameters { get; } =
    [
        new("n", "Número de ensayos", 10, Integer: true, MinInclusive: 1),
        new("p", "Probabilidad de éxito", 0.5, MinExclusive: 0, MaxInclusive: 1)
    ];

    public Result<Unit> Validate(DistParams p)
    {
        if (!JsMath.IsInteger(p["n"]) || p["n"] < 1)
        {
            return Result<Unit>.Err(DomainErrorCode.InvalidParameter, "n debe ser un entero positivo.");
        }
        if (!(p["p"] > 0 && p["p"] <= 1))
        {
            return Result<Unit>.Err(DomainErrorCode.InvalidParameter, "p debe estar en (0, 1].");
        }
        return Result.Ok(Unit.Value);
    }

    public double Pdf(double k, DistParams p) =>
        k < 0 || k > p["n"] || !JsMath.IsInteger(k) ? 0 : Math.Exp(DistributionMath.LogPmfBinomial(p["n"], k, p["p"]));

    public double Cdf(double k, DistParams p) => CdfOf(k, p);

    public double InvCdf(double q, DistParams p) =>
        DistributionMath.DiscreteInvert(k => CdfOf(k, p), q, 0, p["n"]);

    public double Random(Xoshiro128 rng, DistParams p) => rng.Binomial(p["n"], p["p"]);

    public double Mean(DistParams p) => p["n"] * p["p"];

    public double Sd(DistParams p) => Math.Sqrt(p["n"] * p["p"] * (1 - p["p"]));

    public DistributionSupport Support(DistParams p) => new(0, p["n"]);

    private static double CdfOf(double k, DistParams p)
    {
        var n = p["n"];
        if (k < 0) return 0;
        if (k >= n) return 1;
        return SpecialFunctions.Betai(n - Math.Floor(k), Math.Floor(k) + 1, 1 - p["p"]);
    }
}

/// <summary>Poisson(lambda).</summary>
public sealed class PoissonDistribution : IDistribution
{
    public string Id => "poisson";

    public string Label => "Poisson";

    public DistributionKind Kind => DistributionKind.Discrete;

    public IReadOnlyList<DistParamDef> Parameters { get; } = [new("lambda", "Media (λ)", 3, MinExclusive: 0)];

    public Result<Unit> Validate(DistParams p) => DistributionMath.Positive(p, "lambda");

    public double Pdf(double k, DistParams p) =>
        k < 0 || !JsMath.IsInteger(k) ? 0 : Math.Exp(DistributionMath.LogPmfPoisson(p["lambda"], k));

    public double Cdf(double k, DistParams p) =>
        k < 0 ? 0 : SpecialFunctions.Gammq(Math.Floor(k) + 1, p["lambda"]);

    public double InvCdf(double q, DistParams p) =>
        DistributionMath.DiscreteInvert(k => CdfOf(k, p), q, 0, double.PositiveInfinity);

    public double Random(Xoshiro128 rng, DistParams p) => rng.Poisson(p["lambda"]);

    public double Mean(DistParams p) => p["lambda"];

    public double Sd(DistParams p) => Math.Sqrt(p["lambda"]);

    public DistributionSupport Support(DistParams p) => new(0, double.PositiveInfinity);

    private static double CdfOf(double k, DistParams p) =>
        k < 0 ? 0 : SpecialFunctions.Gammq(Math.Floor(k) + 1, p["lambda"]);
}

/// <summary>Hipergeométrica(population, successes, sample).</summary>
public sealed class HypergeometricDistribution : IDistribution
{
    public string Id => "hypergeometric";

    public string Label => "Hipergeométrica";

    public DistributionKind Kind => DistributionKind.Discrete;

    public IReadOnlyList<DistParamDef> Parameters { get; } =
    [
        new("population", "Tamaño de población (N)", 50, Integer: true, MinInclusive: 1),
        new("successes", "Éxitos en la población (K)", 10, Integer: true, MinInclusive: 0),
        new("sample", "Tamaño de muestra (n)", 5, Integer: true, MinInclusive: 0)
    ];

    public Result<Unit> Validate(DistParams p) =>
        p["successes"] <= p["population"] && p["sample"] <= p["population"]
            ? Result.Ok(Unit.Value)
            : Result<Unit>.Err(DomainErrorCode.InvalidParameter, "Se requiere K ≤ N y n ≤ N.");

    public double Pdf(double k, DistParams p) => PdfOf(k, p);

    public double Cdf(double k, DistParams p) => CdfOf(k, p);

    public double InvCdf(double q, DistParams p) =>
        DistributionMath.DiscreteInvert(
            k => CdfOf(k, p),
            q,
            0,
            JsMath.Min(p["successes"], p["sample"]));

    public double Random(Xoshiro128 rng, DistParams p)
    {
        var remainingSuccesses = p["successes"];
        var remainingPopulation = p["population"];
        var drawn = 0.0;
        for (var i = 0.0; i < p["sample"]; i += 1)
        {
            if (rng.Next() < remainingSuccesses / remainingPopulation)
            {
                drawn += 1;
                remainingSuccesses -= 1;
            }
            remainingPopulation -= 1;
        }
        return drawn;
    }

    public double Mean(DistParams p) => p["sample"] * p["successes"] / p["population"];

    public double Sd(DistParams p)
    {
        var population = p["population"];
        var successes = p["successes"];
        var sample = p["sample"];
        return Math.Sqrt(sample * successes * (population - successes) * (population - sample) /
                         (population * population * (population - 1)));
    }

    public DistributionSupport Support(DistParams p) =>
        new(
            JsMath.Max(0, p["sample"] - (p["population"] - p["successes"])),
            JsMath.Min(p["successes"], p["sample"]));

    private static double PdfOf(double k, DistParams p)
    {
        var population = p["population"];
        var successes = p["successes"];
        var sample = p["sample"];
        if (k < JsMath.Max(0, sample - (population - successes)) || k > JsMath.Min(successes, sample)) return 0;
        return Math.Exp(
            NumericMath.LogCombination(successes, k) +
            NumericMath.LogCombination(population - successes, sample - k) -
            NumericMath.LogCombination(population, sample));
    }

    private static double CdfOf(double k, DistParams p)
    {
        var total = 0.0;
        for (var i = 0.0; i <= Math.Floor(k); i += 1) total += PdfOf(i, p);
        return JsMath.Min(1, total);
    }
}

/// <summary>Binomial negativa(r, p) sobre el número de fracasos.</summary>
public sealed class NegativeBinomialDistribution : IDistribution
{
    public string Id => "negativeBinomial";

    public string Label => "Binomial negativa";

    public DistributionKind Kind => DistributionKind.Discrete;

    public IReadOnlyList<DistParamDef> Parameters { get; } =
    [
        new("r", "Número de éxitos (r)", 5, Integer: true, MinInclusive: 1),
        new("p", "Probabilidad de éxito", 0.5, MinExclusive: 0, MaxInclusive: 1)
    ];

    public Result<Unit> Validate(DistParams p)
    {
        if (!JsMath.IsInteger(p["r"]) || p["r"] < 1)
        {
            return Result<Unit>.Err(DomainErrorCode.InvalidParameter, "r debe ser un entero positivo.");
        }
        if (!(p["p"] > 0 && p["p"] <= 1))
        {
            return Result<Unit>.Err(DomainErrorCode.InvalidParameter, "p debe estar en (0, 1].");
        }
        return Result.Ok(Unit.Value);
    }

    public double Pdf(double k, DistParams p) => PdfOf(k, p);

    public double Cdf(double k, DistParams p) => CdfOf(k, p);

    public double InvCdf(double q, DistParams p) =>
        DistributionMath.DiscreteInvert(k => CdfOf(k, p), q, 0, double.PositiveInfinity);

    public double Random(Xoshiro128 rng, DistParams p)
    {
        var failures = 0.0;
        var successes = 0.0;
        while (successes < p["r"])
        {
            if (rng.Next() < p["p"]) successes += 1;
            else failures += 1;
        }
        return failures;
    }

    public double Mean(DistParams p) => p["r"] * (1 - p["p"]) / p["p"];

    public double Sd(DistParams p) => Math.Sqrt(p["r"] * (1 - p["p"]) / (p["p"] * p["p"]));

    public DistributionSupport Support(DistParams p) => new(0, double.PositiveInfinity);

    private static double PdfOf(double k, DistParams p)
    {
        if (k < 0 || !JsMath.IsInteger(k)) return 0;
        var r = p["r"];
        return Math.Exp(
            NumericMath.LogCombination(k + r - 1, k) +
            r * Math.Log(p["p"]) +
            k * Math.Log(1 - p["p"]));
    }

    private static double CdfOf(double k, DistParams p)
    {
        var total = 0.0;
        for (var i = 0.0; i <= Math.Floor(k); i += 1) total += PdfOf(i, p);
        return JsMath.Min(1, total);
    }
}
