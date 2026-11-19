using StatisticEngine.Domain.Analysis;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Data.Operations;

/// <summary>Operaciones básicas de filas y estructura sobre frames tipados.</summary>
public static class RowOperations
{
    public const string ResultKey = "resultado";

    public static readonly AnalysisDefinition Sort = new(
        "data-sort",
        "Ordenar filas",
        "Datos",
        "Ordena el dataset por una o varias columnas, de forma ascendente o descendente.",
        [],
        [],
        [
            new AnalysisParamDefinition("descending", "Orden descendente", AnalysisParamKinds.Boolean, false, [], false)
        ],
        RunSort,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Filas",
        Roles: [new ColumnRole("cols", "Columnas de orden", ColumnKinds.Any, Multiple: true)], 
        Outputs: [new AnalysisOutputSpec(ResultKey, "Datos ordenados", Required: true)],
        NeedsDataFrame: true);

    public static readonly AnalysisDefinition SelectColumns = new(
        "data-select-columns",
        "Seleccionar columnas",
        "Datos",
        "Conserva sólo las columnas elegidas, en el orden indicado, y opcionalmente les cambia el nombre.",
        [],
        [],
        [],
        RunSelect,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Estructura",
        Roles: [new ColumnRole("cols", "Columnas a conservar", ColumnKinds.Any, Multiple: true)],
        Outputs: [new AnalysisOutputSpec(ResultKey, "Datos seleccionados", Required: true)],
        NeedsDataFrame: true);

    public static readonly AnalysisDefinition Duplicates = new(
        "data-duplicates",
        "Detectar y quitar duplicados",
        "Datos",
        "Marca las filas duplicadas según las columnas clave y conserva la primera aparición.",
        [],
        [],
        [],
        RunDuplicates,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Filas",
        Roles: [new ColumnRole("cols", "Columnas clave (vacío = fila completa)", ColumnKinds.Any, Multiple: true, Optional: true)],
        Outputs: [new AnalysisOutputSpec(ResultKey, "Datos sin duplicados", Required: true)],
        NeedsDataFrame: true);

    private static Result<AnalysisResult> RunSort(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var names = input.RoleNames("cols", true);
        if (names.Count == 0) return Result.Err<AnalysisResult>(DomainErrorCode.InsufficientData, "Elija al menos una columna de orden.");
        var descending = input.Pbool("descending");
        var indices = Enumerable.Range(0, frame.RowCount).ToArray();
        var columns = names.Select(frame.Column).ToArray();
        Array.Sort(indices, (left, right) =>
        {
            foreach (var column in columns)
            {
                var comparison = Compare(column, left, right);
                if (comparison != 0) return descending ? -comparison : comparison;
            }
            return left.CompareTo(right);
        });
        return Build("data-sort", "Datos ordenados", frame, frame.SubsetRows(indices), "Ordenado por " + string.Join(", ", names));
    }

    private static Result<AnalysisResult> RunSelect(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var names = input.RoleNames("cols", true);
        if (names.Count == 0) return Result.Err<AnalysisResult>(DomainErrorCode.InsufficientData, "Elija al menos una columna.");
        var selected = frame.Subset(names);
        return Build("data-select-columns", "Datos seleccionados", frame, selected, $"Se conservan {names.Count} de {frame.ColumnCount} columnas.");
    }

    private static Result<AnalysisResult> RunDuplicates(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var names = input.RoleNames("cols");
        var keys = names.Count > 0 ? names : frame.Columns.Select(column => column.Name).ToArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var keep = new List<int>();
        var removed = 0;
        for (var row = 0; row < frame.RowCount; row++)
        {
            var key = string.Join("\u001f", keys.Select(name => frame.Find(name)?.IsMissing(row) == true ? "<NA>" : frame.Texts(name)[row] ?? string.Empty));
            if (seen.Add(key)) keep.Add(row);
            else removed++;
        }
        return Build("data-duplicates", "Datos sin duplicados", frame, frame.SubsetRows(keep), $"{removed} fila(s) duplicada(s) eliminada(s).");
    }

    private static int Compare(DataColumn column, int left, int right)
    {
        if (column.IsMissing(left) || column.IsMissing(right)) return column.IsMissing(left) == column.IsMissing(right) ? 0 : column.IsMissing(left) ? 1 : -1;
        return column.Kind switch
        {
            DataColumnKind.Numeric => column.NumberAt(left).CompareTo(column.NumberAt(right)),
            DataColumnKind.DateTime => column.DateAt(left).GetValueOrDefault().CompareTo(column.DateAt(right).GetValueOrDefault()),
            _ => string.CompareOrdinal(column.TextAt(left), column.TextAt(right))
        };
    }

    private static Result<AnalysisResult> Build(string id, string label, Frame before, Frame after, string message)
    {
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Filas antes", JsNumber.ToString(before.RowCount)),
                new TileItem("Filas después", JsNumber.ToString(after.RowCount)),
                new TileItem("Columnas", JsNumber.ToString(after.ColumnCount))
            ]),
            new AlertSection("Resultado", "info", message)
        };
        return Result.Ok(new AnalysisResult(
            id,
            label,
            sections,
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
            {
                [ResultKey] = new DatasetResult(label, after.Columns)
            },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n_rows"] = after.RowCount,
                ["n_cols"] = after.ColumnCount,
                ["n_rows_before"] = before.RowCount
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
