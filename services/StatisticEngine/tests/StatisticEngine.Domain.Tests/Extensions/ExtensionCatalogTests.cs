using StatisticEngine.Domain.Analysis;
using Xunit;

namespace StatisticEngine.Domain.Tests.Extensions;

/// <summary>Integridad del catálogo extendido: baseline intacto, ids únicos y vectores presentes.</summary>
public class ExtensionCatalogTests
{
    [Fact]
    public void ElBaselineSigueTeniendoSesentaAnalisisYQuinceCategorias()
    {
        Assert.Equal(60, AnalysisCatalog.Baseline.Count);
        Assert.Equal(15, AnalysisCatalog.BaselineCategories.Count);
        Assert.All(AnalysisCatalog.Baseline, analysis => Assert.Equal(AnalysisCategories.TinyTabOrigin, analysis.Origin));
    }

    [Fact]
    public void LosIdsSonUnicosEntreBaselineExtensionesYDatos()
    {
        var ids = AnalysisCatalog.Baseline.Select(analysis => analysis.Id)
            .Concat(AnalysisCatalog.Extensions.Select(analysis => analysis.Id))
            .Concat(DataCatalog.All.Select(operation => operation.Id))
            .ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void LasExtensionesDeclaranOrigenSigma()
    {
        Assert.All(AnalysisCatalog.Extensions, analysis => Assert.Equal(AnalysisCategories.SigmaOrigin, analysis.Origin));
        Assert.All(DataCatalog.All, operation => Assert.Equal(AnalysisCategories.SigmaOrigin, operation.Origin));
    }

    [Fact]
    public void CadaAnalisisExtendidoTieneVectorDeEjemploDeSigma()
    {
        var missing = AnalysisCatalog.Extensions
            .Where(analysis => SigmaVectors.Example(analysis.Id) is null)
            .Select(analysis => analysis.Id)
            .ToArray();
        Assert.True(missing.Length == 0, $"Sin vector de Sigma: {string.Join(", ", missing)}");
    }

    [Fact]
    public void LasCategoriasNuevasSoloAparecenSiTienenAnalisis()
    {
        var used = AnalysisCatalog.All.Select(analysis => analysis.Category).ToHashSet(StringComparer.Ordinal);
        Assert.All(AnalysisCatalog.Categories, category => Assert.Contains(category, used));
    }
}
