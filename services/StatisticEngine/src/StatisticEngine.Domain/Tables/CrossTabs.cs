using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Tables;

/// <summary>
/// Valor de una tabla: el motor TypeScript acepta <c>string | number</c> y aplica <c>String()</c>.
/// Se modela explícitamente para que la conversión (y con ella las etiquetas) sea idéntica.
/// </summary>
public readonly record struct CellValue(string Text)
{
    public static CellValue FromText(string text) => new(text);

    /// <summary><c>String(number)</c> de ECMAScript.</summary>
    public static CellValue FromNumber(double value) => new(JsNumber.ToString(value));
}

/// <summary>Fila de <see cref="CrossTabs.Tally"/>.</summary>
public sealed record TallyRow(string Value, double Count, double Percent, double CumulativePercent);

/// <summary>Tabla de contingencia con estadístico chi-cuadrada.</summary>
public sealed record CrossTabResult(
    IReadOnlyList<string> RowLabels,
    IReadOnlyList<string> ColumnLabels,
    IReadOnlyList<IReadOnlyList<double>> Counts,
    IReadOnlyList<IReadOnlyList<double>> Expected,
    IReadOnlyList<double> RowTotals,
    IReadOnlyList<double> ColumnTotals,
    double Total,
    double Statistic,
    double Df,
    double PValue);

/// <summary>Fila de <see cref="CrossTabs.GoodnessOfFit"/>.</summary>
public sealed record GoodnessOfFitRow(string Label, double Observed, double Expected, double Contribution);

/// <summary>Bondad de ajuste chi-cuadrada por categorías.</summary>
public sealed record GoodnessOfFitResult(
    double Statistic,
    double Df,
    double PValue,
    IReadOnlyList<GoodnessOfFitRow> Rows);

/// <summary>Tablas de frecuencias y tabulaciones cruzadas (port 1:1 de <c>tables.ts</c>).</summary>
public static class CrossTabs
{
    /// <summary><c>tally(values)</c>: recuento por valor con orden de inserción estable.</summary>
    public static IReadOnlyList<TallyRow> Tally(IReadOnlyList<CellValue> values)
    {
        var counts = new Dictionary<string, double>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var value in values)
        {
            if (!counts.ContainsKey(value.Text))
            {
                counts[value.Text] = 0;
                order.Add(value.Text);
            }
            counts[value.Text] += 1;
        }

        var total = values.Count;
        var output = new List<TallyRow>(order.Count);
        var cumulative = 0.0;
        // paridad: tables.ts:19 — orden descendente por recuento y estable ante empates.
        foreach (var key in order.OrderByDescending(item => counts[item]))
        {
            var count = counts[key];
            cumulative += count;
            output.Add(new TallyRow(
                key,
                count,
                total > 0 ? count / total * 100 : 0,
                total > 0 ? cumulative / total * 100 : 0));
        }
        return output;
    }

    /// <summary><c>crossTabulation(rows, columns)</c>.</summary>
    public static Result<CrossTabResult> CrossTabulation(
        IReadOnlyList<CellValue> rows,
        IReadOnlyList<CellValue> columns)
    {
        var n = Math.Min(rows.Count, columns.Count);
        if (n == 0) return Result<CrossTabResult>.Err(DomainErrorCode.InsufficientData, "No hay datos para tabular.");

        var rowLabels = DistinctSorted(rows, n);
        var columnLabels = DistinctSorted(columns, n);

        var rowIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < rowLabels.Count; i++) rowIndexes[rowLabels[i]] = i;
        var columnIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < columnLabels.Count; i++) columnIndexes[columnLabels[i]] = i;

        var counts = new double[rowLabels.Count][];
        for (var i = 0; i < rowLabels.Count; i++) counts[i] = new double[columnLabels.Count];
        for (var i = 0; i < n; i++)
        {
            var rowIndex = rowIndexes[rows[i].Text];
            var columnIndex = columnIndexes[columns[i].Text];
            counts[rowIndex][columnIndex] += 1;
        }

        var chi = HypothesisTests.ChiSquareIndependence(AsRows(counts));
        if (chi.IsError)
        {
            return Result<CrossTabResult>.Err(chi.Error.Code, chi.Error.Message, chi.Error.Details);
        }

        var rowTotals = new double[counts.Length];
        for (var i = 0; i < counts.Length; i++)
        {
            var total = 0.0;
            for (var j = 0; j < counts[i].Length; j++) total += counts[i][j];
            rowTotals[i] = total;
        }

        var columnTotals = new double[columnLabels.Count];
        for (var j = 0; j < columnLabels.Count; j++)
        {
            var total = 0.0;
            for (var i = 0; i < counts.Length; i++) total += counts[i][j];
            columnTotals[j] = total;
        }

        return Result.Ok(new CrossTabResult(
            RowLabels: rowLabels,
            ColumnLabels: columnLabels,
            Counts: AsRows(counts),
            Expected: chi.Value.Expected,
            RowTotals: rowTotals,
            ColumnTotals: columnTotals,
            Total: n,
            Statistic: chi.Value.Statistic,
            Df: chi.Value.Df,
            PValue: chi.Value.PValue));
    }

    /// <summary><c>goodnessOfFit(labels, observed, expectedProportions?)</c>.</summary>
    public static Result<GoodnessOfFitResult> GoodnessOfFit(
        IReadOnlyList<string> labels,
        IReadOnlyList<double> observed,
        IReadOnlyList<double>? expectedProportions = null)
    {
        if (labels.Count != observed.Count || labels.Count < 2)
        {
            return Result<GoodnessOfFitResult>.Err(DomainErrorCode.InvalidInput, "Categorías incompatibles.");
        }
        var total = NumericMath.Sum(observed);
        if (total <= 0)
        {
            return Result<GoodnessOfFitResult>.Err(
                DomainErrorCode.InsufficientData,
                "Las frecuencias observadas deben sumar más de cero.");
        }
        var proportions = new double[labels.Count];
        if (expectedProportions is null)
        {
            for (var i = 0; i < proportions.Length; i++) proportions[i] = 1.0 / labels.Count;
        }
        else
        {
            for (var i = 0; i < proportions.Length; i++) proportions[i] = expectedProportions[i];
        }
        var sumProportions = NumericMath.Sum(proportions);
        if (Math.Abs(sumProportions - 1) > 1e-6)
        {
            return Result<GoodnessOfFitResult>.Err(
                DomainErrorCode.InvalidInput,
                "Las proporciones esperadas deben sumar 1.");
        }

        var statistic = 0.0;
        var rows = new List<GoodnessOfFitRow>(labels.Count);
        for (var i = 0; i < labels.Count; i++)
        {
            var expected = total * proportions[i];
            var contribution = expected > 0
                ? Math.Pow(observed[i] - expected, 2) / expected
                : double.PositiveInfinity;
            statistic += contribution;
            rows.Add(new GoodnessOfFitRow(labels[i], observed[i], expected, contribution));
        }
        var df = labels.Count - 1.0;
        return Result.Ok(new GoodnessOfFitResult(
            statistic,
            df,
            SpecialFunctions.Gammq(df / 2, statistic / 2),
            rows));
    }

    private static List<string> DistinctSorted(IReadOnlyList<CellValue> values, int count)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var labels = new List<string>();
        for (var i = 0; i < count; i++)
        {
            if (seen.Add(values[i].Text)) labels.Add(values[i].Text);
        }
        labels.Sort(StringComparer.Ordinal);
        return labels;
    }

    private static IReadOnlyList<double>[] AsRows(double[][] rows)
    {
        var output = new IReadOnlyList<double>[rows.Length];
        for (var i = 0; i < rows.Length; i++) output[i] = rows[i];
        return output;
    }
}
