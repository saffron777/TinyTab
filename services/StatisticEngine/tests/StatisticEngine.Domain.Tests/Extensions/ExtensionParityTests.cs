using StatisticEngine.Application.UseCases;
using StatisticEngine.Domain;
using StatisticEngine.Domain.Analysis;
using StatisticEngine.Domain.Tests.Parity;
using Xunit;

namespace StatisticEngine.Domain.Tests.Extensions;

/// <summary>
/// Paridad de los análisis extendidos contra los vectores generados desde Sigma. Sólo se ejecutan
/// los ids implementados; el resto de vectores queda en el repositorio para las fases siguientes.
/// </summary>
public class ExtensionParityTests
{
    private static readonly HashSet<string> Implemented =
        ExtensionCatalog.All.Select(analysis => analysis.Id).ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void LosEjemplosDeSigmaCoinciden()
    {
        var failures = new List<string>();
        var checkedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in SigmaVectors.Examples.Where(item => Implemented.Contains(item.Id) && item.Deterministic))
        {
            checkedIds.Add(item.Id);
            var runner = new AnalysisService();
            Result<AnalysisResult> result;
            try
            {
                result = runner.Run(item.Id, SigmaVectors.BuildInput(item));
            }
            catch (Exception exception)
            {
                failures.Add($"{item.Module}/{item.Id}: excepción {exception.GetType().Name}: {exception.Message}");
                continue;
            }

            try
            {
                SigmaCompare.Example(item, result);
            }
            catch (Exception exception)
            {
                failures.Add(exception.Message);
            }
        }

        foreach (var id in Implemented)
        {
            if (!checkedIds.Contains(id) && !SigmaVectors.Examples.Any(item => item.Id == id && !item.Deterministic))
            {
                failures.Add($"{id}: no tiene vector de ejemplo generado desde Sigma.");
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail($"{failures.Count} discrepancia(s):\n{string.Join('\n', failures.Take(40))}");
        }
    }

    [Fact]
    public void LosErroresDeValidacionCoinciden()
    {
        var failures = new List<string>();
        foreach (var item in SigmaVectors.Errors.Where(item => Implemented.Contains(item.Id)))
        {
            var runner = new AnalysisService();
            Result<AnalysisResult> result;
            try
            {
                result = runner.Run(item.Id, SigmaVectors.BuildInput(item));
            }
            catch (Exception exception)
            {
                failures.Add($"{item.Module}/{item.Id}: excepción {exception.GetType().Name}: {exception.Message}");
                continue;
            }

            try
            {
                SigmaCompare.Error(item, result);
            }
            catch (Exception exception)
            {
                failures.Add(exception.Message);
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail($"{failures.Count} discrepancia(s):\n{string.Join('\n', failures.Take(40))}");
        }
    }

    [Fact]
    public void ElCatalogoExtendidoCoincideConSigma()
    {
        var failures = new List<string>();
        foreach (var entry in SigmaVectors.Catalog.GetProperty("analyses").EnumerateArray())
        {
            var sigmaId = entry.GetProperty("id").GetString() ?? string.Empty;
            var csharpId = entry.TryGetProperty("csharp_id", out var idProperty) ? idProperty.GetString() : null;
            if (csharpId is null || !Implemented.Contains(csharpId)) continue;

            var definition = AnalysisCatalog.Find(csharpId);
            if (definition is null)
            {
                failures.Add($"{csharpId}: el catálogo C# no contiene el análisis.");
                continue;
            }

            try
            {
                SigmaCatalogCompare.Compare(entry, definition);
            }
            catch (Exception exception)
            {
                failures.Add($"{csharpId} (sigma/{sigmaId}): {exception.Message}");
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail($"{failures.Count} discrepancia(s):\n{string.Join('\n', failures.Take(40))}");
        }
    }
}
