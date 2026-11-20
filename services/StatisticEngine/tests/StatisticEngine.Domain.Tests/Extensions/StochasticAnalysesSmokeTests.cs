using StatisticEngine.Application.UseCases;
using StatisticEngine.Domain;
using StatisticEngine.Domain.Tests.Parity;
using Xunit;

namespace StatisticEngine.Domain.Tests.Extensions;

/// <summary>Pruebas de propiedades para análisis cuya salida de Sigma no es determinista.</summary>
public class StochasticAnalysesSmokeTests
{
    [Fact]
    public void MarkovDevuelveProbabilidadesYTransicionValidas()
    {
        var item = SigmaVectors.Example("markov")!;
        var result = new AnalysisService().Run(item.Id, SigmaVectors.BuildInput(item));
        if (result.IsError) Assert.Fail(result.Error.Message);
        var dataset = result.Value.Datasets!["regimenes"];
        var regimen = dataset.Columns.First(column => column.Name == "regimen");
        Assert.All(regimen.Numbers, value => Assert.InRange(value, 1, 4));
        var probabilities = dataset.Columns.Where(column => column.Name.StartsWith("prob_regimen_", StringComparison.Ordinal)).ToArray();
        Assert.Equal(dataset.Columns[0].Count, probabilities[0].Count);
        for (var row = 0; row < probabilities[0].Count; row++)
        {
            Assert.InRange(probabilities.Sum(column => column.NumberAt(row)), 0.999999, 1.000001);
        }
        var transition = (double[][])result.Value.KeyResults!["transition"]!;
        Assert.All(transition, row => Assert.InRange(row.Sum(), 0.999999, 1.000001));
    }

    [Fact]
    public void UcmDevuelveComponentesYPronosticosFinitos()
    {
        var item = SigmaVectors.Example("ucm")!;
        var result = new AnalysisService().Run(item.Id, SigmaVectors.BuildInput(item));
        if (result.IsError) Assert.Fail(result.Error.Message);
        var variances = (Dictionary<string, double>)result.Value.KeyResults!["variances"]!;
        Assert.All(variances.Values, value => Assert.True(double.IsFinite(value) && value >= 0));
        var components = result.Value.Datasets!["componentes"];
        Assert.Contains(components.Columns, column => column.Name == "nivel");
        Assert.Contains(components.Columns, column => column.Name == "pendiente");
    }

    [Fact]
    public void GarchDevuelveUnaVolatilidadPositivaYPersistenciaValida()
    {
        var item = SigmaVectors.Example("garch")!;
        var result = new AnalysisService().Run(item.Id, SigmaVectors.BuildInput(item));
        if (result.IsError) Assert.Fail(result.Error.Message);
        var persistence = (double)result.Value.KeyResults!["persistence"]!;
        Assert.InRange(persistence, 0, 1);
        Assert.True(double.IsFinite((double)result.Value.KeyResults!["aic"]!));
        var parameters = (Dictionary<string, double>)result.Value.KeyResults!["parameters"]!;
        Assert.InRange(parameters["alpha[1]"] + parameters["beta[1]"], 0, 1);
        var volatility = result.Value.Datasets!["volatilidad"];
        var column = volatility.Columns.First(value => value.Name is "volatilidad" or "sigma");
        Assert.All(column.Numbers, value => Assert.True(double.IsFinite(value) && value >= 0));
    }

    [Fact]
    public void ModeloMixtoDevuelveComponentesDeVarianzaValidos()
    {
        var item = SigmaVectors.Example("regression-mixed")!;
        var result = new AnalysisService().Run(item.Id, SigmaVectors.BuildInput(item));
        if (result.IsError) Assert.Fail(result.Error.Message);
        var group = (double)result.Value.KeyResults!["var_group"]!;
        var residual = (double)result.Value.KeyResults!["resid_var"]!;
        Assert.True(group >= 0 && residual > 0);
        Assert.InRange((double)result.Value.KeyResults!["icc"]!, 0, 1);
        Assert.Equal(240, result.Value.Datasets!["predicciones"].Columns[0].Count);
    }

    [Fact]
    public void SuavizamientoDevuelveAjusteYPronosticosFinitos()
    {
        var item = SigmaVectors.Example("smoothing")!;
        var result = new AnalysisService().Run(item.Id, SigmaVectors.BuildInput(item));
        if (result.IsError) Assert.Fail(result.Error.Message);
        Assert.True(double.IsFinite((double)result.Value.KeyResults!["rmse"]!));
        Assert.True((double)result.Value.KeyResults!["rmse"]! >= 0);
        var fit = result.Value.Datasets!["ajuste"];
        Assert.Contains(fit.Columns, column => column.Name == "ajustado");
        Assert.All(fit.Columns.First(column => column.Name == "ajustado").Numbers, value => Assert.True(double.IsFinite(value)));
    }

    [Fact]
    public void ArimaDevuelveAjusteYResiduosFinitos()
    {
        var item = SigmaVectors.Example("arima")!;
        var result = new AnalysisService().Run(item.Id, SigmaVectors.BuildInput(item));
        if (result.IsError) Assert.Fail(result.Error.Message);
        Assert.True(double.IsFinite((double)result.Value.KeyResults!["aic"]!));
        Assert.True((double)result.Value.KeyResults!["rmse"]! >= 0);
        var fit = result.Value.Datasets!["ajuste"];
        Assert.All(fit.Columns.First(column => column.Name == "ajustado").Numbers, value => Assert.True(double.IsFinite(value)));
        Assert.All(fit.Columns.First(column => column.Name == "residuo").Numbers, value => Assert.True(double.IsFinite(value)));
    }
}
