using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Periodograma y prueba g de Fisher (port de <c>series_explore._periodogram</c>).</summary>
internal static class PeriodogramAnalyses
{
    public static readonly AnalysisDefinition Periodogram = new(
        "periodogram",
        "Periodograma y periodicidades",
        "Exploración",
        "Detecta ciclos ocultos con el espectro de la serie y la prueba g de Fisher.",
        [],
        [],
        [
            new(
                "detrend",
                "Tendencia",
                AnalysisParamKinds.Select,
                "linear",
                [new("linear", "Quitar la tendencia lineal"), new("none", "Solo quitar la media")],
                false),
            new("peaks", "Picos a listar", AnalysisParamKinds.Integer, 5.0, [], false, Minimum: 1, Maximum: 20),
            ModelHelpers.Alpha()
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Exploración",
        Roles:
        [
            new ColumnRole("series", "Serie", ColumnKinds.Numeric),
            new ColumnRole("time", "Columna de tiempo (opcional)", ColumnKinds.DateTime, Optional: true, Help: "Sin ella se usa el orden de las filas.")
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var seriesColumn = ExtendedHelpers.FirstRole(input, "series", "Serie");
        var timeColumn = ExtendedHelpers.FirstRole(input, "time", "Columna de tiempo");
        var loaded = SeriesLoader.Load(frame, seriesColumn, timeColumn, 0, minimum: 16);
        if (loaded.IsError) return Result.Err<AnalysisResult>(loaded.Error.Code, loaded.Error.Message);
        var series = loaded.Value;
        var n = series.Values.Length;
        var y = (double[])series.Values.Clone();
        if (input.Pstr("detrend", "linear") == "linear")
        {
            var t = Enumerable.Range(0, n).Select(value => (double)value).ToArray();
            var fit = LinearModel.Fit(t.Select(value => new[] { 1.0, value }).ToArray(), y);
            if (fit.IsError) return Result.Err<AnalysisResult>(fit.Error.Code, fit.Error.Message);
            for (var index = 0; index < n; index++) y[index] -= fit.Value.Fitted[index];
        }
        else
        {
            var mean = y.Average();
            for (var index = 0; index < n; index++) y[index] -= mean;
        }

        var half = n / 2;
        var frequencies = new double[half];
        var power = new double[half];
        for (var k = 1; k <= half; k++)
        {
            var real = 0.0;
            var imaginary = 0.0;
            for (var t = 0; t < n; t++)
            {
                var angle = 2 * Math.PI * k * t / n;
                real += y[t] * Math.Cos(angle);
                imaginary -= y[t] * Math.Sin(angle);
            }
            var value = (real * real + imaginary * imaginary) / ((double)n * n);
            if (k != half) value *= 2;
            frequencies[k - 1] = k / (double)n;
            power[k - 1] = value;
        }
        if (power.All(value => value <= 0)) return ExtendedHelpers.Invalid("La serie no tiene variación después de quitar la tendencia.");
        var peaks = new List<int>();
        for (var index = 1; index < power.Length - 1; index++)
        {
            if (power[index] > power[index - 1] && power[index] > power[index + 1]) peaks.Add(index);
        }
        if (peaks.Count == 0) peaks.Add(Array.IndexOf(power, power.Max()));
        var count = Math.Min(input.Pint("peaks", 5), peaks.Count);
        var order = peaks.OrderByDescending(index => power[index]).Take(count).ToArray();
        var total = power.Sum();
        var g = power.Max() / total;
        var terms = (int)Math.Floor(1 / g);
        var pValue = 0.0;
        for (var j = 1; j <= terms; j++)
        {
            var term = Math.Pow(-1, j - 1) * Binomial(power.Length, j) * Math.Pow(1 - j * g, power.Length - 1);
            pValue += term;
        }
        pValue = Math.Clamp(pValue, 0, 1);
        var best = order[0];
        var dominantPeriod = 1 / frequencies[best];
        var alpha = input.Pnum("alpha", 0.05);
        var significant = pValue < alpha;

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Periodo dominante", ModelHelpers.N(dominantPeriod)),
                new TileItem("Frecuencia dominante", ModelHelpers.N(frequencies[best])),
                new TileItem("% de la potencia", ModelHelpers.N(100 * power[best] / total)),
                new TileItem("Valor p (Fisher)", ModelHelpers.P(pValue))
            ]),
            new AlertSection(
                significant ? "Conclusión" : "Aviso",
                significant ? "success" : "info",
                significant
                    ? $"Hay una periodicidad significativa de unos {dominantPeriod.ToString("G3", System.Globalization.CultureInfo.InvariantCulture)} periodos (prueba de Fisher p = {pValue.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)})."
                    : $"No hay evidencia de una periodicidad dominante (prueba de Fisher p = {pValue.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}).")
        };
        var tableRows = order.Select((index, position) => (IReadOnlyList<string>)new[]
        {
            JsNumber.ToString(position + 1),
            ModelHelpers.N(frequencies[index]),
            ModelHelpers.N(1 / frequencies[index]),
            ModelHelpers.N(power[index]),
            ModelHelpers.N(100 * power[index] / total)
        }).ToArray();
        sections.Add(new TableSection(
            "Picos más altos",
            ["#", "Frecuencia", "Periodo", "Potencia", "% del total"],
            tableRows,
            Note: "Periodo = 1 / frecuencia, en unidades de observación."));

        var points = frequencies.Select((frequency, index) => new ScatterPoint(frequency, power[index])).ToList();
        return Result.Ok(new AnalysisResult(
            "periodogram",
            $"Periodograma de {seriesColumn}",
            sections,
            Subtitle: $"n = {n} · {(input.Pstr("detrend", "linear") == "none" ? "sin" : "con")} tendencia lineal quitada",
            Charts:
            [
                new ScatterChart("Periodograma", points, "Frecuencia (ciclos por observación)", "Potencia")
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["dominant_period"] = dominantPeriod,
                ["fisher_p"] = pValue,
                ["fisher_g"] = g
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double Binomial(int n, int k)
    {
        var result = 1.0;
        for (var i = 1; i <= k; i++) result = result * (n - k + i) / i;
        return result;
    }
}
