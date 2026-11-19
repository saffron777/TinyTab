using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Descomposición clásica aditiva (port de <c>series_explore._decompose</c>).</summary>
internal static class DecomposeAnalyses
{
    public static readonly AnalysisDefinition Decompose = new(
        "decompose",
        "Descomposición (clásica y STL)",
        "Componentes, tendencia y filtros",
        "Separa tendencia, estacionalidad y residuo; mide la fuerza de cada componente y entrega la serie desestacionalizada.",
        [],
        [],
        [
            new(
                "method",
                "Método",
                AnalysisParamKinds.Select,
                "stl",
                [
                    new("classical_add", "Clásica aditiva (y = T + S + R)"),
                    new("classical_mul", "Clásica multiplicativa (y = T × S × R)"),
                    new("stl", "STL (LOESS, aditiva)"),
                    new("stl_robust", "STL robusta a atípicos")
                ],
                false),
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
        Outputs: [new AnalysisOutputSpec("componentes", "Componentes de la serie")]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var method = input.Pstr("method", "stl");
        if (method != "classical_add")
        {
            return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "Solo la descomposición clásica aditiva está disponible por ahora; STL y multiplicativa quedan pendientes.");
        }
        var frame = input.AsFrame();
        var seriesColumn = ExtendedHelpers.FirstRole(input, "series", "Serie");
        var timeColumn = ExtendedHelpers.FirstRole(input, "time", "Columna de tiempo");
        var loaded = SeriesLoader.Load(frame, seriesColumn, timeColumn, input.Pint("period", 0));
        if (loaded.IsError) return Result.Err<AnalysisResult>(loaded.Error.Code, loaded.Error.Message);
        var series = loaded.Value;
        var n = series.Values.Length;
        var period = series.Period;
        if (period < 2 || n < 2 * period)
        {
            return ExtendedHelpers.Invalid("Indique el periodo estacional y al menos 2 ciclos completos.");
        }

        var filterLength = period % 2 == 0 ? period + 1 : period;
        var weights = new double[filterLength];
        if (period % 2 == 0)
        {
            weights[0] = 0.5;
            for (var index = 1; index < filterLength - 1; index++) weights[index] = 1;
            weights[^1] = 0.5;
            for (var index = 0; index < filterLength; index++) weights[index] /= period;
        }
        else
        {
            for (var index = 0; index < filterLength; index++) weights[index] = 1.0 / period;
        }
        var trend = Enumerable.Repeat(double.NaN, n).ToArray();
        var trimHead = (int)Math.Ceiling(filterLength / 2.0) - 1;
        for (var start = 0; start + filterLength <= n; start++)
        {
            var sum = 0.0;
            for (var offset = 0; offset < filterLength; offset++) sum += weights[offset] * series.Values[start + offset];
            trend[start + trimHead] = sum;
        }
        var detrended = series.Values.Select((value, index) => value - trend[index]).ToArray();
        var seasonalMeans = new double[period];
        for (var position = 0; position < period; position++)
        {
            var values = Enumerable.Range(position, n - position).Where(index => index % period == position && double.IsFinite(detrended[index]))
                .Select(index => detrended[index]).ToArray();
            seasonalMeans[position] = values.Length > 0 ? values.Average() : double.NaN;
        }
        var seasonalMean = seasonalMeans.Where(double.IsFinite).Average();
        for (var position = 0; position < period; position++) seasonalMeans[position] -= seasonalMean;
        var seasonal = Enumerable.Range(0, n).Select(index => seasonalMeans[index % period]).ToArray();
        var residual = Enumerable.Range(0, n).Select(index => detrended[index] - seasonal[index]).ToArray();
        var adjusted = Enumerable.Range(0, n).Select(index => series.Values[index] - seasonal[index]).ToArray();

        var trendStrength = Strength(residual, trend);
        var seasonalStrength = Strength(residual, seasonal);
        var residualSd = Math.Sqrt(residual.Where(double.IsFinite).Sum(value => value * value) / residual.Count(double.IsFinite));

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Periodo", JsNumber.ToString(period)),
                new TileItem("Fuerza de la tendencia", ModelHelpers.N(trendStrength)),
                new TileItem("Fuerza de la estacionalidad", ModelHelpers.N(seasonalStrength)),
                new TileItem("Desv. est. del residuo", ModelHelpers.N(residualSd))
            ]),
            new AlertSection(
                "Aviso",
                "info",
                "Fuerza = 1 − Var(residuo) / Var(componente + residuo): cerca de 1 el componente domina, cerca de 0 casi no existe (Hyndman y Athanasopoulos).")
        };
        if (series.Dropped > 0)
        {
            sections.Add(new AlertSection("Aviso", "info", $"Se omitieron {series.Dropped} valor(es) faltante(s) en los extremos de la serie."));
        }
        sections.Add(new TableSection(
            "Índices estacionales (posición en el ciclo)",
            ["Posición", "Efecto estacional"],
            Enumerable.Range(0, period).Select(position => (IReadOnlyList<string>)new[]
            {
                JsNumber.ToString(position + 1),
                ModelHelpers.N(seasonalMeans[position])
            }).ToArray(),
            Note: "Aditivo: efecto en las unidades de la serie."));

        var t = Enumerable.Range(1, n).Select(value => (double)value).ToArray();
        var dates = series.Labels.Select(label => DateTime.TryParse(label, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) ? date : (DateTime?)null).ToArray();
        var dataset = new DatasetResult(
            "Componentes de la serie",
            [
                DataColumn.DateTime("tiempo", dates),
                DataColumn.Numeric(seriesColumn, series.Values),
                DataColumn.Numeric("tendencia", trend),
                DataColumn.Numeric("estacional", seasonal),
                DataColumn.Numeric("residuo", residual),
                DataColumn.Numeric("ajustada_estacionalmente", adjusted)
            ]);

        return Result.Ok(new AnalysisResult(
            "decompose",
            $"Descomposición de {seriesColumn}",
            sections,
            Subtitle: $"Clásica aditiva (y = T + S + R) · periodo {period} · n = {n}",
            Charts:
            [
                new SeriesChart(
                    $"Componentes de {seriesColumn}",
                    [
                        new XYSeries("Observado", t, series.Values),
                        new XYSeries("Tendencia", t, trend),
                        new XYSeries("Estacional", t, seasonal),
                        new XYSeries("Residuo", t, residual)
                    ],
                    "Tiempo",
                    seriesColumn)
            ],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal) { ["componentes"] = dataset },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["period"] = period,
                ["trend_strength"] = trendStrength,
                ["seasonal_strength"] = seasonalStrength
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double Strength(double[] residual, double[] component)
    {
        var pairs = Enumerable.Range(0, residual.Length)
            .Where(index => double.IsFinite(residual[index]) && double.IsFinite(component[index]))
            .Select(index => (Residual: residual[index], Component: component[index]))
            .ToArray();
        if (pairs.Length == 0) return 0;
        var combined = pairs.Select(pair => pair.Residual + pair.Component).ToArray();
        var residualValues = pairs.Select(pair => pair.Residual).ToArray();
        var combinedVariance = Variance(combined);
        return combinedVariance > 0 ? Math.Max(0, 1 - Variance(residualValues) / combinedVariance) : 0;
    }

    private static double Variance(double[] values)
    {
        var mean = values.Average();
        return values.Sum(value => Math.Pow(value - mean, 2)) / values.Length;
    }
}
