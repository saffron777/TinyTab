using StatisticEngine.Domain.Random;

namespace StatisticEngine.Domain.Doe;

/// <summary>Definición de un factor experimental con sus niveles bajo y alto.</summary>
public sealed record FactorDef(string Name, double Low, double High);

/// <summary>Corrida de un diseño: orden estándar, orden de corrida y niveles codificados/reales.</summary>
public sealed record DesignRun(double StandardOrder, double RunOrder, double[] Coded, double[] Actual);

/// <summary>Diseño experimental completo generado por el motor.</summary>
public sealed record DoeDesign(
    string Type,
    FactorDef[] Factors,
    DesignRun[] Runs,
    double ReplicateCount,
    double CenterPoints,
    double Seed);

/// <summary>Resumen de un diseño: número de corridas, factores, tipo y puntos centrales.</summary>
public sealed record DoeSummary(double Runs, double Factors, string Type, double CenterPoints);

/// <summary>
/// Generación de diseños de experimentos (port 1:1 de <c>doe.ts</c>): factorial 2^k,
/// Plackett-Burman de 12 corridas y compuesto central con aleatorización xoshiro128**.
/// </summary>
public static class Designs
{
    /// <summary>Matriz Plackett-Burman estándar de 12 corridas y 11 factores.</summary>
    private static readonly double[][] Pb12 =
    [
        [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1],
        [-1, 1, -1, 1, 1, 1, -1, -1, -1, 1, -1],
        [-1, -1, 1, -1, 1, 1, 1, -1, -1, -1, 1],
        [1, -1, -1, 1, -1, 1, 1, 1, -1, -1, -1],
        [-1, 1, -1, -1, 1, -1, 1, 1, 1, -1, -1],
        [-1, -1, 1, -1, -1, 1, -1, 1, 1, 1, -1],
        [-1, -1, -1, 1, -1, -1, 1, -1, 1, 1, 1],
        [1, -1, -1, -1, 1, -1, -1, 1, -1, 1, 1],
        [1, 1, -1, -1, -1, 1, -1, -1, 1, -1, 1],
        [1, 1, 1, -1, -1, -1, 1, -1, -1, 1, -1],
        [-1, 1, 1, 1, -1, -1, -1, 1, -1, -1, 1],
        [1, -1, 1, 1, 1, -1, -1, -1, 1, -1, -1]
    ];

    /// <summary>Diseño factorial completo 2^k, con réplicas y puntos centrales opcionales.</summary>
    public static Result<DoeDesign> CreateFactorialDesign(
        FactorDef[] factors,
        int replicates = 1,
        int centerPoints = 0,
        int seed = 20261023,
        bool randomize = true)
    {
        var k = factors.Length;
        if (k < 1 || k > 8)
        {
            return Result<DoeDesign>.Err(DomainErrorCode.InvalidParameter, "Se admiten entre 1 y 8 factores.");
        }

        var replicateCount = Math.Max(1, replicates);
        var centerCount = Math.Max(0, centerPoints);
        var total = 1 << k;
        var baseRuns = new DesignRun[total];
        for (var index = 0; index < total; index++)
        {
            var coded = new double[k];
            for (var bit = 0; bit < k; bit++)
            {
                coded[bit] = ((index >> (k - 1 - bit)) & 1) != 0 ? 1 : -1;
            }
            baseRuns[index] = new DesignRun(index + 1, index + 1, coded, ApplyFactors(factors, coded));
        }

        var runs = new List<DesignRun>(total * replicateCount + centerCount);
        for (var replicate = 0; replicate < replicateCount; replicate++)
        {
            foreach (var run in baseRuns)
            {
                runs.Add(new DesignRun(run.StandardOrder, run.RunOrder, [.. run.Coded], [.. run.Actual]));
            }
        }
        for (var center = 0; center < centerCount; center++)
        {
            var coded = new double[k];
            // paridad: doe.ts:70-73 — los puntos centrales siguen el orden estándar correlativo.
            runs.Add(new DesignRun(runs.Count + 1, runs.Count + 1, coded, ApplyFactors(factors, coded)));
        }

        DesignRun[] ordered = randomize ? Randomize([.. runs], seed) : [.. runs];
        return Result<DoeDesign>.Ok(new DoeDesign(
            "factorial",
            factors,
            ordered,
            replicateCount,
            centerCount,
            seed));
    }

    /// <summary>Diseño Plackett-Burman de 12 corridas para entre 2 y 11 factores.</summary>
    public static Result<DoeDesign> CreatePlackettBurman(FactorDef[] factors, int seed = 20261023)
    {
        if (factors.Length < 2 || factors.Length > 11)
        {
            return Result<DoeDesign>.Err(DomainErrorCode.InvalidParameter, "Plackett-Burman admite entre 2 y 11 factores.");
        }

        var runs = new DesignRun[Pb12.Length];
        for (var index = 0; index < Pb12.Length; index++)
        {
            var coded = new double[factors.Length];
            for (var factor = 0; factor < factors.Length; factor++) coded[factor] = Pb12[index][factor];
            runs[index] = new DesignRun(index + 1, index + 1, coded, ApplyFactors(factors, coded));
        }

        return Result<DoeDesign>.Ok(new DoeDesign(
            "plackett-burman",
            factors,
            Randomize(runs, seed),
            1,
            0,
            seed));
    }

    /// <summary>Diseño compuesto central con puntos axiales en ±2^(k/4) y puntos centrales.</summary>
    public static Result<DoeDesign> CreateCcd(FactorDef[] factors, int centerPoints = 3, int seed = 20261023)
    {
        var k = factors.Length;
        if (k < 2 || k > 5)
        {
            return Result<DoeDesign>.Err(DomainErrorCode.InvalidParameter, "El diseño compuesto central admite entre 2 y 5 factores.");
        }

        var alpha = Math.Pow(1 << k, 1.0 / 4);
        var runs = new List<DesignRun>();
        var total = 1 << k;
        for (var index = 0; index < total; index++)
        {
            var coded = new double[k];
            for (var bit = 0; bit < k; bit++)
            {
                coded[bit] = ((index >> (k - 1 - bit)) & 1) != 0 ? 1 : -1;
            }
            runs.Add(new DesignRun(runs.Count + 1, runs.Count + 1, coded, ApplyFactors(factors, coded)));
        }
        for (var axis = 0; axis < k; axis++)
        {
            for (var side = 0; side < 2; side++)
            {
                var coded = new double[k];
                coded[axis] = (side == 0 ? -1 : 1) * alpha;
                runs.Add(new DesignRun(runs.Count + 1, runs.Count + 1, coded, ApplyFactors(factors, coded)));
            }
        }
        var centerCount = Math.Max(0, centerPoints);
        for (var center = 0; center < centerCount; center++)
        {
            var coded = new double[k];
            runs.Add(new DesignRun(runs.Count + 1, runs.Count + 1, coded, ApplyFactors(factors, coded)));
        }

        return Result<DoeDesign>.Ok(new DoeDesign(
            "ccd",
            factors,
            Randomize([.. runs], seed),
            1,
            centerCount,
            seed));
    }

    /// <summary>Resumen del diseño para presentación.</summary>
    public static DoeSummary DesignSummary(DoeDesign design) =>
        new(design.Runs.Length, design.Factors.Length, design.Type, design.CenterPoints);

    private static double[] ApplyFactors(FactorDef[] factors, double[] coded)
    {
        var output = new double[coded.Length];
        for (var index = 0; index < coded.Length; index++)
        {
            var factor = factors[index];
            var center = (factor.Low + factor.High) / 2;
            var half = (factor.High - factor.Low) / 2;
            output[index] = center + half * coded[index];
        }
        return output;
    }

    /// <summary>Aleatorización Fisher-Yates con el generador del motor, idéntica a <c>randomize</c>.</summary>
    private static DesignRun[] Randomize(DesignRun[] runs, int seed)
    {
        var rng = new Xoshiro128(unchecked((uint)seed));
        var shuffled = new DesignRun[runs.Length];
        Array.Copy(runs, shuffled, runs.Length);
        for (var i = shuffled.Length - 1; i > 0; i--)
        {
            var j = (int)Math.Floor(rng.Next() * (i + 1));
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        var output = new DesignRun[shuffled.Length];
        for (var index = 0; index < shuffled.Length; index++)
        {
            output[index] = shuffled[index] with { RunOrder = index + 1 };
        }
        return output;
    }
}
