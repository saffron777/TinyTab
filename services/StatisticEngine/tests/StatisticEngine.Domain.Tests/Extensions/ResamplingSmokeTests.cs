using StatisticEngine.Application.UseCases;
using Xunit;

namespace StatisticEngine.Domain.Tests.Extensions;

public class ResamplingSmokeTests
{
    [Fact]
    public void ElBootstrapSeEjecutaYDevuelveUnIntervaloFinito()
    {
        var item = SigmaVectors.Example("bootstrap")!;
        var result = new AnalysisService().Run(item.Id, SigmaVectors.BuildInput(item));
        if (result.IsError) Assert.Fail(result.Error.Message);
        var low = (double)result.Value.KeyResults!["low"]!;
        var high = (double)result.Value.KeyResults!["high"]!;
        Assert.True(double.IsFinite(low) && double.IsFinite(high) && low <= high);
        Assert.NotEmpty(result.Value.Sections);
    }

    [Fact]
    public void LaPermutacionSeEjecutaYDevuelveUnValorPValido()
    {
        var item = SigmaVectors.Example("permutation")!;
        var result = new AnalysisService().Run(item.Id, SigmaVectors.BuildInput(item));
        if (result.IsError) Assert.Fail(result.Error.Message);
        var p = (double)result.Value.KeyResults!["p"]!;
        Assert.InRange(p, 0, 1);
    }

    [Fact]
    public void ElMonteCarloSeEjecutaYDevuelveCuantilesOrdenados()
    {
        var item = SigmaVectors.Example("monte-carlo")!;
        var result = new AnalysisService().Run(item.Id, SigmaVectors.BuildInput(item));
        if (result.IsError) Assert.Fail(result.Error.Message);
        var quantiles = (double[])result.Value.KeyResults!["quantiles"]!;
        Assert.Equal(5, quantiles.Length);
        Assert.True(quantiles.SequenceEqual(quantiles.OrderBy(value => value)));
    }

    [Fact]
    public void LosDatosAleatoriosRespetanDimensionesYColumnas()
    {
        var item = SigmaVectors.Example("random-data")!;
        var result = new AnalysisService().Run(item.Id, SigmaVectors.BuildInput(item));
        if (result.IsError) Assert.Fail(result.Error.Message);
        var dataset = result.Value.Datasets!["resultado"];
        Assert.Equal(50, dataset.Columns[0].Count);
        Assert.Equal(3, dataset.Columns.Count);
    }
}
