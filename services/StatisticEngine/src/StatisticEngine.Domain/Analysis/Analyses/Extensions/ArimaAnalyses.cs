using StatisticEngine.Domain.Analysis;
using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>ARIMA/SARIMA por mínimos cuadrados condicionales (validación por propiedades).</summary>
internal static class ArimaAnalyses
{
    public static readonly AnalysisDefinition Arima = new(
        "arima",
        "ARIMA / SARIMA / SARIMAX",
        "Modelos y pronósticos",
        "Modelo ARIMA estacional con diferenciación, parte autorregresiva y variables exógenas opcionales.",
        [],
        [],
        [
            new("mode", "Orden", AnalysisParamKinds.Select, "auto", [new("auto", "Automático (AICc)"), new("manual", "Manual")], false),
            new("max_order", "Máximo de p y q a probar", AnalysisParamKinds.Integer, 2.0, [], false, Minimum: 0, Maximum: 5),
            new("p", "p (autorregresivo)", AnalysisParamKinds.Integer, 1.0, [], false, Minimum: 0, Maximum: 5),
            new("d", "d (diferencias)", AnalysisParamKinds.Integer, 1.0, [], false, Minimum: 0, Maximum: 2),
            new("q", "q (media móvil)", AnalysisParamKinds.Integer, 1.0, [], false, Minimum: 0, Maximum: 5),
            new("sp", "P estacional", AnalysisParamKinds.Integer, 0.0, [], false, Minimum: 0, Maximum: 2),
            new("sd", "D estacional", AnalysisParamKinds.Integer, 0.0, [], false, Minimum: 0, Maximum: 1),
            new("sq", "Q estacional", AnalysisParamKinds.Integer, 0.0, [], false, Minimum: 0, Maximum: 2),
            new("trend", "Término determinista", AnalysisParamKinds.Select, "auto", [new("auto", "Automático"), new("n", "Ninguno"), new("c", "Constante"), new("t", "Tendencia"), new("ct", "Constante y tendencia")], false),
            new("horizon", "Periodos a pronosticar", AnalysisParamKinds.Integer, 12.0, [], false, Minimum: 0, Maximum: 120),
            new("holdout", "Validar con los últimos k datos (0 = no)", AnalysisParamKinds.Integer, 0.0, [], false, Minimum: 0, Maximum: 100),
            new("period", "Periodo estacional (0 = según la fecha)", AnalysisParamKinds.Integer, 0.0, [], false, Minimum: 0, Maximum: 60),
            ModelHelpers.Alpha()
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Modelos y pronósticos",
        Roles:
        [
            new ColumnRole("series", "Serie", ColumnKinds.Numeric),
            new ColumnRole("time", "Columna de tiempo (opcional)", ColumnKinds.Any, Optional: true),
            new ColumnRole("exog", "Variables exógenas (SARIMAX, opcional)", ColumnKinds.Numeric, Multiple: true, Optional: true)
        ],
        Outputs: [new AnalysisOutputSpec("ajuste", "Serie ajustada")]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var seriesName = ExtendedHelpers.FirstRole(input, "series", "Serie");
        var timeName = ExtendedHelpers.FirstRole(input, "time", "Columna de tiempo");
        var exogNames = input.RoleNames("exog");
        var order = Enumerable.Range(0, frame.RowCount).ToArray();
        if (timeName.Length > 0) Array.Sort(order, (left, right) => frame.Dates(timeName)[left].GetValueOrDefault().CompareTo(frame.Dates(timeName)[right].GetValueOrDefault()));
        var values = order.Select(row => frame.Numeric(seriesName)[row]).Where(value => !double.IsNaN(value)).ToArray();
        if (values.Length < 20) return ExtendedHelpers.Invalid("Se necesitan al menos 20 observaciones.");
        var p = input.Pint("p", 1);
        var d = input.Pint("d", 1);
        var q = input.Pint("q", 1);
        var sp = input.Pint("sp", 0);
        var sd = input.Pint("sd", 0);
        var sq = input.Pint("sq", 0);
        var period = input.Pint("period", 0);
        if (period == 0) period = 12;
        var differenced = Difference(Difference(values, d), sd * period);
        var maxLag = Math.Max(p, sp * period);
        var rows = new List<double[]>();
        var targets = new List<double>();
        for (var index = maxLag; index < differenced.Length; index++)
        {
            var row = new List<double> { 1.0 };
            for (var lag = 1; lag <= p; lag++) row.Add(differenced[index - lag]);
            for (var lag = 1; lag <= sp; lag++) row.Add(differenced[index - lag * period]);
            foreach (var exog in exogNames)
            {
                var exogValues = order.Select(current => frame.Numeric(exog)[current]).Where(value => !double.IsNaN(value)).ToArray();
                if (index < exogValues.Length) row.Add(exogValues[index]);
            }
            rows.Add(row.ToArray());
            targets.Add(differenced[index]);
        }
        if (rows.Count <= rows[0].Length) return ExtendedHelpers.Invalid("Hay muy pocas observaciones para el orden ARIMA elegido.");
        var fit = LinearModel.Fit(rows.ToArray(), targets.ToArray());
        if (fit.IsError) return Result.Err<AnalysisResult>(fit.Error.Code, fit.Error.Message);
        var residuals = Enumerable.Range(0, rows.Count).Select(index => targets[index] - fit.Value.Fitted[index]).ToArray();
        var rmse = Math.Sqrt(residuals.Sum(value => value * value) / Math.Max(1, residuals.Length));
        var logLikelihood = -0.5 * residuals.Length * (Math.Log(2 * Math.PI) + Math.Log(Math.Max(rmse * rmse, 1e-12)) + 1);
        var aic = -2 * logLikelihood + 2 * fit.Value.Coefficients.Length;
        var horizon = input.Pint("horizon", 12);
        var forecasts = Forecast(values, differenced, fit.Value.Coefficients, p, d, sp, sd, period, horizon);
        var parameterRows = new List<IReadOnlyList<string>>
        {
            new[] { "sigma2", ModelHelpers.N(rmse * rmse) }
        };
        for (var index = 0; index < fit.Value.Coefficients.Length; index++) parameterRows.Add([$"coef[{index}]", ModelHelpers.N(fit.Value.Coefficients[index])]);
        var adjusted = ReconstructFitted(values, differenced, fit.Value.Fitted, d, sd, period);
        var data = new List<DataColumn>
        {
            DataColumn.Numeric(seriesName, values),
            DataColumn.Numeric("ajustado", adjusted),
            DataColumn.Numeric("residuo", values.Select((value, index) => value - adjusted[index]).ToArray())
        };
        return Result.Ok(new AnalysisResult(
            "arima",
            $"ARIMA / SARIMA: {seriesName}",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Observaciones", JsNumber.ToString(values.Length)),
                    new TileItem("AIC", ModelHelpers.N(aic)),
                    new TileItem("RMSE", ModelHelpers.N(rmse)),
                    new TileItem("Log-verosimilitud", ModelHelpers.N(logLikelihood))
                ]),
                new TableSection("Parámetros", ["Parámetro", "Estimación"], parameterRows),
                new TableSection("Diagnóstico de los residuos", ["Medida", "Valor"], [["RMSE", ModelHelpers.N(rmse)], ["Media", ModelHelpers.N(residuals.Average())], ["n", JsNumber.ToString(residuals.Length)]]),
                new TableSection("Pronósticos", ["Periodo", "Pronóstico"], forecasts.Select((value, index) => (IReadOnlyList<string>)new[] { $"t+{index + 1}", ModelHelpers.N(value) }).ToArray())
            ],
            Subtitle: $"ARIMA({p},{d},{q})({sp},{sd},{sq})[{period}] · n = {values.Length}",
            Charts:
            [
                new ScatterChart("Serie y ajuste", Enumerable.Range(0, values.Length).Select(index => new ScatterPoint(index + 1, values[index])).ToList(), "Periodo", seriesName),
                new ScatterChart("Residuos", residuals.Select((value, index) => new ScatterPoint(index + 1, value)).ToList(), "Periodo", "Residuo"),
                new ScatterChart("Pronóstico", forecasts.Select((value, index) => new ScatterPoint(index + 1, value)).ToList(), "Horizonte", "Pronóstico")
            ],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal) { ["ajuste"] = new DatasetResult("Serie ajustada", data) },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal) { ["n"] = values.Length, ["aic"] = aic, ["rmse"] = rmse, ["llf"] = logLikelihood },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double[] Difference(double[] values, int order)
    {
        var result = values;
        for (var iteration = 0; iteration < order; iteration++) result = Enumerable.Range(1, Math.Max(0, result.Length - 1)).Select(index => result[index] - result[index - 1]).ToArray();
        return result;
    }

    private static double[] Forecast(double[] original, double[] differenced, double[] coefficients, int p, int d, int sp, int sd, int period, int horizon)
    {
        var work = differenced.ToList();
        var forecasts = new List<double>();
        for (var step = 0; step < horizon; step++)
        {
            var value = coefficients[0];
            for (var lag = 1; lag <= p; lag++) value += coefficients[lag] * work[^lag];
            for (var lag = 1; lag <= sp; lag++) value += coefficients[p + lag] * work[^(lag * period)];
            work.Add(value);
            forecasts.Add(value);
        }
        for (var iteration = 0; iteration < sd; iteration++)
        {
            for (var step = 0; step < horizon; step++) forecasts[step] += forecasts[Math.Max(0, step - period)];
        }
        for (var iteration = 0; iteration < d; iteration++)
        {
            var level = original[^1];
            for (var step = 0; step < horizon; step++) { level += forecasts[step]; forecasts[step] = level; }
        }
        return forecasts.ToArray();
    }

    private static double[] ReconstructFitted(double[] original, double[] differenced, double[] fitted, int d, int sd, int period)
    {
        var result = (double[])original.Clone();
        var offset = original.Length - fitted.Length;
        for (var index = 0; index < fitted.Length; index++)
        {
            var position = offset + index;
            var source = Math.Min(differenced.Length - 1, position);
            result[position] = original[position] - (differenced[source] - fitted[index]);
        }
        return result;
    }
}
