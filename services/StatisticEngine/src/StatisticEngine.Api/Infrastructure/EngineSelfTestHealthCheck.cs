using Microsoft.Extensions.Diagnostics.HealthChecks;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Api.Infrastructure;

/// <summary>
/// Autotest determinista del motor numérico. Se ejecuta en el probe de readiness para detectar
/// una imagen o plataforma donde las funciones especiales no se comportan como se verificó.
/// </summary>
public sealed class EngineSelfTestHealthCheck : IHealthCheck
{
    private const double ExpectedQuantile = 1.959963984540054;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var cdf = SpecialFunctions.NormalCdf(0);
        var quantile = SpecialFunctions.NormalInv(0.975);
        var student = InferenceDistributions.TCdf(0, 10);

        var healthy = cdf == 0.5
                      && Math.Abs(quantile - ExpectedQuantile) < 1e-9
                      && student == 0.5;

        return Task.FromResult(
            healthy
                ? HealthCheckResult.Healthy("Motor numérico verificado.")
                : HealthCheckResult.Unhealthy(
                    $"Autotest numérico falló: normalCdf(0)={cdf}, normalInv(0.975)={quantile}, tCdf(0,10)={student}."));
    }
}
