using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Tendencia de Mann-Kendall con pendiente de Sen (port de <c>series_explore._mann_kendall</c>).</summary>
internal static class MannKendallAnalyses
{
    public static readonly AnalysisDefinition MannKendall = new(
        "mann-kendall",
        "Tendencia de Mann-Kendall (no paramétrica)",
        "Componentes, tendencia y filtros",
        "Prueba si hay tendencia monótona sin suponer normalidad, con la pendiente de Sen; admite la versión estacional.",
        [],
        [],
        [
            new(
                "seasonal",
                "Versión estacional (compara dentro de cada posición del ciclo)",
                AnalysisParamKinds.Boolean,
                false,
                [],
                false),
            new(
                "period",
                "Periodo estacional (0 = según la fecha)",
                AnalysisParamKinds.Integer,
                0.0,
                [],
                false,
                Minimum: 0,
                Maximum: 400),
            ModelHelpers.Alpha()
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Componentes, tendencia y filtros",
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
        var loaded = SeriesLoader.Load(frame, seriesColumn, timeColumn, input.Pint("period", 0), minimum: 8);
        if (loaded.IsError) return Result.Err<AnalysisResult>(loaded.Error.Code, loaded.Error.Message);
        var series = loaded.Value;
        var alpha = input.Pnum("alpha", 0.05);
        var seasonal = input.Pbool("seasonal");
        if (seasonal && (series.Period < 2 || series.Values.Length < 2 * series.Period))
        {
            return ExtendedHelpers.Invalid("Indique el periodo estacional y al menos 2 ciclos completos.");
        }
        var groups = seasonal
            ? Enumerable.Range(0, series.Values.Length).Select(index => index % series.Period).ToArray()
            : null;
        var (s, variance, z) = Statistics(series.Values, groups);
        var pValue = 2 * (1 - SpecialFunctions.NormalCdf(Math.Abs(z)));
        var t = Enumerable.Range(1, series.Values.Length).Select(value => (double)value).ToArray();
        var (senSlope, senLow, senHigh) = SenSlope(series.Values, t, 1 - alpha);
        var direction = z > 0 ? "creciente" : z < 0 ? "decreciente" : "sin dirección";
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("S de Mann-Kendall", ModelHelpers.N(s)),
                new TileItem("Z", ModelHelpers.N(z)),
                new TileItem("Valor p", ModelHelpers.P(pValue)),
                new TileItem("Pendiente de Sen", ModelHelpers.N(senSlope))
            ]),
            new AlertSection(
                "Conclusión",
                pValue < alpha ? "warning" : "success",
                pValue < alpha
                    ? $"Hay una tendencia monótona {direction} significativa (p = {pValue.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)})."
                    : $"No hay evidencia de una tendencia monótona (p = {pValue.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)})."),
            new TableSection(
                "Resultado",
                ["S", "Var(S)", "Z", "Valor p", "Pendiente de Sen", $"IC {SigmaText.General(100 * (1 - alpha))} % inf.", "IC sup."],
                [[
                    ModelHelpers.N(s), ModelHelpers.N(variance), ModelHelpers.N(z), ModelHelpers.P(pValue),
                    ModelHelpers.N(senSlope), ModelHelpers.N(senLow), ModelHelpers.N(senHigh)
                ]],
                Note: "Prueba no paramétrica: no supone normalidad. La pendiente de Sen es la mediana de las pendientes entre todos los pares y resiste atípicos. Supone observaciones independientes (sin autocorrelación fuerte).")
        };

        var labels = series.Labels;
        var line = series.Values.Select((value, index) => value).ToArray();
        var fitted = t.Select(value => senSlope * value + (Median(series.Values) - senSlope * Median(t))).ToArray();
        return Result.Ok(new AnalysisResult(
            "mann-kendall",
            $"Tendencia de Mann-Kendall{(seasonal ? " estacional" : string.Empty)}: {seriesColumn}",
            sections,
            Subtitle: $"n = {series.Values.Length}" + (seasonal ? $" · periodo {series.Period}" : string.Empty),
            Charts:
            [
                new SeriesChart(
                    "Serie y pendiente de Sen",
                    [new XYSeries("Datos", t, line), new XYSeries("Pendiente de Sen", t, fitted)],
                    "Tiempo",
                    seriesColumn)
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["s"] = s,
                ["z"] = z,
                ["p"] = pValue,
                ["sen_slope"] = senSlope
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static (double S, double Variance, double Z) Statistics(double[] values, int[]? groups)
    {
        var totalS = 0.0;
        var totalVariance = 0.0;
        foreach (var part in groups is null ? [values] : Enumerable.Range(0, groups.Max() + 1).Select(group => values.Where((_, index) => groups[index] == group).ToArray()))
        {
            var n = part.Length;
            var s = 0.0;
            for (var i = 0; i < n - 1; i++)
            {
                for (var j = i + 1; j < n; j++) s += Math.Sign(part[j] - part[i]);
            }
            var ties = part.GroupBy(value => value).Select(group => group.Count()).ToArray();
            var variance = (n * (n - 1) * (2 * n + 5) - ties.Sum(count => count * (count - 1) * (2 * count + 5))) / 18.0;
            totalS += s;
            totalVariance += variance;
        }
        var z = totalS == 0 ? 0 : (totalS - Math.Sign(totalS)) / Math.Sqrt(totalVariance);
        return (totalS, totalVariance, z);
    }

    private static (double Slope, double Low, double High) SenSlope(double[] y, double[] x, double confidence)
    {
        var slopes = new List<double>();
        for (var i = 0; i < y.Length - 1; i++)
        {
            for (var j = i + 1; j < y.Length; j++)
            {
                if (x[j] != x[i]) slopes.Add((y[j] - y[i]) / (x[j] - x[i]));
            }
        }
        var sorted = slopes.OrderBy(value => value).ToArray();
        var slope = Median(sorted);
        var nt = sorted.Length;
        var xTies = x.GroupBy(value => value).Select(group => group.Count()).ToArray();
        var yTies = y.GroupBy(value => value).Select(group => group.Count()).ToArray();
        var sigsq = (y.Length * (y.Length - 1) * (2 * y.Length + 5)
                     - xTies.Sum(count => count * (count - 1) * (2 * count + 5))
                     - yTies.Sum(count => count * (count - 1) * (2 * count + 5))) / 18.0;
        var z = SpecialFunctions.NormalInv((1 - confidence) / 2);
        var sigma = Math.Sqrt(Math.Max(sigsq, 0));
        var ru = Math.Min((int)Math.Round((nt - z * sigma) / 2), nt - 1);
        var rl = Math.Max((int)Math.Round((nt + z * sigma) / 2) - 1, 0);
        return (slope, sorted[rl], sorted[ru]);
    }

    private static double Median(double[] values)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
    }
}
