using System.Text.Json.Serialization;

namespace StatisticEngine.Domain.Charts;

/// <summary>
/// Especificación serializable de gráficos: el dominio describe los datos y el cliente dibuja.
/// Espeja la unión discriminada <c>ChartSpec</c> de <c>model/chart.ts</c>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(HistogramChart), "histogram")]
[JsonDerivedType(typeof(BoxPlotChart), "boxplot")]
[JsonDerivedType(typeof(ScatterChart), "scatter")]
[JsonDerivedType(typeof(ControlChart), "control")]
[JsonDerivedType(typeof(ProbabilityChart), "probability")]
[JsonDerivedType(typeof(ResidualsChart), "residuals")]
[JsonDerivedType(typeof(TimeSeriesChart), "timeseries")]
[JsonDerivedType(typeof(LineChart), "line")]
[JsonDerivedType(typeof(BarChart), "bar")]
[JsonDerivedType(typeof(ParetoChart), "pareto")]
[JsonDerivedType(typeof(PcaChart), "pca")]
public abstract record ChartSpec(string Title);

public sealed record HistogramChart(string Title, double[] Values, int? Bins = null) : ChartSpec(Title);

public sealed record BoxPlotGroup(string Label, double[] Values);

public sealed record BoxPlotChart(string Title, IReadOnlyList<BoxPlotGroup> Groups) : ChartSpec(Title);

public sealed record ScatterPoint(double X, double Y);

public sealed record ScatterChart(
    string Title,
    IReadOnlyList<ScatterPoint> Points,
    string XLabel,
    string YLabel) : ChartSpec(Title);

public sealed record ControlPointSpec(int Index, double Value);

public sealed record ControlChart(
    string Title,
    IReadOnlyList<ControlPointSpec> Points,
    double Center,
    double Lower,
    double Upper,
    IReadOnlyList<int> Violations,
    string YLabel) : ChartSpec(Title);

public sealed record ProbabilityPoint(double X, double Y, double? Rank = null);

public sealed record ProbabilityChart(
    string Title,
    IReadOnlyList<ProbabilityPoint> Points,
    double Slope,
    double Intercept) : ChartSpec(Title);

public sealed record ResidualsChart(
    string Title,
    double[] Fitted,
    double[] Residuals,
    double[] Order) : ChartSpec(Title);

public sealed record TimeSeriesChart(string Title, double[] Values, double[]? Forecast = null) : ChartSpec(Title);

public sealed record LineSeries(string Label, double[] Values);

public sealed record LineChart(string Title, IReadOnlyList<LineSeries> Series, string? XLabel = null) : ChartSpec(Title);

public sealed record BarChart(string Title, IReadOnlyList<string> Labels, double[] Values) : ChartSpec(Title);

public sealed record ParetoChart(string Title, IReadOnlyList<string> Labels, double[] Values) : ChartSpec(Title);

public sealed record PcaChart(string Title, double[] Explained, double[] Cumulative) : ChartSpec(Title);
