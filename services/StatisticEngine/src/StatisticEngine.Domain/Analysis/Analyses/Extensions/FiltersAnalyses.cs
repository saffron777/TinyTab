using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Filtro Hodrick-Prescott (port de <c>series_explore._filters</c>).</summary>
internal static class FiltersAnalyses
{
    public static readonly AnalysisDefinition Filters = new(
        "filters",
        "Filtros: Hodrick-Prescott, Baxter-King, Christiano-Fitzgerald",
        "Componentes, tendencia y filtros",
        "Separa la tendencia del ciclo económico; entrega los componentes como dataset.",
        [],
        [],
        [
            new(
                "filter",
                "Filtro",
                AnalysisParamKinds.Select,
                "hp",
                [
                    new("hp", "Hodrick-Prescott (tendencia suave y ciclo)"),
                    new("bk", "Baxter-King (pasa-banda)"),
                    new("cf", "Christiano-Fitzgerald (pasa-banda)")
                ],
                false),
            new("lambda_", "λ de Hodrick-Prescott (vacío = 129 600 mensual, 1 600 trimestral, 6.25 anual)", AnalysisParamKinds.Number, null, [], true, Minimum: 0.1, VisibleIf: new("filter", ["hp"])),
            new("low", "Periodo mínimo del ciclo", AnalysisParamKinds.Number, 6.0, [], false, Minimum: 2, VisibleIf: new("filter", ["bk", "cf"])),
            new("high", "Periodo máximo del ciclo", AnalysisParamKinds.Number, 32.0, [], false, Minimum: 3, VisibleIf: new("filter", ["bk", "cf"])),
            new("lags", "Rezagos K de Baxter-King", AnalysisParamKinds.Integer, 12.0, [], false, Minimum: 1, Maximum: 100, VisibleIf: new("filter", ["bk"])),
            new("drift", "Quitar la deriva (Christiano-Fitzgerald)", AnalysisParamKinds.Boolean, false, [], false, VisibleIf: new("filter", ["cf"])),
            new("period", "Periodo estacional (0 = según la fecha)", AnalysisParamKinds.Integer, 0.0, [], false, Minimum: 0, Maximum: 400)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Componentes, tendencia y filtros",
        Roles:
        [
            new ColumnRole("series", "Serie", ColumnKinds.Numeric),
            new ColumnRole("time", "Columna de tiempo (opcional)", ColumnKinds.DateTime, Optional: true, Help: "Sin ella se usa el orden de las filas.")
        ],
        Outputs: [new AnalysisOutputSpec("filtrada", "Serie, tendencia y ciclo")]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        if (input.Pstr("filter", "hp") != "hp")
        {
            return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "Los filtros Baxter-King y Christiano-Fitzgerald aún no están disponibles; use Hodrick-Prescott.");
        }
        var frame = input.AsFrame();
        var seriesColumn = ExtendedHelpers.FirstRole(input, "series", "Serie");
        var timeColumn = ExtendedHelpers.FirstRole(input, "time", "Columna de tiempo");
        var loaded = SeriesLoader.Load(frame, seriesColumn, timeColumn, input.Pint("period", 0), minimum: 20);
        if (loaded.IsError) return Result.Err<AnalysisResult>(loaded.Error.Code, loaded.Error.Message);
        var series = loaded.Value;
        var n = series.Values.Length;
        var lambda = input.Params.TryGetValue("lambda_", out var rawLambda) && rawLambda is not null
            ? input.Pnum("lambda_", 1600)
            : series.Period switch { 12 => 129600.0, 4 => 1600.0, 1 => 6.25, _ => 1600.0 };

        var matrix = new double[n][];
        for (var i = 0; i < n; i++) matrix[i] = new double[n];
        for (var i = 0; i < n; i++) matrix[i][i] = 1;
        for (var row = 0; row < n - 2; row++)
        {
            var coefficients = new[] { 1.0, -2.0, 1.0 };
            for (var i = 0; i < 3; i++)
            {
                for (var j = 0; j < 3; j++) matrix[row + i][row + j] += lambda * coefficients[i] * coefficients[j];
            }
        }
        var solved = LinearAlgebra.SolveLinear(matrix, series.Values);
        if (solved.IsError) return Result.Err<AnalysisResult>(solved.Error.Code, solved.Error.Message);
        var trend = solved.Value;
        var cycle = series.Values.Select((value, index) => value - trend[index]).ToArray();
        var cycleSd = Math.Sqrt(cycle.Sum(value => value * value) / n);
        var yMean = series.Values.Average();
        var yVariance = series.Values.Sum(value => Math.Pow(value - yMean, 2)) / n;
        var cycleMean = cycle.Average();
        var cycleVariance = cycle.Sum(value => Math.Pow(value - cycleMean, 2)) / n;

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Desv. est. del ciclo", ModelHelpers.N(cycleSd)),
                new TileItem("% de la varianza en el ciclo", ModelHelpers.N(100 * cycleVariance / yVariance)),
                new TileItem("Observaciones usadas", JsNumber.ToString(n))
            ])
        };
        if (series.Dropped > 0)
        {
            sections.Add(new AlertSection("Aviso", "info", $"Se omitieron {series.Dropped} valor(es) faltante(s) en los extremos de la serie."));
        }

        var seriesPoints = new List<ScatterPoint>(n);
        for (var index = 0; index < n; index++) seriesPoints.Add(new ScatterPoint(index + 1, series.Values[index]));
        var trendPoints = seriesPoints.Select((point, index) => new ScatterPoint(point.X, trend[index])).ToList();
        var cyclePoints = cycle.Select((value, index) => new ScatterPoint(index + 1, value)).ToList();

        var dates = series.Labels.Select(label => DateTime.TryParse(label, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) ? date : (DateTime?)null).ToArray();
        var dataset = new DatasetResult(
            "Serie, tendencia y ciclo",
            [
                DataColumn.DateTime("tiempo", dates),
                DataColumn.Numeric(seriesColumn, series.Values),
                DataColumn.Numeric("tendencia", trend),
                DataColumn.Numeric("ciclo", cycle)
            ]);

        return Result.Ok(new AnalysisResult(
            "filters",
            $"Filtro Hodrick-Prescott: {seriesColumn}",
            sections,
            Subtitle: $"n = {n} · λ = {SigmaText.General(lambda)}",
            Charts:
            [
                new SeriesChart(
                    "Serie y componente de tendencia",
                    [new XYSeries("Serie", seriesPoints.Select(point => point.X).ToArray(), series.Values), new XYSeries("Tendencia", trendPoints.Select(point => point.X).ToArray(), trend)],
                    "Tiempo",
                    seriesColumn),
                new SeriesChart(
                    "Componente cíclico",
                    [new XYSeries("Ciclo", cyclePoints.Select(point => point.X).ToArray(), cycle)],
                    "Tiempo",
                    "Ciclo")
            ],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal) { ["filtrada"] = dataset },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal) { ["cycle_sd"] = cycleSd },
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
