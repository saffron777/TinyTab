using Microsoft.Extensions.Diagnostics.HealthChecks;
using StatisticEngine.Application.UseCases;
using StatisticEngine.Domain.Analysis;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Api.Infrastructure;

/// <summary>
/// Autotest determinista del motor numérico. Se ejecuta en el probe de readiness para detectar
/// una imagen o plataforma donde las funciones especiales no se comportan como se verificó.
/// También ejecuta un análisis baseline, uno extendido y una operación de datos.
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

        var frame = new Frame([DataColumn.Numeric("x", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10])]);
        var analysis = new AnalysisService();
        var baseline = analysis.Run("describe", new AnalysisInput(
            numeric: new Dictionary<string, double[]>(StringComparer.Ordinal) { ["x"] = [1, 2, 3, 4, 5] }));
        var extension = analysis.Run("jarque-bera", new AnalysisInput(
            frame: frame,
            roles: new Dictionary<string, string[]>(StringComparer.Ordinal) { ["var"] = ["x"] }));
        var dataOperation = DataService.Run("data-standardize", new AnalysisInput(
            frame: frame,
            roles: new Dictionary<string, string[]>(StringComparer.Ordinal) { ["cols"] = ["x"] }));

        var healthy = cdf == 0.5
                      && Math.Abs(quantile - ExpectedQuantile) < 1e-9
                      && student == 0.5
                      && baseline.IsOk
                      && extension.IsOk
                      && dataOperation.IsOk;

        return Task.FromResult(
            healthy
                ? HealthCheckResult.Healthy("Motor numérico y catálogo verificados.")
                : HealthCheckResult.Unhealthy(
                    $"Autotest falló: normalCdf(0)={cdf}, normalInv(0.975)={quantile}, tCdf(0,10)={student}, "
                    + $"baseline={baseline}, extendido={extension}, datos={dataOperation}."));
    }
}
