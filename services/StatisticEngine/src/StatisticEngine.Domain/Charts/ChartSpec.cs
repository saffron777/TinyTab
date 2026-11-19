using System.Text.Json.Serialization;

namespace StatisticEngine.Domain.Charts;

/// <summary>
/// Especificación serializable de gráficos: el dominio describe los datos y el cliente dibuja.
/// Espeja la unión discriminada <c>ChartSpec</c> de <c>model/chart.ts</c> y añade las variantes que
/// necesitan los análisis extendidos (series X/Y, grupos, mapas de calor, contornos, 3D, splom,
/// intervalos, barras agrupadas y tallos).
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
[JsonDerivedType(typeof(SeriesChart), "series")]
[JsonDerivedType(typeof(ScatterGroupsChart), "scatter-groups")]
[JsonDerivedType(typeof(HeatmapChart), "heatmap")]
[JsonDerivedType(typeof(ContourChart), "contour")]
[JsonDerivedType(typeof(Scatter3DChart), "scatter3d")]
[JsonDerivedType(typeof(SplomChart), "splom")]
[JsonDerivedType(typeof(PieChart), "pie")]
[JsonDerivedType(typeof(IntervalChart), "interval")]
[JsonDerivedType(typeof(StemChart), "stem")]
[JsonDerivedType(typeof(GroupedBarChart), "grouped-bar")]
public abstract record ChartSpec(string Title);

public sealed record HistogramChart(string Title, double[] Values, int? Bins = null) : ChartSpec(Title);

public sealed record BoxPlotGroup(string Label, double[] Values);

public sealed record BoxPlotChart(
    string Title,
    IReadOnlyList<BoxPlotGroup> Groups,
    string? Mode = null) : ChartSpec(Title);

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

/// <summary>Serie X/Y con etiqueta (el eje X es explícito, a diferencia de <see cref="LineChart"/>).</summary>
public sealed record XYSeries(string Label, double[] X, double[] Y, string? Style = null);

public sealed record SeriesChart(
    string Title,
    IReadOnlyList<XYSeries> Series,
    string XLabel,
    string YLabel) : ChartSpec(Title);

/// <summary>Nube de puntos por grupo, con las líneas de ajuste opcionales.</summary>
public sealed record ScatterGroup(string Label, IReadOnlyList<ScatterPoint> Points);

public sealed record ScatterFit(string Label, double[] X, double[] Y, string? Style = null);

public sealed record ScatterGroupsChart(
    string Title,
    IReadOnlyList<ScatterGroup> Groups,
    string XLabel,
    string YLabel,
    IReadOnlyList<ScatterFit>? Fits = null) : ChartSpec(Title);

public sealed record HeatmapChart(
    string Title,
    IReadOnlyList<string> XLabels,
    IReadOnlyList<string> YLabels,
    IReadOnlyList<double[]> Values) : ChartSpec(Title);

public sealed record ContourGrid(string Label, double[] X, double[] Y, double[][] Z);

public sealed record ContourChart(
    string Title,
    IReadOnlyList<ContourGrid> Grids,
    string XLabel,
    string YLabel) : ChartSpec(Title);

public sealed record Point3D(double X, double Y, double Z, string? Label = null);

public sealed record Scatter3DChart(
    string Title,
    IReadOnlyList<Point3D> Points,
    string XLabel,
    string YLabel,
    string ZLabel) : ChartSpec(Title);

public sealed record SplomPair(int XIndex, int YIndex, IReadOnlyList<ScatterPoint> Points);

/// <summary>Matriz de dispersión: variables y los pares (i, j) con sus puntos.</summary>
public sealed record SplomChart(
    string Title,
    IReadOnlyList<string> Variables,
    IReadOnlyList<SplomPair> Pairs) : ChartSpec(Title);

public sealed record PieChart(string Title, IReadOnlyList<string> Labels, double[] Values) : ChartSpec(Title);

public sealed record IntervalPoint(string Label, double Estimate, double Lower, double Upper);

public sealed record IntervalChart(string Title, IReadOnlyList<IntervalPoint> Points, string YLabel) : ChartSpec(Title);

public sealed record StemChart(string Title, double[] X, double[] Y, string XLabel, string YLabel) : ChartSpec(Title);

public sealed record BarSeries(string Label, double[] Values);

public sealed record GroupedBarChart(
    string Title,
    IReadOnlyList<string> Categories,
    IReadOnlyList<BarSeries> Series,
    bool Stacked = false) : ChartSpec(Title);
