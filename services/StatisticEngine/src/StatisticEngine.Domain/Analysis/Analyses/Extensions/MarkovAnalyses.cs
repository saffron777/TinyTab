using StatisticEngine.Domain.Analysis;
using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Cambio de régimen de Markov con EM (propiedades; salida no determinista en Sigma).</summary>
internal static class MarkovAnalyses
{
    public static readonly AnalysisDefinition Markov = new(
        "markov",
        "Cambio de régimen de Markov (2 regímenes)",
        "Volatilidad y regímenes",
        "Detecta regímenes ocultos con medias y varianzas propias mediante un modelo de Markov oculto.",
        [],
        [],
        [
            new("regimes", "Número de regímenes", AnalysisParamKinds.Integer, 2.0, [], false, Minimum: 2, Maximum: 4),
            new("order", "Orden autorregresivo (0 = solo media cambiante)", AnalysisParamKinds.Integer, 0.0, [], false, Minimum: 0, Maximum: 5),
            new("switch_variance", "La varianza también cambia de régimen", AnalysisParamKinds.Boolean, true, [], false),
            new("switch_ar", "Los coeficientes AR cambian de régimen", AnalysisParamKinds.Boolean, false, [], false),
            new("restarts", "Reinicios de la optimización", AnalysisParamKinds.Integer, 10.0, [], false, Minimum: 1, Maximum: 50),
            new("seed", "Semilla", AnalysisParamKinds.Integer, 123.0, [], false, Minimum: 0, Maximum: 2147483647)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Volatilidad y regímenes",
        Roles:
        [
            new ColumnRole("series", "Serie", ColumnKinds.Numeric),
            new ColumnRole("time", "Columna de tiempo (opcional)", ColumnKinds.Any, Optional: true)
        ],
        Outputs: [new AnalysisOutputSpec("regimenes", "Régimen de cada periodo")]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        if (input.Pint("order", 0) > 0 || input.Pbool("switch_ar")) return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "Los regímenes con coeficientes AR aún no están disponibles.");
        var frame = input.AsFrame();
        var seriesName = ExtendedHelpers.FirstRole(input, "series", "Serie");
        var timeName = ExtendedHelpers.FirstRole(input, "time", "Columna de tiempo");
        var order = Enumerable.Range(0, frame.RowCount).ToArray();
        if (timeName.Length > 0) Array.Sort(order, (left, right) => frame.Dates(timeName)[left].GetValueOrDefault().CompareTo(frame.Dates(timeName)[right].GetValueOrDefault()));
        var values = order.Select(row => frame.Numeric(seriesName)[row]).Where(value => !double.IsNaN(value)).ToArray();
        if (values.Length < 10) return ExtendedHelpers.Invalid("Se necesitan al menos 10 observaciones.");
        var k = input.Pint("regimes", 2);
        var switchVariance = input.Pbool("switch_variance");
        var (means, variances, transition, probabilities, logLikelihood) = Fit(values, k, switchVariance);
        var labels = Enumerable.Range(0, values.Length).Select(index => probabilities[index].Select((value, regime) => (value, regime)).OrderByDescending(pair => pair.value).First().regime).ToArray();
        var durations = Enumerable.Range(0, k).Select(regime => 1 / Math.Max(1e-9, 1 - transition[regime][regime])).ToArray();
        var counts = Enumerable.Range(0, k).Select(regime => labels.Count(value => value == regime)).ToArray();
        var parameters = 2 * k + (switchVariance ? k : 1);
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Regímenes", JsNumber.ToString(k)),
                new TileItem("Log-verosimilitud", ModelHelpers.N(logLikelihood)),
                new TileItem("AIC", ModelHelpers.N(-2 * logLikelihood + 2 * parameters)),
                new TileItem("BIC", ModelHelpers.N(-2 * logLikelihood + Math.Log(values.Length) * parameters))
            ]),
            new TableSection("Parámetros", ["Parámetro", "Estimación", "Error est.", "z", "Valor p"],
                Enumerable.Range(0, k).SelectMany(regime => new IReadOnlyList<string>[]
                {
                    new[] { $"const[{regime}]", ModelHelpers.N(means[regime]), "", "", "" },
                    new[] { $"sigma2[{regime}]", ModelHelpers.N(variances[regime]), "", "", "" }
                }).ToArray()),
            new TableSection("Matriz de transición (fila: régimen actual, columna: siguiente)", ["De \\ A", .. Enumerable.Range(0, k).Select(regime => $"Régimen {regime + 1}")],
                Enumerable.Range(0, k).Select(regime => (IReadOnlyList<string>)[$"Régimen {regime + 1}", .. Enumerable.Range(0, k).Select(next => ModelHelpers.N(transition[regime][next]))]).ToArray(),
                Note: "Probabilidad de pasar de un régimen a otro de un periodo al siguiente."),
            new TableSection("Regímenes", ["Régimen", "Media (constante)", "Duración esperada (periodos)", "Observaciones asignadas", "% del tiempo"],
                Enumerable.Range(0, k).Select(regime => (IReadOnlyList<string>)[$"Régimen {regime + 1}", ModelHelpers.N(means[regime]), ModelHelpers.N(durations[regime]), JsNumber.ToString(counts[regime]), ModelHelpers.N(100.0 * counts[regime] / values.Length)]).ToArray())
        };
        var datasetColumns = new List<DataColumn>();
        if (timeName.Length > 0) datasetColumns.Add(DataColumn.Text("tiempo", order.Select(row => frame.Texts(timeName)[row]).ToArray()));
        datasetColumns.Add(DataColumn.Numeric(seriesName, values));
        datasetColumns.Add(DataColumn.Numeric("regimen", labels.Select(value => (double)value + 1).ToArray()));
        for (var regime = 0; regime < k; regime++) datasetColumns.Add(DataColumn.Numeric($"prob_regimen_{regime + 1}", probabilities.Select(row => row[regime]).ToArray()));
        return Result.Ok(new AnalysisResult(
            "markov",
            $"Cambio de régimen de Markov ({k} regímenes): {seriesName}",
            sections,
            Subtitle: $"regresión con media cambiante · n = {values.Length}",
            Charts:
            [
                new ScatterChart("Serie y régimen asignado", Enumerable.Range(0, values.Length).Select(index => new ScatterPoint(index + 1, values[index])).ToList(), "Periodo", seriesName),
                new ScatterChart("Probabilidad del régimen 1", Enumerable.Range(0, values.Length).Select(index => new ScatterPoint(index + 1, probabilities[index][0])).ToList(), "Periodo", "Probabilidad")
            ],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
            {
                ["regimenes"] = new DatasetResult("Régimen de cada periodo", datasetColumns)
            },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["llf"] = logLikelihood,
                ["aic"] = -2 * logLikelihood + 2 * parameters,
                ["durations"] = durations,
                ["means"] = means,
                ["transition"] = transition
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static (double[] Means, double[] Variances, double[][] Transition, double[][] Probabilities, double LogLikelihood) Fit(double[] values, int k, bool switchVariance)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        var means = Enumerable.Range(0, k).Select(regime => NumericMath.Quantile(sorted, (regime + 0.5) / k)).ToArray();
        var overallVariance = Math.Pow(NumericMath.Sd(values), 2);
        var variances = Enumerable.Repeat(overallVariance, k).ToArray();
        var transition = Enumerable.Range(0, k).Select(regime => Enumerable.Range(0, k).Select(next => next == regime ? 0.9 : 0.1 / Math.Max(1, k - 1)).ToArray()).ToArray();
        var probabilities = new double[values.Length][];
        var logLikelihood = double.NegativeInfinity;
        for (var iteration = 0; iteration < 1000; iteration++)
        {
            var forward = new double[values.Length][];
            var scales = new double[values.Length];
            forward[0] = Enumerable.Range(0, k).Select(regime => Density(values[0], means[regime], switchVariance ? variances[regime] : variances[0]) / k).ToArray();
            scales[0] = forward[0].Sum();
            if (scales[0] <= 0) break;
            for (var regime = 0; regime < k; regime++) forward[0][regime] /= scales[0];
            for (var index = 1; index < values.Length; index++)
            {
                forward[index] = new double[k];
                for (var regime = 0; regime < k; regime++)
                {
                    for (var previous = 0; previous < k; previous++) forward[index][regime] += forward[index - 1][previous] * transition[previous][regime];
                    forward[index][regime] *= Density(values[index], means[regime], switchVariance ? variances[regime] : variances[0]);
                }
                scales[index] = forward[index].Sum();
                if (scales[index] <= 0) { scales[index] = 1e-300; }
                for (var regime = 0; regime < k; regime++) forward[index][regime] /= scales[index];
            }
            var likelihood = scales.Sum(scale => Math.Log(Math.Max(scale, 1e-300)));
            var backward = new double[values.Length][];
            backward[^1] = Enumerable.Repeat(1.0, k).ToArray();
            for (var index = values.Length - 2; index >= 0; index--)
            {
                backward[index] = new double[k];
                for (var regime = 0; regime < k; regime++)
                {
                    for (var next = 0; next < k; next++) backward[index][regime] += transition[regime][next] * Density(values[index + 1], means[next], switchVariance ? variances[next] : variances[0]) * backward[index + 1][next];
                }
                var sum = backward[index].Sum();
                if (sum > 0) for (var regime = 0; regime < k; regime++) backward[index][regime] /= sum;
            }
            var gamma = new double[values.Length][];
            for (var index = 0; index < values.Length; index++)
            {
                gamma[index] = new double[k];
                var sum = Enumerable.Range(0, k).Sum(regime => forward[index][regime] * backward[index][regime]);
                for (var regime = 0; regime < k; regime++) gamma[index][regime] = sum > 0 ? forward[index][regime] * backward[index][regime] / sum : 1.0 / k;
            }
            var newTransition = new double[k][];
            for (var regime = 0; regime < k; regime++) newTransition[regime] = new double[k];
            for (var index = 0; index < values.Length - 1; index++)
            {
                var denominator = 0.0;
                for (var regime = 0; regime < k; regime++) denominator += gamma[index][regime];
                for (var regime = 0; regime < k; regime++)
                {
                    for (var next = 0; next < k; next++) newTransition[regime][next] += gamma[index][regime] * transition[regime][next] * Density(values[index + 1], means[next], switchVariance ? variances[next] : variances[0]) * backward[index + 1][next];
                }
            }
            for (var regime = 0; regime < k; regime++)
            {
                var rowSum = newTransition[regime].Sum();
                for (var next = 0; next < k; next++) newTransition[regime][next] = rowSum > 0 ? newTransition[regime][next] / rowSum : 1.0 / k;
            }
            var weights = Enumerable.Range(0, k).Select(regime => gamma.Sum(row => row[regime])).ToArray();
            var newMeans = Enumerable.Range(0, k).Select(regime => weights[regime] > 0 ? Enumerable.Range(0, values.Length).Sum(index => gamma[index][regime] * values[index]) / weights[regime] : means[regime]).ToArray();
            var newVariances = Enumerable.Range(0, k).Select(regime => weights[regime] > 0 ? Enumerable.Range(0, values.Length).Sum(index => gamma[index][regime] * Math.Pow(values[index] - newMeans[regime], 2)) / weights[regime] : variances[regime]).ToArray();
            if (!switchVariance)
            {
                var commonVariance = Enumerable.Range(0, k).Sum(regime => weights[regime] * newVariances[regime]) / values.Length;
                newVariances = Enumerable.Repeat(commonVariance, k).ToArray();
            }
            var change = Enumerable.Range(0, k).Max(regime => Math.Abs(newMeans[regime] - means[regime]) + Math.Abs(newVariances[regime] - variances[regime]));
            means = newMeans;
            variances = newVariances.Select(value => Math.Max(value, 1e-8)).ToArray();
            transition = newTransition;
            probabilities = gamma;
            logLikelihood = likelihood;
            if (change < 1e-10) break;
        }
        return (means, variances, transition, probabilities, logLikelihood);
    }

    private static double Density(double value, double mean, double variance) => Math.Exp(-0.5 * Math.Pow(value - mean, 2) / Math.Max(variance, 1e-12)) / Math.Sqrt(2 * Math.PI * Math.Max(variance, 1e-12));
}
