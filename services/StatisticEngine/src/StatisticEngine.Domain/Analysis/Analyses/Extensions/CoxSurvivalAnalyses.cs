using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Regresión de Cox con ties de Efron.</summary>
internal static class CoxSurvivalAnalyses
{
    public static readonly AnalysisDefinition Cox = new(
        "cox",
        "Regresión de Cox (riesgos proporcionales)",
        "Supervivencia",
        "Ajusta un modelo de riesgos proporcionales de Cox y reporta las razones de riesgo con intervalos y valores p.",
        [],
        [],
        [ModelHelpers.Alpha()],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Supervivencia",
        Roles:
        [
            new ColumnRole("time", "Tiempo hasta el evento", ColumnKinds.Numeric),
            new ColumnRole("event", "Evento (1 = ocurrió, 0 = censurado)", ColumnKinds.Numeric),
            new ColumnRole("predictors", "Predictores (covariables)", ColumnKinds.Numeric, Multiple: true, MinCount: 1)
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var timeName = ExtendedHelpers.FirstRole(input, "time", "Tiempo hasta el evento");
        var eventName = ExtendedHelpers.FirstRole(input, "event", "Evento (1 = ocurrió, 0 = censurado)");
        var predictors = input.RoleNames("predictors", true);
        var names = new List<string> { timeName, eventName };
        names.AddRange(predictors);
        var complete = frame.CompleteCases(names);
        var n = complete.RowCount;
        var times = complete.Numeric(timeName);
        var events = complete.Numeric(eventName);
        var x = predictors.Select(name => complete.Numeric(name)).ToArray();
        var eventsCount = events.Count(value => value > 0);
        if (eventsCount < 2) return ExtendedHelpers.Invalid("Se necesitan al menos dos eventos.");
        var beta = new double[predictors.Count];
        var logLikelihood = EfronLogLikelihood(times, events, x, beta);
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var gradient = new double[beta.Length];
            var hessian = new double[beta.Length, beta.Length];
            for (var i = 0; i < beta.Length; i++)
            {
                var step = 1e-5;
                var high = (double[])beta.Clone();
                var low = (double[])beta.Clone();
                high[i] += step;
                low[i] -= step;
                gradient[i] = (EfronLogLikelihood(times, events, x, high) - EfronLogLikelihood(times, events, x, low)) / (2 * step);
                for (var j = 0; j < beta.Length; j++)
                {
                    var hh = (double[])high.Clone();
                    var ll = (double[])low.Clone();
                    var hj = (double[])high.Clone();
                    var lj = (double[])low.Clone();
                    hh[j] += step;
                    ll[j] += step;
                    hj[j] -= step;
                    lj[j] -= step;
                    hessian[i, j] = (EfronLogLikelihood(times, events, x, hh) - EfronLogLikelihood(times, events, x, hj) - EfronLogLikelihood(times, events, x, ll) + EfronLogLikelihood(times, events, x, lj)) / (4 * step * step);
                }
            }
            var matrix = new double[beta.Length][];
            for (var i = 0; i < beta.Length; i++)
            {
                matrix[i] = new double[beta.Length];
                for (var j = 0; j < beta.Length; j++) matrix[i][j] = hessian[i, j];
            }
            var inverse = LinearAlgebra.Inverse(matrix);
            if (inverse.IsError) break;
            var delta = new double[beta.Length];
            for (var i = 0; i < beta.Length; i++)
            {
                for (var j = 0; j < beta.Length; j++) delta[i] += inverse.Value[i][j] * gradient[j];
            }
            var next = beta.Select((value, index) => value - delta[index]).ToArray();
            if (EfronLogLikelihood(times, events, x, next) < logLikelihood) break;
            var change = next.Select((value, index) => Math.Abs(value - beta[index])).Max();
            beta = next;
            logLikelihood = EfronLogLikelihood(times, events, x, beta);
            if (change < 1e-10) break;
        }
        var covariance = new double[beta.Length][];
        {
            var hessian = new double[beta.Length, beta.Length];
            for (var i = 0; i < beta.Length; i++)
            {
                for (var j = 0; j < beta.Length; j++)
                {
                    var step = 1e-5;
                    var hh = (double[])beta.Clone();
                    var hj = (double[])beta.Clone();
                    var ll = (double[])beta.Clone();
                    var lj = (double[])beta.Clone();
                    hh[i] += step; hh[j] += step;
                    hj[i] += step; hj[j] -= step;
                    ll[i] -= step; ll[j] += step;
                    lj[i] -= step; lj[j] -= step;
                    hessian[i, j] = (EfronLogLikelihood(times, events, x, hh) - EfronLogLikelihood(times, events, x, hj) - EfronLogLikelihood(times, events, x, ll) + EfronLogLikelihood(times, events, x, lj)) / (4 * step * step);
                }
            }
            var matrix = new double[beta.Length][];
            for (var i = 0; i < beta.Length; i++)
            {
                matrix[i] = new double[beta.Length];
                for (var j = 0; j < beta.Length; j++) matrix[i][j] = -hessian[i, j];
            }
            var inverse = LinearAlgebra.Inverse(matrix);
            covariance = inverse.IsOk ? inverse.Value : matrix.Select(_ => new double[beta.Length]).ToArray();
        }
        var rows = new List<IReadOnlyList<string>>();
        var significant = new List<string>();
        for (var index = 0; index < predictors.Count; index++)
        {
            var se = Math.Sqrt(Math.Max(covariance[index][index], 0));
            var z = se > 0 ? beta[index] / se : 0;
            var pValue = 2 * (1 - SpecialFunctions.NormalCdf(Math.Abs(z)));
            var low = Math.Exp(beta[index] - 1.959963984540054 * se);
            var high = Math.Exp(beta[index] + 1.959963984540054 * se);
            if (pValue < 0.05) significant.Add(predictors[index]);
            rows.Add([
                predictors[index],
                ModelHelpers.N(beta[index]),
                ModelHelpers.N(Math.Exp(beta[index])),
                ModelHelpers.N(low),
                ModelHelpers.N(high),
                ModelHelpers.P(pValue)
            ]);
        }
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("Eventos", JsNumber.ToString(eventsCount)),
                new TileItem("Log-verosimilitud", ModelHelpers.N(logLikelihood))
            ]),
            new TableSection(
                "Razones de riesgo",
                ["Predictor", "Coef. β", "Exp(β)", "IC 95 % inf.", "IC sup.", "Valor p"],
                rows,
                Note: "Exp(β) > 1 aumenta el riesgo (acorta la supervivencia); < 1 lo reduce. Supone riesgos proporcionales."),
            new AlertSection(
                "Conclusión",
                "info",
                significant.Count > 0
                    ? "Predictores significativos: " + string.Join(", ", significant) + "."
                    : "Ningún predictor alcanza significación al 5 %.")
        };
        return Result.Ok(new AnalysisResult(
            "cox",
            "Regresión de Cox (riesgos proporcionales)",
            sections,
            Subtitle: $"n = {n} · {eventsCount} eventos",
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["events"] = eventsCount,
                ["llf"] = logLikelihood,
                ["hazard_ratios"] = beta.Select(Math.Exp).ToArray(),
                ["coefficients"] = beta
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double EfronLogLikelihood(double[] times, double[] events, double[][] x, double[] beta)
    {
        var eta = Enumerable.Range(0, times.Length).Select(row => beta.Select((coefficient, column) => coefficient * x[column][row]).Sum()).ToArray();
        var exponential = eta.Select(Math.Exp).ToArray();
        var total = 0.0;
        foreach (var time in times.Where((value, index) => events[index] > 0).Distinct().OrderBy(value => value))
        {
            var risk = Enumerable.Range(0, times.Length).Where(index => times[index] >= time).ToArray();
            var deaths = Enumerable.Range(0, times.Length).Where(index => times[index] == time && events[index] > 0).ToArray();
            if (deaths.Length == 0) continue;
            var riskSum = risk.Sum(index => exponential[index]);
            var deathSum = deaths.Sum(index => exponential[index]);
            total += deaths.Sum(index => eta[index]);
            for (var l = 0; l < deaths.Length; l++) total -= Math.Log(Math.Max(riskSum - (double)l / deaths.Length * deathSum, 1e-300));
        }
        return total;
    }
}
