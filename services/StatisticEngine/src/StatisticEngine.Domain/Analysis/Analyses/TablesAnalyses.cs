using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Tables;
using static StatisticEngine.Domain.Analysis.FormatHelpers;

namespace StatisticEngine.Domain.Analysis.Analyses;

/// <summary>Tablas de conteo, contingencia y bondad de ajuste (paridad: <c>registry-basic.ts</c>).</summary>
public static class TablesAnalyses
{
    private static readonly AnalysisDefinition Tally = DefinitionFactory.Define(
        "tally",
        "Tally individual variables",
        "Tablas",
        "Conteo, porcentaje y porcentaje acumulado por categoría.",
        [DefinitionFactory.Numeric("x", "Variable numérica (opcional)", false)],
        [DefinitionFactory.Text("t", "Variable de texto (opcional)", false)],
        [],
        input =>
        {
            var textValues = input.ColText("t");
            var values = textValues.Length > 0
                ? textValues
                : input.Num("x").Select(JsNumber.ToString).ToArray();
            if (values.Length == 0)
            {
                return Result<AnalysisResult>.Err(DomainErrorCode.InsufficientData, "Seleccione una columna.");
            }

            var rows = CrossTabs.Tally(values.Select(CellValue.FromText).ToArray())
                .Select(row => (IReadOnlyList<object?>)
                [
                    row.Value,
                    JsNumber.ToString(row.Count),
                    $"{JsNumber.ToFixed(row.Percent, 2)} %",
                    $"{JsNumber.ToFixed(row.CumulativePercent, 2)} %"
                ])
                .ToArray();

            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "tally",
                "Tally individual variables",
                Sections(Table("Conteo", ["Valor", "Conteo", "Porcentaje", "Acumulado"], rows))));
        });

    private static readonly AnalysisDefinition CrossTab = DefinitionFactory.Define(
        "cross-tab",
        "Tabla cruzada y chi-cuadrado",
        "Tablas",
        "Tabla de contingencia con prueba de independencia chi-cuadrado.",
        [],
        [
            DefinitionFactory.Text("row", "Variable de fila"),
            DefinitionFactory.Text("column", "Variable de columna")
        ],
        [],
        input =>
        {
            var rows = input.ColText("row");
            var columns = input.ColText("column");
            var result = CrossTabs.CrossTabulation(
                rows.Select(CellValue.FromText).ToArray(),
                columns.Select(CellValue.FromText).ToArray());
            if (result.IsError)
            {
                return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            }

            var value = result.Value;
            var tableRows = new List<IReadOnlyList<object?>>();
            for (var index = 0; index < value.Counts.Count; index++)
            {
                var row = new List<object?>
                {
                    $"{value.RowLabels[index]} ({JsNumber.ToString(value.RowTotals[index])})"
                };
                row.AddRange(value.Counts[index].Select(JsNumber.ToString));
                row.Add(JsNumber.ToString(value.RowTotals[index]));
                tableRows.Add(row);
            }

            var totalRow = new List<object?> { "Total" };
            totalRow.AddRange(value.ColumnTotals.Select(JsNumber.ToString));
            totalRow.Add(JsNumber.ToString(value.Total));

            var headers = new List<string> { "Fila \\ Columna" };
            headers.AddRange(value.ColumnLabels);
            headers.Add("Total");

            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "cross-tab",
                "Tabla cruzada",
                [
                    Table("Conteos", headers, tableRows.Append(totalRow)),
                    Table(
                        "Prueba chi-cuadrado",
                        ["Estadístico", "GL", "Valor p"],
                        [[N(value.Statistic, 3), JsNumber.ToString(value.Df), P(value.PValue)]])
                ]));
        });

    private static readonly AnalysisDefinition ChiSquareGof = DefinitionFactory.Define(
        "chi-square-gof",
        "Chi-cuadrado de bondad de ajuste",
        "Tablas",
        "Compara frecuencias observadas con proporciones esperadas iguales.",
        [DefinitionFactory.Numeric("counts", "Frecuencias observadas")],
        [DefinitionFactory.Text("labels", "Etiquetas (opcional)", false)],
        [],
        input =>
        {
            var observed = input.Num("counts");
            if (observed.Length < 2)
            {
                return Result<AnalysisResult>.Err(DomainErrorCode.InsufficientData, "Seleccione la columna de frecuencias.");
            }

            var labels = observed.Select((_, index) => JsNumber.ToString(index + 1)).ToArray();
            var result = CrossTabs.GoodnessOfFit(labels, observed);
            if (result.IsError)
            {
                return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            }

            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "chi-square-gof",
                "Chi-cuadrado de bondad de ajuste",
                [
                    Table(
                        "Contribuciones",
                        ["Categoría", "Observado", "Esperado", "Contribución"],
                        value.Rows.Select(row => (IReadOnlyList<object?>)
                        [
                            row.Label,
                            N(row.Observed, 1),
                            N(row.Expected, 2),
                            N(row.Contribution, 3)
                        ])),
                    Table(
                        "Prueba",
                        ["χ²", "GL", "Valor p"],
                        [[N(value.Statistic, 3), JsNumber.ToString(value.Df), P(value.PValue)]])
                ]));
        });

    public static IReadOnlyList<AnalysisDefinition> All { get; } = [Tally, CrossTab, ChiSquareGof];
}
