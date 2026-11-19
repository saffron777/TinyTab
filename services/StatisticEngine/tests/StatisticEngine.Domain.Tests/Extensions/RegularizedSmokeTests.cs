using StatisticEngine.Application.UseCases;
using Xunit;

namespace StatisticEngine.Domain.Tests.Extensions;

public class RegularizedSmokeTests
{
    [Fact]
    public void ElEjemploRegularizadoSeEjecutaYDevuelveResultadosFinitos()
    {
        var item = SigmaVectors.Example("regression-regularized")!;
        var result = new AnalysisService().Run(item.Id, SigmaVectors.BuildInput(item));
        if (result.IsError) Assert.Fail(result.Error.Message);
        Assert.NotEmpty(result.Value.Sections);
        Assert.Equal(2, result.Value.Charts!.Count);
        Assert.Contains("ajustado", result.Value.Datasets!["predicciones"].Columns.Select(column => column.Name));
        var lambda = (double)result.Value.KeyResults!["lambda_"]!;
        Assert.True(double.IsFinite(lambda) && lambda > 0);
    }
}
