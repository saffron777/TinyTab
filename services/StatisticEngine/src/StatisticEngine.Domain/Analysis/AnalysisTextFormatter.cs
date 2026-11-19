using System.Text;

namespace StatisticEngine.Domain.Analysis;

/// <summary>
/// Renderizado de texto de la sesión (espeja <c>formatAnalysisResult</c> y
/// <c>sectionToPlainText</c> de <c>analysis/index.ts</c>). Se usa para <c>?format=text</c>
/// y para verificar la paridad del formato.
/// </summary>
public static class AnalysisTextFormatter
{
    private const string Rule = "─";

    public static string Format(AnalysisResult result)
    {
        var lines = new List<string> { result.Title };
        if (result.Subtitle is not null) lines.Add(result.Subtitle);
        lines.Add(string.Empty);

        foreach (var section in result.Sections)
        {
            if (section is HeadingSection heading)
            {
                lines.Add(heading.Title);
                lines.Add(string.Empty);
                continue;
            }

            lines.Add($"{Rule} {section.Title}");
            lines.Add(RenderSection(section));
            lines.Add(string.Empty);
        }

        if (result.Charts is { Count: > 0 } charts)
        {
            lines.Add($"Gráficos generados: {charts.Count} ({string.Join(", ", charts.Select(DescribeKind))})");
        }

        if (result.Datasets is { Count: > 0 } datasets)
        {
            lines.Add($"Datasets generados: {string.Join(", ", datasets.Select(pair => pair.Value.Label))}");
        }

        return TrimEnd(string.Join('\n', lines));
    }

    public static string FormatTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var widths = new int[headers.Count];
        for (var index = 0; index < headers.Count; index++)
        {
            var width = headers[index].Length;
            foreach (var row in rows)
            {
                var cell = index < row.Count ? row[index] : string.Empty;
                if (cell.Length > width) width = cell.Length;
            }
            widths[index] = width;
        }

        var output = new List<string> { RenderRow(headers, widths) };
        output.Add(string.Join("  ", widths.Select(width => Repeat(Rule, width))));
        output.AddRange(rows.Select(row => RenderRow(row, widths)));
        return string.Join('\n', output);
    }

    public static string SectionToPlainText(AnalysisSection section) =>
        section switch
        {
            TextSection text => $"{text.Title}\n{text.Text}",
            TableSection table => $"{table.Title}\n{FormatTable(table.Headers, table.Rows)}",
            HeadingSection heading => heading.Title,
            TilesSection tiles => $"{tiles.Title}\n{RenderTiles(tiles)}",
            AlertSection alert => $"{alert.Title}\n[{alert.Severity}] {alert.Message}",
            _ => section.Title
        };

    private static string RenderSection(AnalysisSection section) =>
        section switch
        {
            TextSection text => text.Text,
            TableSection table => FormatTable(table.Headers, table.Rows) + NoteLine(table.Note),
            TilesSection tiles => RenderTiles(tiles),
            AlertSection alert => $"[{alert.Severity}] {alert.Message}",
            _ => string.Empty
        };

    private static string RenderTiles(TilesSection tiles)
    {
        var builder = new StringBuilder();
        foreach (var item in tiles.Items)
        {
            if (builder.Length > 0) builder.Append('\n');
            builder.Append(item.Label).Append(": ").Append(item.Value);
            if (item.Hint is not null) builder.Append(" (").Append(item.Hint).Append(')');
        }
        return builder.ToString();
    }

    private static string NoteLine(string? note) => note is null ? string.Empty : $"\n{note}";

    private static string RenderRow(IReadOnlyList<string> row, int[] widths)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < row.Count; index++)
        {
            if (index > 0) builder.Append("  ");
            var cell = row[index] ?? string.Empty;
            var width = index < widths.Length ? widths[index] : cell.Length;
            builder.Append(cell.PadRight(width));
        }
        return TrimEnd(builder.ToString());
    }

    /// <summary>Equivalente a <c>String.prototype.repeat</c> para el carácter de regla.</summary>
    private static string Repeat(string value, int count) => count <= 0 ? string.Empty : string.Concat(Enumerable.Repeat(value, count));

    private static string TrimEnd(string value) => value.TrimEnd(' ', '\t', '\n', '\r');

    private static string DescribeKind(Charts.ChartSpec chart) =>
        chart switch
        {
            Charts.HistogramChart => "histogram",
            Charts.BoxPlotChart => "boxplot",
            Charts.ScatterChart => "scatter",
            Charts.ControlChart => "control",
            Charts.ProbabilityChart => "probability",
            Charts.ResidualsChart => "residuals",
            Charts.TimeSeriesChart => "timeseries",
            Charts.LineChart => "line",
            Charts.BarChart => "bar",
            Charts.ParetoChart => "pareto",
            Charts.PcaChart => "pca",
            Charts.SeriesChart => "series",
            Charts.ScatterGroupsChart => "scatter-groups",
            Charts.HeatmapChart => "heatmap",
            Charts.ContourChart => "contour",
            Charts.Scatter3DChart => "scatter3d",
            Charts.SplomChart => "splom",
            Charts.PieChart => "pie",
            Charts.IntervalChart => "interval",
            Charts.StemChart => "stem",
            Charts.GroupedBarChart => "grouped-bar",
            _ => "unknown"
        };
}
