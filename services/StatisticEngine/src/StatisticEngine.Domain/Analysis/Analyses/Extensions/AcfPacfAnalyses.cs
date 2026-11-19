using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;
using StatisticEngine.Domain.TimeSeries;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>ACF, PACF y Ljung-Box (port de <c>series._acf_pacf</c>).</summary>
internal static class AcfPacfAnalyses
{
    public static readonly AnalysisDefinition AcfPacf = new(
        "acf-pacf",
        "Autocorrelación (ACF, PACF y Ljung-Box)",
        "Exploración",
        "Funciones de autocorrelación simple y parcial con bandas de confianza y prueba de Ljung-Box.",
        [],
        [],
        [
            new("lags", "Rezagos", AnalysisParamKinds.Integer, 24.0, [], false, Minimum: 1, Maximum: 200),
            new AnalysisParamDefinition("alpha", "Nivel de significancia α", AnalysisParamKinds.Number, 0.05, [], false, Minimum: 0.001, Maximum: 0.5),
            new(
                "pacf_method",
                "Método de la PACF",
                AnalysisParamKinds.Select,
                "ywm",
                [
                    new("ywm", "Yule-Walker (ajustado)"),
                    new("ols", "Regresiones MCO"),
                    new("ldb", "Levinson-Durbin")
                ],
                false)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Exploración",
        Roles:
        [
            new ColumnRole("series", "Serie", ColumnKinds.Numeric),
            new ColumnRole("time", "Columna de tiempo (opcional)", ColumnKinds.DateTime, Optional: true, Help: "Si se omite se usa el orden de las filas.")
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        if (input.Pstr("pacf_method", "ywm") != "ywm")
        {
            return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "La PACF por MCO o Levinson-Durbin aún no está disponible; use Yule-Walker.");
        }
        var frame = input.AsFrame();
        var seriesColumn = ExtendedHelpers.FirstRole(input, "series", "Serie");
        var timeColumn = ExtendedHelpers.FirstRole(input, "time", "Columna de tiempo");
        var loaded = SeriesLoader.Load(frame, seriesColumn, timeColumn, 0);
        if (loaded.IsError) return Result.Err<AnalysisResult>(loaded.Error.Code, loaded.Error.Message);
        var series = loaded.Value;
        var n = series.Values.Length;
        var lags = Math.Min(input.Pint("lags", 24), n / 2 - 1);
        if (lags < 1) return ExtendedHelpers.Invalid("La serie es demasiado corta para esa cantidad de rezagos.");
        var alpha = input.Pnum("alpha", 0.05);
        var values = series.Values;
        var mean = values.Average();
        var acov = new double[lags + 1];
        for (var lag = 0; lag <= lags; lag++)
        {
            var sum = 0.0;
            for (var index = lag; index < n; index++) sum += (values[index] - mean) * (values[index - lag] - mean);
            acov[lag] = sum / n;
        }
        var acf = acov.Select(value => value / acov[0]).ToArray();
        var pacf = new double[lags + 1];
        pacf[0] = 1;
        var phi = new double[lags + 1];
        for (var lag = 1; lag <= lags; lag++)
        {
            var numerator = acf[lag];
            for (var index = 1; index < lag; index++) numerator -= phi[index] * acf[lag - index];
            var denominator = 1.0;
            for (var index = 1; index < lag; index++) denominator -= phi[index] * acf[index];
            var value = denominator != 0 ? numerator / denominator : 0;
            var next = (double[])phi.Clone();
            next[lag] = value;
            for (var index = 1; index < lag; index++) next[index] = phi[index] - value * phi[lag - index];
            phi = next;
            pacf[lag] = value;
        }
        var critical = SpecialFunctions.NormalInv(1 - alpha / 2);
        var bands = new double[lags + 1];
        var cumulative = 0.0;
        for (var lag = 1; lag <= lags; lag++)
        {
            var standardError = Math.Sqrt((1 + 2 * cumulative) / n);
            bands[lag] = critical * standardError;
            cumulative += acf[lag] * acf[lag];
        }
        var q = new double[lags + 1];
        var p = new double[lags + 1];
        var running = 0.0;
        for (var lag = 1; lag <= lags; lag++)
        {
            running += acf[lag] * acf[lag] / (n - lag);
            q[lag] = n * (n + 2) * running;
            p[lag] = ExtendedStatistics.ChiSquareSf(q[lag], lag);
        }
        var significant = Enumerable.Range(1, lags).Where(lag => Math.Abs(acf[lag]) > bands[lag]).ToArray();

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("Media", ModelHelpers.N(mean)),
                new TileItem("Desv. estándar", ModelHelpers.N(NumericMath.Sd(values))),
                new TileItem("Rezagos significativos (ACF)", JsNumber.ToString(significant.Length))
            ]),
            new AlertSection(
                "Conclusión",
                p[lags] < alpha ? "warning" : "success",
                p[lags] < alpha
                    ? $"Ljung-Box con {lags} rezagos: Q = {q[lags].ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}, p = {p[lags].ToString("F4", System.Globalization.CultureInfo.InvariantCulture)} → hay autocorrelación en la serie."
                    : $"Ljung-Box con {lags} rezagos: Q = {q[lags].ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}, p = {p[lags].ToString("F4", System.Globalization.CultureInfo.InvariantCulture)} → no hay evidencia de autocorrelación.")
        };
        var tableRows = Enumerable.Range(1, lags).Select(lag => (IReadOnlyList<string>)new[]
        {
            JsNumber.ToString(lag),
            ModelHelpers.N(acf[lag]),
            ModelHelpers.N(pacf[lag]),
            ModelHelpers.N(q[lag]),
            ModelHelpers.P(p[lag])
        }).ToArray();
        sections.Add(new TableSection(
            "Autocorrelaciones",
            ["Rezago", "ACF", "PACF", "Q de Ljung-Box", "Valor p"],
            tableRows,
            Note: "En rojo: autocorrelaciones fuera de las bandas de confianza."));

        var lagPoints = Enumerable.Range(1, lags).Select(lag => new ScatterPoint(lag, acf[lag])).ToList();
        var pacfPoints = Enumerable.Range(1, lags).Select(lag => new ScatterPoint(lag, pacf[lag])).ToList();
        var seriesPoints = Enumerable.Range(1, n).Select(index => new ScatterPoint(index, values[index - 1])).ToList();

        return Result.Ok(new AnalysisResult(
            "acf-pacf",
            $"Autocorrelación de {seriesColumn}",
            sections,
            Subtitle: $"n = {n} · {lags} rezagos · bandas al {SigmaText.General(100 * (1 - alpha))} %",
            Charts:
            [
                new ScatterChart("Función de autocorrelación (ACF)", lagPoints, "Rezago", "Autocorrelación"),
                new ScatterChart("Autocorrelación parcial (PACF)", pacfPoints, "Rezago", "Autocorrelación parcial"),
                new ScatterChart("Serie", seriesPoints, "Observación", seriesColumn)
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["lags"] = lags,
                ["ljung_box_q"] = q[lags],
                ["ljung_box_p"] = p[lags],
                ["significant_lags"] = significant,
                ["acf"] = acf.Skip(1).Take(5).ToArray()
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
