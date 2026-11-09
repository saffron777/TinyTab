using System.Text.Json;
using StatisticEngine.Domain.Distributions;
using StatisticEngine.Domain.Random;
using StatisticEngine.Domain.Tests.Parity;
using Xunit;

namespace StatisticEngine.Domain.Tests.Distributions;

/// <summary>
/// Paridad de las 21 distribuciones: metadatos, validación, pdf/cdf/invCdf, momentos, soporte
/// y muestreo con la misma semilla y la misma secuencia de llamadas que el motor TypeScript.
/// </summary>
public class DistributionParityTests
{
    private static readonly JsonSerializerOptions MetadataOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Las21DistribucionesCoincidenConElMotor()
    {
        var registry = new Dictionary<string, Func<GoldenCase, object?>>(StringComparer.Ordinal);

        foreach (var distribution in DistributionCatalog.All)
        {
            var target = distribution;
            var prefix = distribution.Id;

            registry[$"{prefix}.metadata"] = _ => Metadata(target);
            registry[$"{prefix}.validate"] = goldenCase =>
                ResultDto.FromVoid(target.Validate(Params(goldenCase.Args[0])));

            registry[$"{prefix}.pdf"] = goldenCase =>
                target.Pdf(GoldenReader.Number(goldenCase.Args[0]), Params(goldenCase.Args[1]));

            registry[$"{prefix}.cdf"] = goldenCase =>
                target.Cdf(GoldenReader.Number(goldenCase.Args[0]), Params(goldenCase.Args[1]));

            registry[$"{prefix}.invCdf"] = goldenCase =>
                target.InvCdf(GoldenReader.Number(goldenCase.Args[0]), Params(goldenCase.Args[1]));

            registry[$"{prefix}.mean"] = goldenCase => target.Mean(Params(goldenCase.Args[0]));

            registry[$"{prefix}.sd"] = goldenCase => target.Sd(Params(goldenCase.Args[0]));

            registry[$"{prefix}.support"] = goldenCase => target.Support(Params(goldenCase.Args[0]));

            registry[$"{prefix}.random"] = goldenCase =>
            {
                var seed = GoldenReader.Number(goldenCase.Args[0]);
                var parameters = Params(goldenCase.Args[1]);
                var rng = new Xoshiro128((uint)seed);
                // paridad: el exportador TS crea un Rng(seed) y extrae 5 muestras seguidas.
                var samples = new double[5];
                for (var i = 0; i < samples.Length; i++) samples[i] = target.Random(rng, parameters);
                return samples;
            };
        }

        GoldenSuite.Run("distributions", registry, 1e-12);
    }

    private static DistParams Params(JsonElement element)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject()) values[property.Name] = GoldenReader.Number(property.Value);
        return new DistParams(values);
    }

    /// <summary>
    /// Metadatos con la forma exacta del motor: límites nulos explícitos, <c>integer</c>
    /// siempre presente y <c>defaults</c> en el orden de los parámetros. Se serializa con las
    /// convenciones Web pero sin omitir nulos (a diferencia del comparador de paridad).
    /// </summary>
    private static JsonElement Metadata(IDistribution distribution) => JsonSerializer.SerializeToElement(
        new
        {
            id = distribution.Id,
            label = distribution.Label,
            kind = DistributionKinds.ToWire(distribution.Kind),
            @params = distribution.Parameters.Select(parameter => new
            {
                key = parameter.Key,
                label = parameter.Label,
                defaultValue = parameter.DefaultValue,
                integer = parameter.Integer,
                minExclusive = parameter.MinExclusive,
                minInclusive = parameter.MinInclusive,
                maxInclusive = parameter.MaxInclusive
            }).ToArray(),
            defaults = Defaults(distribution)
        },
        MetadataOptions);

    private static Dictionary<string, double> Defaults(IDistribution distribution)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var parameter in distribution.Parameters) values[parameter.Key] = parameter.DefaultValue;
        return values;
    }
}
