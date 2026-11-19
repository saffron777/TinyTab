using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Causalidad de Granger entre dos series (port de <c>econ_multi._granger</c>).</summary>
internal static class GrangerAnalyses
{
    public static readonly AnalysisDefinition Granger = new(
        "granger",
        "Causalidad de Granger entre dos series",
        "Sistemas",
        "¿Los valores pasados de X ayudan a predecir Y (y al revés)? Pruebas F y χ² para cada número de rezagos.",
        [],
        [],
        [
            new("max_lag", "Rezagos máximos", AnalysisParamKinds.Integer, 4.0, [], false, Minimum: 1, Maximum: 24),
            new(
                "difference",
                "Usar primeras diferencias",
                AnalysisParamKinds.Boolean,
                true,
                [],
                false,
                Help: "Las series deben ser estacionarias: use diferencias si tienen tendencia."),
            ModelHelpers.Alpha()
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Sistemas de series",
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
            var order = Enumerable.Range(0, complete.RowCount).OrderBy(index => dates[index] ?? DateTime.MaxValue).ToArray();
            x = order.Select(index => x[index]).ToList();
            y = order.Select(index => y[index]).ToList();
        }
        if (input.Pbool("difference", true))
        {
            x = x.Zip(x.Skip(1), (first, second) => second - first).ToList();
            y = y.Zip(y.Skip(1), (first, second) => second - first).ToList();
        }
        var n = x.Count;
        var maxLag = Math.Min(input.Pint("max_lag", 4), (n - 2) / 3);
        if (maxLag < 1) return ExtendedHelpers.Invalid("La serie es demasiado corta para esa cantidad de rezagos.");
        var xValues = x.ToArray();
        var yValues = y.ToArray();
        var results = new List<(string Direction, int Lag, double F, int Df1, int Df2, double PF, double Chi2, double PChi)>();
        var forwardRows = new List<(string Direction, int Lag, double F, int Df1, int Df2, double PF, double Chi2, double PChi)>();
        var backwardRows = new List<(string Direction, int Lag, double F, int Df1, int Df2, double PF, double Chi2, double PChi)>();
        for (var lag = 1; lag <= maxLag; lag++)
        {
            var forward = Test(yValues, xValues, lag);
            if (forward.IsError) return Result.Err<AnalysisResult>(forward.Error.Code, forward.Error.Message);
            var row = ($"{xName} → {yName}", lag, forward.Value.F, lag, forward.Value.Df2, forward.Value.PF, forward.Value.Chi2, forward.Value.PChi);
            results.Add(row);
            forwardRows.Add(row);
        }
        for (var lag = 1; lag <= maxLag; lag++)
        {
            var backward = Test(xValues, yValues, lag);
            if (backward.IsError) return Result.Err<AnalysisResult>(backward.Error.Code, backward.Error.Message);
            var row = ($"{yName} → {xName}", lag, backward.Value.F, lag, backward.Value.Df2, backward.Value.PF, backward.Value.Chi2, backward.Value.PChi);
            results.Add(row);
            backwardRows.Add(row);
        }
        var forwardBest = forwardRows.MinBy(row => row.PF);
        var backwardBest = backwardRows.MinBy(row => row.PF);
        var sections = new List<AnalysisSection>
        {
            new TableSection(
                "Pruebas por rezago",
                ["Hipótesis nula: la primera NO causa a la segunda", "Rezagos", "F", "gl", "Valor p (F)", "χ²", "Valor p (χ²)"],
                results.Select(row => (IReadOnlyList<string>)new[]
                {
                    row.Direction,
                    JsNumber.ToString(row.Lag),
                    ModelHelpers.N(row.F),
                    $"{row.Df1}, {row.Df2}",
                    ModelHelpers.P(row.PF),
                    ModelHelpers.N(row.Chi2),
                    ModelHelpers.P(row.PChi)
                }).ToArray(),
                Note: "H₀: los rezagos de la causa no mejoran la predicción del efecto. La causalidad de Granger es predictiva, no necesariamente causal."),
            new AlertSection(
                "Conclusión",
                forwardBest.PF < alpha ? "warning" : "info",
                forwardBest.PF < alpha
                    ? $"{xName} → {yName}: hay causalidad de Granger (mínimo p = {forwardBest.PF.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)} con {forwardBest.Lag} rezago(s))."
                    : $"{xName} → {yName}: no hay evidencia de que {xName} anticipe a {yName} (mínimo p = {forwardBest.PF.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)})."),
            new AlertSection(
                "Conclusión",
                backwardBest.PF < alpha ? "warning" : "info",
                backwardBest.PF < alpha
                    ? $"{yName} → {xName}: hay causalidad de Granger (mínimo p = {backwardBest.PF.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)} con {backwardBest.Lag} rezago(s))."
                    : $"{yName} → {xName}: no hay evidencia de que {yName} anticipe a {xName} (mínimo p = {backwardBest.PF.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)})."),
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("Rezagos probados", JsNumber.ToString(maxLag)),
                new TileItem($"{xName}->{yName}", $"p = {forwardBest.PF.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}"),
                new TileItem($"{yName}->{xName}", $"p = {backwardBest.PF.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}")
            ])
        };
        return Result.Ok(new AnalysisResult(
            "granger",
            $"Causalidad de Granger: {xName} y {yName}",
            sections,
            Subtitle: $"hasta {maxLag} rezagos · n = {n}" + (input.Pbool("difference", true) ? " · en primeras diferencias" : string.Empty),
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["best"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [$"{xName}->{yName}"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["lag"] = forwardBest.Lag, ["p"] = forwardBest.PF },
                    [$"{yName}->{xName}"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["lag"] = backwardBest.Lag, ["p"] = backwardBest.PF }
                }
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private sealed record TestResult(double F, int Df2, double PF, double Chi2, double PChi);

    private static Result<TestResult> Test(double[] target, double[] cause, int lag)
    {
        var rows = target.Length - lag;
        var restricted = new double[rows][];
        var joint = new double[rows][];
        var response = new double[rows];
        for (var index = 0; index < rows; index++)
        {
            var time = lag + index;
            response[index] = target[time];
            var columns = new List<double> { 1 };
            for (var offset = 1; offset <= lag; offset++) columns.Add(target[time - offset]);
            restricted[index] = [.. columns];
            for (var offset = 1; offset <= lag; offset++) columns.Add(cause[time - offset]);
            joint[index] = [.. columns];
        }
        var restrictedFit = LinearModel.Fit(restricted, response);
        var jointFit = LinearModel.Fit(joint, response);
        if (restrictedFit.IsError) return Result.Err<TestResult>(restrictedFit.Error.Code, restrictedFit.Error.Message);
        if (jointFit.IsError) return Result.Err<TestResult>(jointFit.Error.Code, jointFit.Error.Message);
        var df2 = rows - (1 + 2 * lag);
        if (df2 <= 0) return Result.Err<TestResult>(DomainErrorCode.InsufficientData, "No hay grados de libertad suficientes.");
        var difference = restrictedFit.Value.Sse - jointFit.Value.Sse;
        var f = (difference / lag) / (jointFit.Value.Sse / df2);
        var chi2 = rows * difference / jointFit.Value.Sse;
        return Result.Ok(new TestResult(f, df2, ExtendedStatistics.FSf(f, lag, df2), chi2, ExtendedStatistics.ChiSquareSf(chi2, lag)));
    }
}
