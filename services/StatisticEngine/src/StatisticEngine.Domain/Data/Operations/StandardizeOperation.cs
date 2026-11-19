using StatisticEngine.Domain.Analysis;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Data.Operations;

/// <summary>
/// Tipificar y normalizar columnas numéricas (port de <c>sigma.domain.analysis.transform._standardize</c>).
/// Calcula (x − shift) / divisor con cinco métodos y devuelve el frame transformado como dataset.
/// </summary>
public static class StandardizeOperation
{
    private static readonly (string Method, string Label, string Suffix)[] Scales =
    [
        ("zscore", "Tipificar (z): (x − media) / desv. estándar", "_z"),
        ("minmax", "Escalar a [0, 1]: (x − mín) / (máx − mín)", "_mm"),
        ("robust", "Robusto: (x − mediana) / rango intercuartílico", "_rob"),
        ("maxabs", "Dividir por el máximo absoluto", "_abs"),
        ("center", "Centrar: x − media", "_c")
    ];

    public const string ResultKey = "resultado";

    public static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var columns = input.RoleNames("cols", multiple: true);
        if (columns.Count == 0)
        {
            return Result.Err<AnalysisResult>(DomainErrorCode.InsufficientData, "Elija al menos una columna numérica.");
        }

        var method = input.Pstr("method", "zscore");
        var scale = Scales.FirstOrDefault(item => item.Method == method);
        if (scale.Method is null)
        {
            return Result.Err<AnalysisResult>(DomainErrorCode.InvalidParameter, "Valor no válido para «Método».");
        }

        var replace = input.Pstr("mode", "new") == "replace";
        var after = frame;
        var usedNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var parameterRows = new List<string[]>(columns.Count);
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        var created = new List<string>();

        foreach (var column in columns)
        {
            if (!frame.Has(column))
            {
                return Result.Err<AnalysisResult>(
                    DomainErrorCode.InvalidInput,
                    $"La columna «{column}» no existe en el dataset.");
            }

            var values = frame.Numeric(column);
            var (shift, divisor) = ScaleParameters(values, method);
            if (!double.IsFinite(divisor) || divisor == 0)
            {
                return Result.Err<AnalysisResult>(
                    DomainErrorCode.InvalidInput,
                    $"«{column}» es constante o tiene muy pocos datos: no se puede escalar.");
            }

            var name = replace ? column : column + scale.Suffix;
            if (!replace && after.Has(name))
            {
                return Result.Err<AnalysisResult>(
                    DomainErrorCode.InvalidInput,
                    $"Ya existe la columna «{name}»: use «Reemplazar las columnas originales» o cambie lo que se añade al nombre.");
            }

            var scaled = new double[values.Length];
            var missing = 0;
            for (var index = 0; index < values.Length; index++)
            {
                if (double.IsNaN(values[index]))
                {
                    scaled[index] = double.NaN;
                    missing++;
                    continue;
                }
                scaled[index] = (values[index] - shift) / divisor;
            }

            after = after.With(DataColumn.Numeric(name, scaled));
            usedNames[column] = name;
            if (!replace) created.Add(name);
            parameterRows.Add([column, FormatHelpers.N(shift), FormatHelpers.N(divisor), JsNumber.ToString(missing)]);
            parameters[column] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["shift"] = shift,
                ["divisor"] = divisor
            };
        }

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen", BuildTiles(after, created.Count)),
            new TableSection(
                "Parámetros usados",
                ["Columna", "Se resta", "Se divide por", "Faltantes"],
                parameterRows.Select(row => (IReadOnlyList<string>)row).ToArray(),
                Note: "Guarde estos valores para aplicar la misma transformación a datos nuevos.")
        };

        var preview = Preview(after, [.. usedNames.Values]);
        if (preview is not null) sections.Add(preview);

        var keyResults = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["n_rows"] = after.RowCount,
            ["n_cols"] = after.ColumnCount,
            ["n_rows_before"] = frame.RowCount,
            ["created"] = created.ToArray(),
            ["parameters"] = parameters
        };

        var datasets = new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
        {
            [ResultKey] = new DatasetResult("Datos transformados", after.Columns)
        };

        return Result.Ok(new AnalysisResult(
            "data-standardize",
            "Tipificar y normalizar",
            sections,
            Subtitle: scale.Label,
            Datasets: datasets,
            KeyResults: keyResults,
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static List<TileItem> BuildTiles(Frame after, int created) =>
    [
        new TileItem("Filas", JsNumber.ToString(after.RowCount)),
        new TileItem("Columnas", JsNumber.ToString(after.ColumnCount)),
        new TileItem("Columnas nuevas", JsNumber.ToString(created))
    ];

    private static (double Shift, double Divisor) ScaleParameters(double[] values, string method)
    {
        var present = values.Where(value => !double.IsNaN(value)).ToArray();
        if (present.Length == 0) return (double.NaN, double.NaN);
        return method switch
        {
            "zscore" => (NumericMath.Mean(present), NumericMath.Sd(present)),
            "minmax" => (NumericMath.MinOf(present), NumericMath.MaxOf(present) - NumericMath.MinOf(present)),
            "robust" => (
                NumericMath.Median(present),
                NumericMath.Quantile(present, 0.75) - NumericMath.Quantile(present, 0.25)),
            "maxabs" => (0.0, present.Max(Math.Abs)),
            _ => (NumericMath.Mean(present), 1.0)
        };
    }

    private static TableSection? Preview(Frame frame, IReadOnlyList<string> columns)
    {
        var shown = columns.Distinct(StringComparer.Ordinal).Take(12).ToArray();
        if (shown.Length == 0 || frame.RowCount == 0) return null;

        var rows = Math.Min(8, frame.RowCount);
        var tableRows = new List<IReadOnlyList<string>>(rows);
        for (var row = 0; row < rows; row++)
        {
            var cells = new string[shown.Length];
            for (var index = 0; index < shown.Length; index++)
            {
                var column = frame.Column(shown[index]);
                cells[index] = column.IsMissing(row) ? "*" : FormatHelpers.N(column.NumberAt(row));
            }
            tableRows.Add(cells);
        }

        var note = frame.RowCount > rows
            ? $"Primeras {rows} de {FormatThousands(frame.RowCount)} filas."
            : null;
        return new TableSection("Vista previa", shown, tableRows, Note: note);
    }

    private static string FormatThousands(int value) =>
        value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");
}
