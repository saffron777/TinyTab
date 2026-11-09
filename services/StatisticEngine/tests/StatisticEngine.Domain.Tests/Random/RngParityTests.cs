using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Random;
using StatisticEngine.Domain.Tests.Parity;
using Xunit;

namespace StatisticEngine.Domain.Tests.Random;

public class RngParityTests
{
    private static readonly Dictionary<string, Func<GoldenCase, object?>> Registry = new(StringComparer.Ordinal)
    {
        ["nextUint32"] = goldenCase => Sequence(goldenCase, (rng, _) => rng.NextUInt32()),
        ["next"] = goldenCase => Sequence(goldenCase, (rng, _) => rng.Next()),
        ["uniform"] = goldenCase => Sequence(goldenCase, (rng, _) => rng.Uniform(GoldenReader.Number(goldenCase.Args[1]), GoldenReader.Number(goldenCase.Args[2]))),
        ["normal"] = goldenCase => Sequence(goldenCase, (rng, _) => rng.Normal()),
        ["exponential"] = goldenCase => Sequence(goldenCase, (rng, _) => rng.Exponential(GoldenReader.Number(goldenCase.Args[1]))),
        ["gamma"] = goldenCase => Sequence(goldenCase, (rng, _) => rng.Gamma(GoldenReader.Number(goldenCase.Args[1]), GoldenReader.Number(goldenCase.Args[2]))),
        ["beta"] = goldenCase => Sequence(goldenCase, (rng, _) => rng.Beta(GoldenReader.Number(goldenCase.Args[1]), GoldenReader.Number(goldenCase.Args[2]))),
        ["weibull"] = goldenCase => Sequence(goldenCase, (rng, _) => rng.Weibull(GoldenReader.Number(goldenCase.Args[1]), GoldenReader.Number(goldenCase.Args[2]))),
        ["lognormal"] = goldenCase => Sequence(goldenCase, (rng, _) => rng.LogNormal(GoldenReader.Number(goldenCase.Args[1]), GoldenReader.Number(goldenCase.Args[2]))),
        ["binomial"] = goldenCase => Sequence(goldenCase, (rng, _) => rng.Binomial(GoldenReader.Number(goldenCase.Args[1]), GoldenReader.Number(goldenCase.Args[2]))),
        ["poisson"] = goldenCase => Sequence(goldenCase, (rng, _) => rng.Poisson(GoldenReader.Number(goldenCase.Args[1]))),
        ["sample"] = goldenCase => Sample(goldenCase)
    };

    private static double[] Sequence(GoldenCase goldenCase, Func<Xoshiro128, GoldenCase, double> next)
    {
        var seed = (uint)GoldenReader.Number(goldenCase.Args[0]);
        // El último argumento de todos los casos del generador es siempre la cantidad de extracciones.
        var count = GoldenReader.Int(goldenCase.Args[^1]);
        var rng = new Xoshiro128(seed);
        var output = new double[count];
        for (var i = 0; i < count; i++) output[i] = next(rng, goldenCase);
        return output;
    }

    private static string[] Sample(GoldenCase goldenCase)
    {
        var seed = (uint)GoldenReader.Number(goldenCase.Args[0]);
        var items = GoldenReader.Texts(goldenCase.Args[1]);
        var count = GoldenReader.Int(goldenCase.Args[2]);
        var rng = new Xoshiro128(seed);
        var output = new string[count];
        for (var i = 0; i < count; i++) output[i] = rng.Sample(items);
        return output;
    }

    [Fact]
    public void ElEstadoEnteroEsBitExacto()
    {
        // La secuencia entera no depende de transcendentales: debe coincidir exactamente.
        var document = GoldenDocument.Load("rng");
        var failures = new List<string>();
        foreach (var name in new[] { "nextUint32", "next" })
        {
            var cases = document.Cases(name);
            for (var index = 0; index < cases.Count; index++)
            {
                var goldenCase = cases[index];
                var seed = (uint)GoldenReader.Number(goldenCase.Args[0]);
                var count = GoldenReader.Int(goldenCase.Args[1]);
                var expected = GoldenReader.Numbers(goldenCase.Expected);
                var rng = new Xoshiro128(seed);
                for (var i = 0; i < count; i++)
                {
                    var actual = name == "next" ? rng.Next() : rng.NextUInt32();
                    if (actual != expected[i])
                    {
                        failures.Add($"{name}[seed={seed}][{i}]: esperado {expected[i]:R}, obtenido {actual:R}");
                        break;
                    }
                }
            }
        }
        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Fact]
    public void LasDistribucionesMuestreadasCoincidenConElMotor()
    {
        GoldenSuite.Run("rng", Registry, 1e-12, "nextUint32", "next");
    }
}
