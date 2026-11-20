using StatisticEngine.Domain.Analysis;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Data.Operations;

/// <summary>Faltantes, atípicos, muestras y particiones.</summary>
public static class CleaningOperations
{
    public static readonly AnalysisDefinition Missing = new(
        "missing",
        "Valores faltantes: eliminar o reemplazar",
        "Datos",
        "Elimina filas o columnas con faltantes, o los reemplaza por media, mediana, moda, valor fijo, vecino o interpolación.",
        [],
        [],
        [
            new("method", "Método", AnalysisParamKinds.Select, "mean",
                [
                    new("drop_rows", "Eliminar las filas con algún faltante"), new("drop_columns", "Eliminar las columnas con demasiados faltantes"),
                    new("mean", "Reemplazar por la media"), new("median", "Reemplazar por la mediana"), new("mode", "Reemplazar por la moda"),
                    new("constant", "Reemplazar por un valor fijo"), new("ffill", "Arrastrar el último valor conocido"),
                    new("bfill", "Traer el siguiente valor conocido"), new("interpolate", "Interpolación lineal")
                ], false),
            new("value", "Valor de reemplazo", AnalysisParamKinds.Text, "", [], false),
            new("threshold", "Eliminar si faltan más del (%)", AnalysisParamKinds.Number, 50.0, [], false, Minimum: 0, Maximum: 99.9),
            new("flag", "Añadir columna indicadora 0/1", AnalysisParamKinds.Boolean, false, [], false)
        ],
        RunMissing,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Calidad de los datos",
        Roles: [new ColumnRole("cols", "Columnas (si no elige ninguna, todas)", ColumnKinds.Any, Multiple: true, Optional: true)],
        Outputs: [new AnalysisOutputSpec(RowOperations.ResultKey, "Datos tratados", Required: true)],
        NeedsDataFrame: true);

    public static readonly AnalysisDefinition Outliers = new(
        "outliers",
        "Valores atípicos",
        "Datos",
        "Detecta atípicos (IQR, z, MAD, Grubbs) y los marca, reemplaza, limita o elimina.",
        [],
        [],
        [
            new("method", "Método de detección", AnalysisParamKinds.Select, "iqr",
                [new("iqr", "Rango intercuartílico"), new("zscore", "Puntuación z"), new("mad", "Z modificada con MAD"), new("grubbs", "Prueba de Grubbs")], false),
            new("threshold", "k (umbral)", AnalysisParamKinds.Number, 1.5, [], false, Minimum: 0.1, Maximum: 20),
            new("alpha", "Nivel α", AnalysisParamKinds.Number, 0.05, [], false, Minimum: 0.001, Maximum: 0.5),
            new("action", "Qué hacer con ellos", AnalysisParamKinds.Select, "report",
                [
                    new("report", "Solo informar"), new("flag", "Añadir columna indicadora 0/1"),
                    new("missing", "Reemplazar por dato faltante"), new("winsorize", "Limitar al valor límite"),
                    new("remove", "Eliminar las filas con algún atípico")
                ], false)
        ],
        RunOutliers,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Calidad de los datos",
        Roles: [new ColumnRole("cols", "Columnas numéricas", ColumnKinds.Numeric, Multiple: true)],
        Outputs: [new AnalysisOutputSpec(RowOperations.ResultKey, "Datos tratados", Required: true)],
        NeedsDataFrame: true);

    private static Result<AnalysisResult> RunMissing(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var columns = input.RoleNames("cols");
        if (columns.Count == 0) columns = frame.Columns.Select(column => column.Name).ToArray();
        var method = input.Pstr("method", "mean");
        var after = frame;
        var report = new List<IReadOnlyList<string>>();
        if (method == "drop_rows")
        {
            var rows = frame.CompleteCaseIndices(columns);
            after = frame.SubsetRows(rows);
            report.Add(["Filas eliminadas", JsNumber.ToString(frame.RowCount - after.RowCount), JsNumber.ToString(after.RowCount)]);
        }
        else if (method == "drop_columns")
        {
            var threshold = input.Pnum("threshold", 50);
            var keep = frame.Columns.Where(column => 100.0 * MissingCount(column) / Math.Max(1, frame.RowCount) <= threshold).ToArray();
            after = new Frame(keep, frame.RowCount);
            report.Add(["Columnas eliminadas", JsNumber.ToString(frame.ColumnCount - after.ColumnCount), JsNumber.ToString(after.ColumnCount)]);
        }
        else
        {
            foreach (var name in columns)
            {
                if (!frame.Has(name)) continue;
                var column = frame.Column(name);
                var missing = MissingCount(column);
                if (missing == 0) continue;
                var replaced = ReplaceMissing(frame, column, method, input.Pstr("value", ""));
                after = after.With(replaced);
                report.Add([name, JsNumber.ToString(missing), method]);
            }
            if (input.Pbool("flag") && method is not "drop_rows" and not "drop_columns")
            {
                foreach (var name in columns)
                {
                    if (!frame.Has(name)) continue;
                    var source = frame.Column(name);
                    var flags = Enumerable.Range(0, frame.RowCount).Select(row => source.IsMissing(row) ? 1.0 : 0.0).ToArray();
                    after = after.With(DataColumn.Numeric(name + "_faltante", flags));
                }
            }
        }
        return Build("missing", "Datos tratados", frame, after, report.Count > 0 ? report : [["Sin cambios", JsNumber.ToString(after.RowCount), ""]], "Filas y columnas tras el tratamiento.");
    }

    private static Result<AnalysisResult> RunOutliers(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var columns = input.RoleNames("cols", true);
        var method = input.Pstr("method", "iqr");
        var threshold = input.Pnum("threshold", 1.5);
        var alpha = input.Pnum("alpha", 0.05);
        var action = input.Pstr("action", "report");
        var after = frame;
        var report = new List<IReadOnlyList<string>>();
        var removeRows = new HashSet<int>();
        foreach (var name in columns)
        {
            if (!frame.Has(name)) continue;
            var values = frame.Numeric(name);
            var flags = new bool[frame.RowCount];
            var bounds = Bounds(values.Where(value => !double.IsNaN(value)).ToArray(), method, threshold, alpha);
            for (var row = 0; row < frame.RowCount; row++)
            {
                if (double.IsNaN(values[row])) continue;
                if (values[row] < bounds.Low || values[row] > bounds.High)
                {
                    flags[row] = true;
                    removeRows.Add(row);
                }
            }
            report.Add([name, JsNumber.ToString(flags.Count(value => value)), method + " " + ModelHelpers.N(bounds.Low) + " – " + ModelHelpers.N(bounds.High)]);
            if (action == "flag") after = after.With(DataColumn.Numeric(name + "_atipico", flags.Select(value => value ? 1.0 : 0.0).ToArray()));
            else if (action == "missing")
            {
                var replaced = (double[])values.Clone();
                for (var row = 0; row < replaced.Length; row++) if (flags[row]) replaced[row] = double.NaN;
                after = after.With(DataColumn.Numeric(name, replaced));
            }
            else if (action == "winsorize") after = after.With(DataColumn.Numeric(name, values.Select(value => double.IsNaN(value) ? value : Math.Min(Math.Max(value, bounds.Low), bounds.High)).ToArray()));
        }
        if (action == "remove" && removeRows.Count > 0) after = after.SubsetRows(Enumerable.Range(0, frame.RowCount).Where(row => !removeRows.Contains(row)).ToArray());
        return Build("outliers", "Datos tratados", frame, after, report, "Atípicos detectados y tratados según la acción elegida.");
    }

    private static (double Low, double High) Bounds(double[] values, string method, double threshold, double alpha)
    {
        if (values.Length == 0) return (double.NaN, double.NaN);
        if (method == "zscore")
        {
            var mean = values.Average();
            var sd = NumericMath.Sd(values);
            return (mean - threshold * sd, mean + threshold * sd);
        }
        if (method == "mad")
        {
            var median = NumericMath.Quantile(values, 0.5);
            var mad = NumericMath.Quantile(values.Select(value => Math.Abs(value - median)).ToArray(), 0.5);
            var scale = 1.4826 * mad;
            return (median - threshold * scale, median + threshold * scale);
        }
        if (method == "grubbs")
        {
            var mean = values.Average();
            var sd = NumericMath.Sd(values);
            var n = values.Length;
            var t = InferenceDistributions.TQuantile(1 - alpha / (2.0 * n), n - 2);
            var critical = (n - 1) / Math.Sqrt(n) * Math.Sqrt(t * t / (n - 2 + t * t));
            return (mean - critical * sd, mean + critical * sd);
        }
        var q1 = NumericMath.Quantile(values, 0.25);
        var q3 = NumericMath.Quantile(values, 0.75);
        var iqr = q3 - q1;
        return (q1 - threshold * iqr, q3 + threshold * iqr);
    }

    private static DataColumn ReplaceMissing(Frame frame, DataColumn column, string method, string constant)
    {
        if (column.Kind == DataColumnKind.Numeric)
        {
            var values = (double[])column.Numbers.Clone();
            var observed = values.Where(value => !double.IsNaN(value)).ToArray();
            var replacement = method switch
            {
                "median" => NumericMath.Quantile(observed, 0.5),
                "mode" => Mode(observed.Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray()) is { } mode && double.TryParse(mode, out var number) ? number : observed.DefaultIfEmpty(0).Average(),
                "constant" => JsNumber.Parse(constant),
                _ => observed.DefaultIfEmpty(0).Average()
            };
            for (var row = 0; row < values.Length; row++)
            {
                if (!double.IsNaN(values[row])) continue;
                values[row] = method switch
                {
                    "ffill" => Previous(values, row, -1),
                    "bfill" => Previous(values, row, 1),
                    "interpolate" => Interpolate(values, row),
                    _ => replacement
                };
            }
            return DataColumn.Numeric(column.Name, values);
        }
        if (column.Kind == DataColumnKind.Text)
        {
            var values = (string?[])column.Texts.Clone();
            var observed = values.Where(value => !string.IsNullOrEmpty(value)).ToArray();
            var replacement = method == "constant" ? constant : Mode(observed!);
            for (var row = 0; row < values.Length; row++)
            {
                if (!string.IsNullOrEmpty(values[row])) continue;
                values[row] = method switch
                {
                    "ffill" => PreviousText(values, row, -1),
                    "bfill" => PreviousText(values, row, 1),
                    _ => replacement
                };
            }
            return DataColumn.Text(column.Name, values);
        }
        return column;
    }

    private static double Previous(double[] values, int row, int direction)
    {
        for (var index = row + direction; index >= 0 && index < values.Length; index += direction) if (!double.IsNaN(values[index])) return values[index];
        return double.NaN;
    }

    private static string? PreviousText(string?[] values, int row, int direction)
    {
        for (var index = row + direction; index >= 0 && index < values.Length; index += direction) if (!string.IsNullOrEmpty(values[index])) return values[index];
        return null;
    }

    private static double Interpolate(double[] values, int row)
    {
        var left = Previous(values, row, -1);
        var right = Previous(values, row, 1);
        return double.IsNaN(left) ? right : double.IsNaN(right) ? left : (left + right) / 2;
    }

    private static string? Mode(string[] values) => values.Where(value => !string.IsNullOrEmpty(value)).GroupBy(value => value).OrderByDescending(group => group.Count()).Select(group => group.Key).FirstOrDefault();

    private static int MissingCount(DataColumn column) => Enumerable.Range(0, column.Count).Count(column.IsMissing);

    internal static Result<AnalysisResult> Build(string id, string label, Frame before, Frame after, IEnumerable<IReadOnlyList<string>> report, string message)
    {
        return Result.Ok(new AnalysisResult(
            id,
            label,
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Filas antes", JsNumber.ToString(before.RowCount)),
                    new TileItem("Filas después", JsNumber.ToString(after.RowCount)),
                    new TileItem("Columnas", JsNumber.ToString(after.ColumnCount))
                ]),
                new TableSection("Detalle", ["Concepto", "Valor", "Resultado"], report.ToArray()),                new AlertSection("Resultado", "info", message)
            ],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
            {
                [RowOperations.ResultKey] = new DatasetResult(label, after.Columns)
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
