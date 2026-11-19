using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Correlación cruzada (port de <c>series_explore._ccf</c>).</summary>
internal static class CcfAnalyses
{
    public static readonly AnalysisDefinition Ccf = new(
        "ccf",
        "Correlación cruzada",
        "Exploración",
        "Qué serie se adelanta a la otra y por cuántos periodos, con bandas de confianza.",
        [],
        [],
        [
            new("max_lag", "Rezago máximo", AnalysisParamKinds.Integer, 20.0, [], false, Minimum: 1, Maximum: 200),
            new(
                "difference",
                "Usar primeras diferencias",
                AnalysisParamKinds.Boolean,
                false,
                [],
                false,
                Help: "Recomendado con series con tendencia."),
            ModelHelpers.Alpha()
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Exploración",
        Roles:
        [
            new ColumnRole("x", "Serie X", ColumnKinds.Numeric),
            new ColumnRole("y", "Serie Y", ColumnKinds.Numeric),
            new ColumnRole("time", "Columna de tiempo (opcional)", ColumnKinds.DateTime, Optional: true, Help: "Sin ella se usa el orden de las filas.")
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var xName = ExtendedHelpers.FirstRole(input, "x", "Serie X");
        var yName = ExtendedHelpers.FirstRole(input, "y", "Serie Y");
        var timeName = ExtendedHelpers.FirstRole(input, "time", "Columna de tiempo");
        var alpha = input.Pnum("alpha", 0.05);
        var names = new List<string> { xName, yName };
        if (!string.IsNullOrEmpty(timeName)) names.Add(timeName);
        var complete = frame.CompleteCases(names);
        var x = complete.Numeric(xName).ToList();
        var y = complete.Numeric(yName).ToList();
        if (!string.IsNullOrEmpty(timeName))
        {
            var dates = complete.Column(timeName).Dates;
            var order = Enumerable.Range(0, complete.RowCount)
                .OrderBy(index => dates[index] ?? DateTime.MaxValue)
                .ToArray();
            x = order.Select(index => x[index]).ToList();
            y = order.Select(index => y[index]).ToList();
        }
        if (input.Pbool("difference"))
        {
            x = x.Zip(x.Skip(1), (first, second) => second - first).ToList();
            y = y.Zip(y.Skip(1), (first, second) => second - first).ToList();
        }
        var n = x.Count;
        if (n < 15) return ExtendedHelpers.Invalid("Se necesitan al menos 15 observaciones pareadas.");
        var xValues = x.ToArray();
        var yValues = y.ToArray();
        var xMean = xValues.Average();
        var yMean = yValues.Average();
        var xSd = Math.Sqrt(xValues.Sum(value => Math.Pow(value - xMean, 2)) / n);
        var ySd = Math.Sqrt(yValues.Sum(value => Math.Pow(value - yMean, 2)) / n);
        if (xSd == 0 || ySd == 0) return ExtendedHelpers.Invalid("Una de las series es constante.");
        var maxLag = Math.Max(1, Math.Min(input.Pint("max_lag", 20), n / 3));
        var lags = Enumerable.Range(-maxLag, 2 * maxLag + 1).ToArray();
        var correlations = new double[lags.Length];
        var denominator = n * xSd * ySd;
        for (var index = 0; index < lags.Length; index++)
        {
            var lag = lags[index];
            var sum = 0.0;
            if (lag >= 0)
            {
                for (var row = 0; row < n - lag; row++) sum += (xValues[row] - xMean) * (yValues[row + lag] - yMean);
            }
            else
            {
                for (var row = -lag; row < n; row++) sum += (xValues[row] - xMean) * (yValues[row + lag] - yMean);
            }
            correlations[index] = sum / denominator;
        }
        var band = SpecialFunctions.NormalInv(1 - alpha / 2) / Math.Sqrt(n);
        var strongestIndex = 0;
        for (var index = 1; index < correlations.Length; index++)
        {
            if (Math.Abs(correlations[index]) > Math.Abs(correlations[strongestIndex])) strongestIndex = index;
        }
        var strongestLag = lags[strongestIndex];
        var significant = correlations.Count(value => Math.Abs(value) > band);

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Pares", JsNumber.ToString(n)),
                new TileItem("Mayor |correlación|", ModelHelpers.N(correlations[strongestIndex])),
                new TileItem("Rezago de la mayor", JsNumber.ToString(strongestLag)),
                new TileItem("Rezagos significativos", JsNumber.ToString(significant))
            ])
        };
        var meaning = strongestLag == 0
            ? "al mismo tiempo"
            : strongestLag > 0
                ? $"{xName} se adelanta a {yName} por {strongestLag} periodo(s)"
                : $"{xName} va detrás de {yName} por {-strongestLag} periodo(s)";
        sections.Add(new AlertSection("Aviso", "info", $"La asociación más fuerte ocurre {meaning}."));

        var tableRows = lags.Select((lag, index) => (lag, r: correlations[index]))
            .Where(pair => Math.Abs(pair.r) > band)
            .Select(pair => (IReadOnlyList<string>)new[] { JsNumber.ToString(pair.lag), ModelHelpers.N(pair.r) })
            .ToArray();
        sections.Add(new TableSection(
            "Rezagos fuera de las bandas",
            ["Rezago", "Correlación"],
            tableRows,
            Note: "Las series con tendencia o estacionalidad dan correlaciones cruzadas engañosas: pruebe con primeras diferencias."));

        var points = lags.Select((lag, index) => new ScatterPoint(lag, correlations[index])).ToList();
        return Result.Ok(new AnalysisResult(
            "ccf",
            $"Correlación cruzada: {xName} y {yName}",
            sections,
            Subtitle: $"{n} pares" + (input.Pbool("difference") ? " · en primeras diferencias" : string.Empty)
                      + $" · bandas al {SigmaText.General(100 * (1 - alpha))} %",
            Charts:
            [
                new ScatterChart($"Correlación cruzada (positivo: {xName} adelanta a {yName})", points, "Rezago k", $"r({xName}[t], {yName}[t + k])")
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["strongest_lag"] = strongestLag,
                ["strongest_r"] = correlations[strongestIndex]
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
