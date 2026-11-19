using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Quiebres múltiples de Bai-Perron por programación dinámica (port de <c>econ_models._bai_perron</c>).</summary>
internal static class BaiPerronAnalyses
{
    public static readonly AnalysisDefinition BaiPerron = new(
        "bai-perron",
        "Quiebres múltiples (Bai-Perron)",
        "Cambio estructural",
        "Cuántos cambios estructurales hay y dónde están, por programación dinámica y criterio BIC/LWZ; coeficientes de cada régimen.",
        [],
        [],
        [
            new("max_breaks", "Máximo de quiebres", AnalysisParamKinds.Integer, 3.0, [], false, Minimum: 1, Maximum: 5),
            new("trim", "Tamaño mínimo de un régimen (fracción de n)", AnalysisParamKinds.Number, 0.1, [], false, Minimum: 0.05, Maximum: 0.4),
            new(
                "criterion",
                "Criterio",
                AnalysisParamKinds.Select,
                "bic",
                [new("bic", "BIC"), new("lwz", "LWZ (más conservador)")],
                false)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Cambio estructural",
        Heavy: true,
        Roles:
        [
            new ColumnRole("series", "Serie", ColumnKinds.Numeric),
            new ColumnRole("time", "Columna de tiempo (opcional)", ColumnKinds.DateTime, Optional: true, Help: "Sin ella se usa el orden de las filas."),
            new ColumnRole("regressors", "Regresores (opcional; vacío = cambios en la media)", ColumnKinds.Numeric, Multiple: true, Optional: true)
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        if (input.RoleNames("regressors").Count > 0)
        {
            return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "Los regresores de Bai-Perron aún no están disponibles; use cambios en la media.");
        }
        var frame = input.AsFrame();
        var seriesColumn = ExtendedHelpers.FirstRole(input, "series", "Serie");
        var timeColumn = ExtendedHelpers.FirstRole(input, "time", "Columna de tiempo");
        var loaded = SeriesLoader.Load(frame, seriesColumn, timeColumn, 0, minimum: 30);
        if (loaded.IsError) return Result.Err<AnalysisResult>(loaded.Error.Code, loaded.Error.Message);
        var series = loaded.Value;
        var y = series.Values;
        var n = y.Length;
        var trim = input.Pnum("trim", 0.1);
        var minSize = Math.Max((int)Math.Ceiling(trim * n), 3);
        var maxBreaks = Math.Min(input.Pint("max_breaks", 3), 5);
        if ((maxBreaks + 1) * minSize > n)
        {
            return ExtendedHelpers.Invalid("Con ese recorte no caben tantos regímenes: reduzca el número de quiebres o el recorte.");
        }
        var ssr = new double[n + 1, n + 1];
        var sums = new double[n + 1];
        var squares = new double[n + 1];
        for (var index = 0; index < n; index++)
        {
            sums[index + 1] = sums[index] + y[index];
            squares[index + 1] = squares[index] + y[index] * y[index];
        }
        double Segment(int start, int end)
        {
            var count = end - start;
            var sum = sums[end] - sums[start];
            var total = squares[end] - squares[start];
            return Math.Max(0, total - sum * sum / count);
        }
        for (var start = 0; start < n; start++)
        {
            for (var end = start + minSize; end <= n; end++) ssr[start, end] = Segment(start, end);
        }

        var inf = double.PositiveInfinity;
        var dp = new double[maxBreaks + 2][];
        var choice = new int[maxBreaks + 2][];
        for (var segments = 0; segments <= maxBreaks + 1; segments++)
        {
            dp[segments] = Enumerable.Repeat(inf, n + 1).ToArray();
            choice[segments] = Enumerable.Repeat(-1, n + 1).ToArray();
        }
        for (var end = minSize; end <= n; end++) dp[1][end] = ssr[0, end];
        for (var segments = 2; segments <= maxBreaks + 1; segments++)
        {
            for (var end = segments * minSize; end <= n; end++)
            {
                for (var start = (segments - 1) * minSize; start <= end - minSize; start++)
                {
                    if (!double.IsFinite(dp[segments - 1][start])) continue;
                    var candidate = dp[segments - 1][start] + ssr[start, end];
                    if (candidate < dp[segments][end])
                    {
                        dp[segments][end] = candidate;
                        choice[segments][end] = start;
                    }
                }
            }
        }
        var found = new Dictionary<int, (double Ssr, int[] Breaks)>();
        for (var breaks = 0; breaks <= maxBreaks; breaks++)
        {
            var segments = breaks + 1;
            if (!double.IsFinite(dp[segments][n])) continue;
            var breakPositions = new List<int>();
            var end = n;
            for (var segment = segments; segment > 1; segment--)
            {
                var start = choice[segment][end];
                breakPositions.Add(start);
                end = start;
            }
            breakPositions.Reverse();
            found[breaks] = (dp[segments][n], breakPositions.ToArray());
        }
        var criteria = found.ToDictionary(pair => pair.Key, pair => n * Math.Log(pair.Value.Ssr / n) + ((pair.Key + 1) + pair.Key) * Math.Log(n));
        var lwz = found.ToDictionary(pair => pair.Key, pair => Math.Log(pair.Value.Ssr / (n - (pair.Key + 1))) + 0.299 * ((pair.Key + 1) + pair.Key) * Math.Pow(Math.Log(n), 2.1) / n);
        var criterion = input.Pstr("criterion", "bic");
        var chosen = criterion == "lwz" ? lwz.MinBy(pair => pair.Value).Key : criteria.MinBy(pair => pair.Value).Key;
        var boundaries = new List<int> { 0 };
        boundaries.AddRange(found[chosen].Breaks);
        boundaries.Add(n);
        var regimeRows = new List<IReadOnlyList<string>>();
        var fitted = new double[n];
        for (var regime = 0; regime < boundaries.Count - 1; regime++)
        {
            var start = boundaries[regime];
            var end = boundaries[regime + 1];
            var count = end - start;
            var mean = y[start..end].Average();
            var residual = y[start..end].Sum(value => Math.Pow(value - mean, 2));
            var standardError = Math.Sqrt(residual / Math.Max(count - 1, 1) / count);
            for (var index = start; index < end; index++) fitted[index] = mean;
            regimeRows.Add([
                JsNumber.ToString(regime + 1),
                series.Labels[start],
                series.Labels[end - 1],
                JsNumber.ToString(count),
                $"{mean.ToString("G4", System.Globalization.CultureInfo.InvariantCulture)} ({standardError.ToString("G3", System.Globalization.CultureInfo.InvariantCulture)})"
            ]);
        }
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Quiebres elegidos", JsNumber.ToString(chosen)),
                new TileItem("Regímenes", JsNumber.ToString(chosen + 1)),
                new TileItem("SCR final", ModelHelpers.N(found[chosen].Ssr)),
                new TileItem("Criterio", criterion.ToUpperInvariant())
            ]),
            new TableSection(
                "Modelos con 0 a m quiebres",
                ["Quiebres", "SCR", "BIC", "LWZ", "Elegido"],
                found.Keys.OrderBy(key => key).Select(key => (IReadOnlyList<string>)new[]
                {
                    JsNumber.ToString(key),
                    ModelHelpers.N(found[key].Ssr),
                    ModelHelpers.N(criteria[key]),
                    ModelHelpers.N(lwz[key]),
                    key == chosen ? "●" : string.Empty
                }).ToArray(),
                Note: "La mejor partición de cada tamaño se halla por programación dinámica (óptima); el número de quiebres se elige minimizando el criterio."),
            new TableSection(
                "Regímenes (coeficiente y error estándar)",
                ["Régimen", "Desde", "Hasta", "Observaciones", "Constante (media)"],
                regimeRows)
        };
        if (chosen > 0)
        {
            sections.Add(new AlertSection(
                "Aviso",
                "warning",
                "Fechas de los quiebres (primera observación del nuevo régimen): "
                + string.Join(", ", found[chosen].Breaks.Select(index => series.Labels[index])) + "."));
        }
        else
        {
            sections.Add(new AlertSection("Conclusión", "success", "El criterio no encuentra ningún quiebre: un solo régimen describe bien la serie."));
        }
        var points = Enumerable.Range(1, n).Select(index => new ScatterPoint(index, y[index - 1])).ToList();
        var fits = Enumerable.Range(1, n).Select(index => new ScatterPoint(index, fitted[index - 1])).ToList();
        return Result.Ok(new AnalysisResult(
            "bai-perron",
            $"Bai-Perron: quiebres múltiples en {seriesColumn}",
            sections,
            Subtitle: $"hasta {maxBreaks} quiebre(s) · recorte {SigmaText.General(100 * trim)} % (mínimo {minSize} observaciones por régimen) · n = {n}",
            Charts:
            [
                new SeriesChart(
                    "Serie y ajuste por regímenes",
                    [
                        new XYSeries("Datos", points.Select(point => point.X).ToArray(), y),
                        new XYSeries("Ajuste por régimen", fits.Select(point => point.X).ToArray(), fitted)
                    ],
                    "Tiempo",
                    seriesColumn)
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["breaks"] = found[chosen].Breaks.Select(index => index + 1).ToArray(),
                ["break_labels"] = found[chosen].Breaks.Select(index => series.Labels[index]).ToArray(),
                ["chosen"] = chosen,
                ["ssr"] = found.ToDictionary(pair => pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), pair => (object?)pair.Value.Ssr, StringComparer.Ordinal)
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
