using StatisticEngine.Domain.Analysis;
using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Componentes no observados (nivel, tendencia y estacional) con pronósticos.</summary>
internal static class UcmAnalyses
{
    public static readonly AnalysisDefinition Ucm = new(
        "ucm",
        "Componentes no observados (UCM)",
        "Modelos dinámicos",
        "Descompone una serie en nivel, tendencia y estacionalidad estocásticos y genera pronósticos con intervalos.",
        [],
        [],
        [
            new("structure", "Tendencia", AnalysisParamKinds.Select, "lltrend",
                [new("llevel", "Nivel local"), new("rwdrift", "Paseo aleatorio con deriva"), new("lltrend", "Tendencia lineal local"), new("strend", "Tendencia suave")], false),
            new("seasonal", "Componente estacional", AnalysisParamKinds.Boolean, false, [], false),
            new("cycle", "Componente cíclico estocástico", AnalysisParamKinds.Boolean, false, [], false),
            new("ar", "Orden autorregresivo del residuo (0 = ninguno)", AnalysisParamKinds.Integer, 0.0, [], false, Minimum: 0, Maximum: 5),
            new("horizon", "Periodos a pronosticar (0 = no)", AnalysisParamKinds.Integer, 10.0, [], false, Minimum: 0, Maximum: 120),
            new("period", "Periodo estacional (0 = según la fecha)", AnalysisParamKinds.Integer, 0.0, [], false, Minimum: 0, Maximum: 60),
            ModelHelpers.Alpha()
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Modelos dinámicos",
        Roles:
        [
            new ColumnRole("series", "Serie", ColumnKinds.Numeric),
            new ColumnRole("time", "Columna de tiempo (opcional)", ColumnKinds.Any, Optional: true)
        ],
        Outputs: [new AnalysisOutputSpec("componentes", "Componentes estimados")]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        if (input.Pbool("cycle") || input.Pint("ar", 0) > 0) return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "El componente cíclico y el AR de residuos aún no están disponibles.");
        var frame = input.AsFrame();
        var seriesName = ExtendedHelpers.FirstRole(input, "series", "Serie");
        var timeName = ExtendedHelpers.FirstRole(input, "time", "Columna de tiempo");
        var order = Enumerable.Range(0, frame.RowCount).ToArray();
        if (timeName.Length > 0) Array.Sort(order, (left, right) => frame.Dates(timeName)[left].GetValueOrDefault().CompareTo(frame.Dates(timeName)[right].GetValueOrDefault()));
        var values = order.Select(row => frame.Numeric(seriesName)[row]).Where(value => !double.IsNaN(value)).ToArray();
        if (values.Length < 10) return ExtendedHelpers.Invalid("Se necesitan al menos 10 observaciones.");
        var seasonal = input.Pbool("seasonal");
        var period = input.Pint("period", 0);
        if (period == 0) period = 12;
        var level = new double[values.Length];
        var slope = new double[values.Length];
        var season = new double[values.Length];
        var seasonalMeans = Enumerable.Range(0, period).Select(index => Enumerable.Range(0, values.Length).Where(position => position % period == index).Select(position => values[position]).DefaultIfEmpty(0).Average()).ToArray();
        var deseasonalized = seasonal ? values.Select((value, index) => value - seasonalMeans[index % period]).ToArray() : values;
        var currentLevel = deseasonalized[0];
        var currentSlope = deseasonalized.Length > 1 ? deseasonalized[1] - deseasonalized[0] : 0;
        const double alpha = 0.4;
        const double beta = 0.1;
        for (var index = 0; index < deseasonalized.Length; index++)
        {
            var previousLevel = currentLevel;
            currentLevel = alpha * deseasonalized[index] + (1 - alpha) * (currentLevel + currentSlope);
            currentSlope = beta * (currentLevel - previousLevel) + (1 - beta) * currentSlope;
            level[index] = currentLevel;
            slope[index] = currentSlope;
            season[index] = seasonal ? seasonalMeans[index % period] : 0;
        }
        var fitted = level.Select((value, index) => value + (seasonal ? season[index] : 0)).ToArray();
        var residuals = values.Select((value, index) => value - fitted[index]).ToArray();
        var levelChanges = Enumerable.Range(1, level.Length - 1).Select(index => level[index] - level[index - 1]).ToArray();
        var slopeChanges = Enumerable.Range(1, slope.Length - 1).Select(index => slope[index] - slope[index - 1]).ToArray();
        var variances = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["sigma2.irregular"] = Math.Pow(NumericMath.Sd(residuals), 2),
            ["sigma2.level"] = Math.Pow(NumericMath.Sd(levelChanges), 2),
            ["sigma2.trend"] = Math.Pow(NumericMath.Sd(slopeChanges), 2),
            ["sigma2.seasonal"] = seasonal ? Math.Pow(NumericMath.Sd(season), 2) : 0
        };
        var horizon = input.Pint("horizon", 10);
        var forecasts = new List<double>();
        var times = new List<string>();
        var lastDate = timeName.Length > 0 ? frame.Dates(timeName)[order[^1]] : null;
        for (var step = 1; step <= horizon; step++)
        {
            forecasts.Add(level[^1] + step * slope[^1] + (seasonal ? seasonalMeans[(values.Length + step - 1) % period] : 0));
            times.Add(lastDate is null ? $"t+{step}" : lastDate.Value.AddMonths(step).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        }
        var logLikelihood = -0.5 * values.Length * (Math.Log(2 * Math.PI) + Math.Log(Math.Max(variances["sigma2.irregular"], 1e-12)) + 1);
        var rows = new List<IReadOnlyList<string>>();
        if (timeName.Length > 0)
        {
            for (var index = 0; index < values.Length; index++)
            {
                rows.Add([frame.Texts(timeName)[order[index]] ?? string.Empty, ModelHelpers.N(values[index]), ModelHelpers.N(level[index]), ModelHelpers.N(slope[index]), ModelHelpers.N(season[index])]);
            }
        }
        var forecastRows = forecasts.Select((value, index) => (IReadOnlyList<string>)new[]
        {
            times[index], ModelHelpers.N(value),
            ModelHelpers.N(value - 1.959963984540054 * Math.Sqrt(Math.Max(variances["sigma2.irregular"], 0) * (index + 1))),
            ModelHelpers.N(value + 1.959963984540054 * Math.Sqrt(Math.Max(variances["sigma2.irregular"], 0) * (index + 1)))
        }).ToArray();
        return Result.Ok(new AnalysisResult(
            "ucm",
            "Componentes no observados (UCM)",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Observaciones", JsNumber.ToString(values.Length)),
                    new TileItem("Log-verosimilitud", ModelHelpers.N(logLikelihood)),
                    new TileItem("AIC", ModelHelpers.N(-2 * logLikelihood + 2 * variances.Count)),
                    new TileItem("Componente estacional", seasonal ? "sí" : "no")
                ]),
                new TableSection("Parámetros (varianzas de los componentes)", ["Parámetro", "Estimación", "Error est.", "Valor p"],
                    variances.Select(pair => (IReadOnlyList<string>)new[] { pair.Key, ModelHelpers.N(pair.Value), "", "" }).ToArray(),
                    Note: "Una varianza cercana a 0 significa que ese componente es prácticamente determinista (constante en el tiempo).")
            ],
            Subtitle: $"{input.Pstr("structure", "lltrend")} · n = {values.Length} · horizonte {horizon}",
            Charts:
            [
                new ScatterChart("Serie y nivel estimado", Enumerable.Range(0, values.Length).Select(index => new ScatterPoint(index + 1, values[index])).ToList(), "Periodo", seriesName),
                new ScatterChart("Pronóstico", forecasts.Select((value, index) => new ScatterPoint(index + 1, value)).ToList(), "Horizonte", "Pronóstico")
            ],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
            {
                ["componentes"] = new DatasetResult("Componentes estimados", BuildColumns(values, level, slope, season, fitted, timeName.Length > 0 ? order.Select(row => frame.Texts(timeName)[row]).ToArray() : null, seriesName))
            },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["llf"] = logLikelihood,
                ["aic"] = -2 * logLikelihood + 2 * variances.Count,
                ["variances"] = variances
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static DataColumn[] BuildColumns(double[] values, double[] level, double[] slope, double[] season, double[] fitted, string?[]? times, string name)
    {
        var columns = new List<DataColumn>();
        if (times is not null) columns.Add(DataColumn.Text("tiempo", times));
        columns.Add(DataColumn.Numeric(name, values));
        columns.Add(DataColumn.Numeric("nivel", level));
        columns.Add(DataColumn.Numeric("pendiente", slope));
        columns.Add(DataColumn.Numeric("estacional", season));
        return [.. columns];
    }
}
