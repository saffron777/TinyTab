using System.Text.Json;
using StatisticEngine.Domain.Analysis;
using StatisticEngine.Domain.Tests.Parity;
using Xunit;

namespace StatisticEngine.Domain.Tests.Analysis;

/// <summary>
/// Paridad del catálogo y de los 60 análisis contra <c>catalog.json</c>, <c>analyses.json</c> y
/// <c>text-format.json</c>.
/// </summary>
public class AnalysisParityTests
{
    /// <summary>
    /// Tolerancia numérica de los análisis. Las tablas de sesión son texto y se comparan exactas;
    /// la tolerancia sólo afecta a los números crudos de los gráficos, donde los ajustes
    /// iterativos (Weibull MLE, AR) pueden diferir algunas unidades en el último lugar.
    /// </summary>
    private const double Tolerance = 1e-9;

    [Fact]
    public void ElCatalogoCoincideConElMotor()
    {
        var expected = GoldenDocument.Load("catalog");
        var actual = AnalysisCatalog.All.Select(Project).ToArray();
        ParityAssert.Matches(expected.Cases("ALL_ANALYSES")[0].Expected, actual, 0, "ALL_ANALYSES");
        ParityAssert.Matches(expected.Cases("ANALYSIS_CATEGORIES")[0].Expected, AnalysisCatalog.Categories, 0, "ANALYSIS_CATEGORIES");

        var groups = AnalysisCatalog.ByCategory()
            .Select(group => new { category = group.Category, ids = group.Analyses.Select(analysis => analysis.Id).ToArray() })
            .ToArray();
        ParityAssert.Matches(expected.Cases("analysesByCategory")[0].Expected, groups, 0, "analysesByCategory");
    }

    [Fact]
    public void LosSesentaAnalisisProducenElMismoResultado()
    {
        var document = GoldenDocument.Load("analyses");
        var failures = new List<string>();

        foreach (var id in document.FunctionNames.OrderBy(name => name, StringComparer.Ordinal))
        {
            var goldenCase = document.Cases(id)[0];
            var definition = AnalysisCatalog.Find(id);
            if (definition is null)
            {
                failures.Add($"{id}: el análisis no existe en el catálogo C#");
                continue;
            }

            var runResult = definition.Run(ToInput(goldenCase.Args[0]));

            if (id == "multivariate-factor")
            {
                try
                {
                    CompareFactorAnalysis(goldenCase.Expected, runResult);
                }
                catch (Exception exception)
                {
                    failures.Add(exception.Message);
                }
                continue;
            }

            object? actual;
            try
            {
                actual = ResultDto.From(runResult);
            }
            catch (Exception exception)
            {
                failures.Add($"{id}: excepción {exception.GetType().Name}: {exception.Message}");
                continue;
            }

            try
            {
                ParityAssert.Matches(goldenCase.Expected, actual, Tolerance, id);
            }
            catch (Exception exception)
            {
                failures.Add(exception.Message);
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail($"analyses: {failures.Count} discrepancia(s):\n{string.Join('\n', failures.Take(40))}");
        }
    }

    [Fact]
    public void ElTextoDeLaSesionCoincideConElMotor()
    {
        var textFormat = GoldenDocument.Load("text-format");
        var analyses = GoldenDocument.Load("analyses");
        var failures = new List<string>();

        foreach (var goldenCase in textFormat.Cases("formatAnalysisResult"))
        {
            var id = goldenCase.Args[0].GetString() ?? string.Empty;
            var definition = AnalysisCatalog.Find(id);
            if (definition is null)
            {
                failures.Add($"{id}: el análisis no existe en el catálogo C#");
                continue;
            }

            var input = ToInput(analyses.Cases(id)[0].Args[0]);
            var result = definition.Run(input);

            if (goldenCase.Expected.ValueKind == JsonValueKind.Object)
            {
                // El motor devolvió un error de dominio para esta entrada de humo: se compara el
                // resultado tal cual, sin formatear.
                try
                {
                    ParityAssert.Matches(goldenCase.Expected, ResultDto.From(result), Tolerance, $"formatAnalysisResult[{id}]");
                }
                catch (Exception exception)
                {
                    failures.Add(exception.Message);
                }
                continue;
            }

            if (result.IsError)
            {
                failures.Add($"{id}: el análisis devolvió {result.Error.WireCode}");
                continue;
            }

            var text = AnalysisTextFormatter.Format(result.Value);
            try
            {
                ParityAssert.Matches(goldenCase.Expected, text, 0, $"formatAnalysisResult[{id}]");
            }
            catch (Exception exception)
            {
                failures.Add(exception.Message);
            }
        }

        foreach (var goldenCase in textFormat.Cases("sectionToPlainText"))
        {
            var section = ToSection(goldenCase.Args[0]);
            var text = AnalysisTextFormatter.SectionToPlainText(section);
            try
            {
                ParityAssert.Matches(goldenCase.Expected, text, 0, "sectionToPlainText");
            }
            catch (Exception exception)
            {
                failures.Add(exception.Message);
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail($"text-format: {failures.Count} discrepancia(s):\n{string.Join('\n', failures.Take(20))}");
        }
    }

    /// <summary>
    /// <c>multivariate-factor</c> depende de la rotación varimax, que en el motor TypeScript no
    /// converge (iteración caótica; ver la prueba de multivariante). Se comparan etiquetas,
    /// cabeceras y comunalidades exactas, y las cargas con tolerancia absoluta de 0,01.
    /// </summary>
    private static void CompareFactorAnalysis(JsonElement expected, Result<AnalysisResult> result)
    {
        if (expected.TryGetProperty("ok", out var okElement) && !okElement.GetBoolean())
        {
            ParityAssert.Matches(expected, ResultDto.From(result), 0, "multivariate-factor");
            return;
        }

        if (result.IsError)
        {
            throw new ParityMismatchException($"multivariate-factor: se esperaba un resultado correcto y se obtuvo {result.Error.WireCode}.");
        }

        var actual = result.Value;
        var expectedSections = expected.GetProperty("value").GetProperty("sections");
        if (expectedSections.GetArrayLength() != actual.Sections.Count)
        {
            throw new ParityMismatchException(
                $"multivariate-factor: se esperaban {expectedSections.GetArrayLength()} secciones y hay {actual.Sections.Count}.");
        }

        var expectedLoadings = expectedSections[0];
        if (actual.Sections[0] is not TableSection loadings)
        {
            throw new ParityMismatchException("multivariate-factor: la primera sección no es una tabla.");
        }

        ParityAssert.Matches(expectedLoadings.GetProperty("title"), loadings.Title, 0, "multivariate-factor.cargas.title");
        ParityAssert.Matches(expectedLoadings.GetProperty("headers"), loadings.Headers, 0, "multivariate-factor.cargas.headers");

        var expectedRows = expectedLoadings.GetProperty("rows").EnumerateArray().ToArray();
        if (expectedRows.Length != loadings.Rows.Count)
        {
            throw new ParityMismatchException(
                $"multivariate-factor: se esperaban {expectedRows.Length} filas y hay {loadings.Rows.Count}.");
        }

        for (var rowIndex = 0; rowIndex < expectedRows.Length; rowIndex++)
        {
            var expectedCells = expectedRows[rowIndex].EnumerateArray().Select(cell => cell.GetString() ?? string.Empty).ToArray();
            var actualCells = loadings.Rows[rowIndex];
            if (expectedCells.Length != actualCells.Count)
            {
                throw new ParityMismatchException(
                    $"multivariate-factor.cargas[{rowIndex}]: se esperaban {expectedCells.Length} celdas y hay {actualCells.Count}.");
            }

            for (var cellIndex = 0; cellIndex < expectedCells.Length; cellIndex++)
            {
                if (cellIndex == 0)
                {
                    if (!string.Equals(expectedCells[cellIndex], actualCells[cellIndex], StringComparison.Ordinal))
                    {
                        throw new ParityMismatchException(
                            $"multivariate-factor.cargas[{rowIndex}][0]: se esperaba \"{expectedCells[cellIndex]}\" y se obtuvo \"{actualCells[cellIndex]}\".");
                    }
                    continue;
                }

                var expectedNumber = double.Parse(expectedCells[cellIndex], System.Globalization.CultureInfo.InvariantCulture);
                var actualNumber = double.Parse(actualCells[cellIndex], System.Globalization.CultureInfo.InvariantCulture);
                if (Math.Abs(expectedNumber - actualNumber) > 0.01)
                {
                    throw new ParityMismatchException(
                        $"multivariate-factor.cargas[{rowIndex}][{cellIndex}]: se esperaba {expectedCells[cellIndex]} y se obtuvo {actualCells[cellIndex]}.");
                }
            }
        }

        if (actual.Sections[1] is not TableSection communalities)
        {
            throw new ParityMismatchException("multivariate-factor: la segunda sección no es una tabla.");
        }

        var expectedCommunalities = expectedSections[1];
        ParityAssert.Matches(expectedCommunalities.GetProperty("title"), communalities.Title, 0, "multivariate-factor.comunalidades.title");
        ParityAssert.Matches(expectedCommunalities.GetProperty("headers"), communalities.Headers, 0, "multivariate-factor.comunalidades.headers");
        ParityAssert.Matches(expectedCommunalities.GetProperty("rows"), communalities.Rows, 0, "multivariate-factor.comunalidades.rows");
    }

    private static object Project(AnalysisDefinition analysis) =>
        new
        {
            id = analysis.Id,
            label = analysis.Label,
            category = analysis.Category,
            description = analysis.Description,
            numericColumns = analysis.NumericColumns.Select(column => new
            {
                key = column.Key,
                label = column.Label,
                required = column.Required,
                multiple = column.Multiple
            }).ToArray(),
            textColumns = analysis.TextColumns.Select(column => new
            {
                key = column.Key,
                label = column.Label,
                required = column.Required,
                multiple = column.Multiple
            }).ToArray(),
            @params = analysis.Params.Select(parameter => new
            {
                key = parameter.Key,
                label = parameter.Label,
                kind = parameter.Kind,
                defaultValue = parameter.DefaultValue,
                options = parameter.Options.Select(option => new { value = option.Value, label = option.Label }).ToArray(),
                optional = parameter.Optional
            }).ToArray()
        };

    private static AnalysisInput ToInput(JsonElement element)
    {
        var numeric = new Dictionary<string, double[]>(StringComparer.Ordinal);
        if (element.TryGetProperty("numeric", out var numericElement))
        {
            foreach (var property in numericElement.EnumerateObject())
            {
                numeric[property.Name] = GoldenReader.Numbers(property.Value);
            }
        }

        var text = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (element.TryGetProperty("text", out var textElement))
        {
            foreach (var property in textElement.EnumerateObject())
            {
                text[property.Name] = GoldenReader.Texts(property.Value);
            }
        }

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (element.TryGetProperty("params", out var paramsElement))
        {
            foreach (var property in paramsElement.EnumerateObject())
            {
                parameters[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.Number => property.Value.GetDouble(),
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null
                };
            }
        }

        return new AnalysisInput(numeric, text, parameters);
    }

    private static AnalysisSection ToSection(JsonElement element)
    {
        var kind = element.GetProperty("kind").GetString();
        var title = element.GetProperty("title").GetString() ?? string.Empty;
        if (kind == "text")
        {
            return new TextSection(title, element.GetProperty("text").GetString() ?? string.Empty);
        }

        var headers = element.GetProperty("headers").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
        var rows = element.GetProperty("rows")
            .EnumerateArray()
            .Select(row => (IReadOnlyList<string>)row.EnumerateArray().Select(cell => cell.GetString() ?? string.Empty).ToArray())
            .ToArray();
        return new TableSection(title, headers, rows);
    }
}
