using StatisticEngine.Domain.Analysis;
using StatisticEngine.Domain.Analysis.Analyses.Extensions;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Models;

/// <summary>Piezas de informe compartidas por los modelos portados desde Sigma.</summary>
internal static class ModelHelpers
{
    public const string PredictionsKey = "predicciones";

    public const string PredictionsLabel = "Predicciones y residuos";

    public static AnalysisParamDefinition Alpha() => new(
        "alpha",
        "Nivel de significancia α",
        AnalysisParamKinds.Number,
        0.05,
        [],
        false,
        Minimum: 0.001,
        Maximum: 0.5);

    public static AnalysisParamDefinition Interactions() => new(
        "interactions",
        "Interacciones",
        AnalysisParamKinds.Select,
        "none",
        [
            new("none", "Ninguna"),
            new("two", "Entre pares de predictores"),
            new("full", "Todas")
        ],
        false);

    public static string N(double value, int digits = 4) => FormatHelpers.N(value, digits);

    public static string P(double value) => SigmaText.PValue(value);

    public static string[] CiColumns(double alpha) =>
        [$"IC {SigmaText.General(100 * (1 - alpha))} % inf.", "IC sup."];

    public static void DroppedNote(List<AnalysisSection> sections, Design design)
    {
        if (design.Dropped > 0)
        {
            sections.Add(new AlertSection(
                "Aviso",
                "info",
                $"Se omitieron {design.Dropped} fila(s) con datos faltantes en las columnas del modelo."));
        }
    }

    public static Result<Unit> NeedEnough(Design design, int parameters)
    {
        if (design.RowCount <= parameters)
        {
            return Result.Err<Unit>(
                DomainErrorCode.InsufficientData,
                $"El modelo tiene {parameters} parámetros y solo hay {design.RowCount} filas completas: faltan datos.");
        }
        return Result.Ok(Unit.Value);
    }

    public static TableSection CoefficientTable(Design design, LinearFit fit, double alpha)
    {
        var intervals = fit.ConfidenceIntervals(alpha);
        var rows = new List<IReadOnlyList<string>>();
        for (var index = 0; index < fit.Coefficients.Length; index++)
        {
            var name = design.ColumnNames[index] == "Intercept" ? "Constante" : design.Pretty(design.ColumnNames[index]);
            rows.Add([
                name,
                N(fit.Coefficients[index]),
                N(fit.StandardErrors[index]),
                N(fit.TValues[index]),
                P(fit.PValues[index]),
                N(intervals[index].Low),
                N(intervals[index].High)
            ]);
        }
        return new TableSection(
            "Coeficientes",
            ["Término", "Coeficiente", "Error est.", "t", "Valor p", .. CiColumns(alpha)],
            rows);
    }

    public static DatasetResult Predictions(Frame source, Design design, params (string Name, double[] Values)[] columns)
    {
        var result = new List<DataColumn>(source.Columns.Count + columns.Length);
        result.AddRange(source.Columns);
        foreach (var (name, values) in columns)
        {
            var series = Enumerable.Repeat(double.NaN, source.RowCount).ToArray();
            for (var index = 0; index < Math.Min(values.Length, design.UsedRows.Length); index++)
            {
                series[design.UsedRows[index]] = values[index];
            }
            result.Add(DataColumn.Numeric(name, series));
        }
        return new DatasetResult(PredictionsLabel, result);
    }

    public static Dictionary<string, object?> Coefficients(Design design, LinearFit fit)
    {
        var output = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 0; index < fit.Coefficients.Length; index++)
        {
            output[design.ColumnNames[index] == "Intercept" ? "Intercept" : design.Pretty(design.ColumnNames[index])] = fit.Coefficients[index];
        }
        return output;
    }
}
