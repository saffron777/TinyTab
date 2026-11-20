using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>
/// Mezcla de gaussianas (clúster probabilístico). Port de <c>multivariado._mixture</c>: modelos de
/// mezcla ajustados por EM con inicialización k-means++ y varias réplicas, número de componentes
/// elegido por BIC. La inicialización replica la secuencia de <c>np.random.RandomState(seed)</c> de
/// scikit-learn para que los criterios de selección coincidan con Sigma.
/// </summary>
internal static class MixtureAnalyses
{
    private const double Regularization = 1e-6;

    private const double ConvergenceTolerance = 1e-3;

    private const int MaxIterations = 300;

    private const int MinimumRows = 30;

    private static readonly (string Value, string Label)[] Covariances =
    [
        ("full", "Completa (cada grupo con su matriz)"),
        ("tied", "Común a todos los grupos"),
        ("diag", "Diagonal"),
        ("spherical", "Esférica")
    ];

    public static readonly AnalysisDefinition Mixture = new(
        "mixture",
        "Mezcla de gaussianas (clúster probabilístico)",
        "Agrupamiento (clúster)",
        "Cada fila recibe una probabilidad de pertenecer a cada grupo; el número de grupos se elige con BIC.",
        [],
        [],
        [
            new("components", "Número de componentes (vacío = el de menor BIC)", AnalysisParamKinds.Integer, null, [], true, Minimum: 1, Maximum: 20),
            new("k_max", "Probar hasta k =", AnalysisParamKinds.Integer, 6.0, [], false, Minimum: 2, Maximum: 15),
            new("covariance", "Forma de la covarianza", AnalysisParamKinds.Select, "full",
                Covariances.Select(item => new AnalysisParamOption(item.Value, item.Label)).ToArray(), false),
            new("restarts", "Inicios aleatorios", AnalysisParamKinds.Integer, 3.0, [], false, Minimum: 1, Maximum: 20),
            new("standardize", "Estandarizar las variables", AnalysisParamKinds.Boolean, true, [], false),
            new("seed", "Semilla", AnalysisParamKinds.Integer, 123.0, [], false, Minimum: 0, Maximum: 2147483647)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Agrupamiento (clúster)",
        Roles: [new ColumnRole("vars", "Variables numéricas (2 o más)", ColumnKinds.Numeric, Multiple: true, MinCount: 2)],
        Outputs: [new AnalysisOutputSpec("grupos", "Grupo y probabilidad de cada fila")],
        Heavy: true,
        NeedsDataFrame: true);

    private sealed record MixtureFit(double[] Weights, double[][] Means, double[][][] Covariances, double LowerBound, double LogLikelihood, bool Converged);

    private sealed record Candidate(int Components, MixtureFit Fit, double Bic, double Aic);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var names = input.RoleNames("vars", true);
        var columns = names.Select(name => frame.Numeric(name)).ToArray();
        var usedRows = Enumerable.Range(0, frame.RowCount)
            .Where(row => columns.All(column => !double.IsNaN(column[row])))
            .ToArray();
        var n = usedRows.Length;
        var d = names.Count;
        if (n < MinimumRows)
        {
            return ExtendedHelpers.Invalid($"Se necesitan al menos {MinimumRows} filas completas en esas columnas (hay {n}).");
        }

        var raw = new double[n][];
        for (var row = 0; row < n; row++)
        {
            raw[row] = new double[d];
            for (var column = 0; column < d; column++) raw[row][column] = columns[column][usedRows[row]];
        }

        var means = new double[d];
        var deviations = new double[d];
        for (var column = 0; column < d; column++)
        {
            var sum = 0.0;
            for (var row = 0; row < n; row++) sum += raw[row][column];
            means[column] = sum / n;
            var squares = 0.0;
            for (var row = 0; row < n; row++)
            {
                var delta = raw[row][column] - means[column];
                squares += delta * delta;
            }
            deviations[column] = Math.Sqrt(squares / (n - 1));
            if (!(deviations[column] > 0))
            {
                return ExtendedHelpers.Invalid($"«{names[column]}» es constante: no aporta información.");
            }
        }

        var standardize = input.Pbool("standardize", true);
        var values = new double[n][];
        for (var row = 0; row < n; row++)
        {
            values[row] = new double[d];
            for (var column = 0; column < d; column++)
            {
                values[row][column] = standardize ? (raw[row][column] - means[column]) / deviations[column] : raw[row][column];
            }
        }

        var covariance = input.Pstr("covariance", "full");
        var seed = (ulong)Math.Max(0, input.Pint("seed", 123));
        var restarts = Math.Max(1, input.Pint("restarts", 3));
        var limit = n / (d + 2);
        var wished = 0;
        if (input.Params.TryGetValue("components", out var rawComponents) && rawComponents is not null)
        {
            var text = rawComponents as string;
            if (text is null || text.Length > 0) wished = input.Pint("components", 0);
        }
        if (wished > limit)
        {
            return ExtendedHelpers.Invalid($"Con {n} filas y {d} variables se pueden ajustar como máximo {limit} componentes.");
        }
        var kMax = Math.Min(Math.Max(input.Pint("k_max", 6), wished > 0 ? wished : 1), limit);
        if (kMax < 1) return ExtendedHelpers.Invalid("Hay muy pocas filas para ajustar una mezcla con esas variables.");

        var candidates = new List<Candidate>(kMax);
        for (var components = 1; components <= kMax; components++)
        {
            var fit = Fit(values, components, restarts, seed, covariance);
            var parameters = ParameterCount(components, d, covariance);
            var logLikelihood = fit.LogLikelihood * n;
            candidates.Add(new Candidate(
                components,
                fit,
                -2 * logLikelihood + parameters * Math.Log(n),
                -2 * logLikelihood + 2 * parameters));
        }
        var chosen = wished > 0
            ? candidates.FirstOrDefault(item => item.Components == wished) ?? candidates[0]
            : candidates.OrderBy(item => item.Bic).First();

        var (logProbabilities, logNormalizers) = LogProbabilities(values, chosen.Fit.Weights, chosen.Fit.Means, chosen.Fit.Covariances, out _);
        var probabilities = new double[n][];
        var labels = new int[n];
        var certainty = new double[n];
        for (var row = 0; row < n; row++)
        {
            probabilities[row] = new double[chosen.Components];
            var sum = 0.0;
            for (var component = 0; component < chosen.Components; component++)
            {
                probabilities[row][component] = Math.Exp(logProbabilities[row][component] - logNormalizers[row]);
                sum += probabilities[row][component];
            }
            var best = 0;
            for (var component = 0; component < chosen.Components; component++)
            {
                probabilities[row][component] /= sum;
                if (probabilities[row][component] > probabilities[row][best]) best = component;
            }
            labels[row] = best;
            certainty[row] = probabilities[row][best];
        }
        var entropy = 0.0;
        for (var row = 0; row < n; row++)
        {
            for (var component = 0; component < chosen.Components; component++)
            {
                var probability = Math.Max(probabilities[row][component], 1e-12);
                entropy += probability * Math.Log(probability);
            }
        }
        entropy = -entropy / n;

        var scale = standardize ? deviations : Enumerable.Repeat(1.0, d).ToArray();
        var centre = standardize ? means : new double[d];
        var scaleLabel = Covariances.First(item => item.Value == covariance).Label.Split(" (")[0].ToLowerInvariant();
        var weightHeaders = new List<string> { "Componente", "Peso" };
        weightHeaders.AddRange(names);
        var weightRows = new List<IReadOnlyList<string>>();
        for (var component = 0; component < chosen.Components; component++)
        {
            var row = new List<string>
            {
                $"Grupo {component + 1}",
                ModelHelpers.N(chosen.Fit.Weights[component])
            };
            for (var column = 0; column < d; column++)
            {
                row.Add(ModelHelpers.N(centre[column] + chosen.Fit.Means[component][column] * scale[column]));
            }
            weightRows.Add(row);
        }
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Componentes", JsNumber.ToString(chosen.Components)),
                new TileItem("BIC", ModelHelpers.N(chosen.Bic)),
                new TileItem("Certeza media de la asignación", ModelHelpers.N(certainty.Average())),
                new TileItem("Convergió", chosen.Fit.Converged ? "sí" : "no")
            ]),
            new TableSection("Selección del número de componentes", ["Componentes", "BIC", "AIC", "Log-verosimilitud", "Elegido"],
                candidates.Select(item => (IReadOnlyList<string>)new[]
                {
                    JsNumber.ToString(item.Components),
                    ModelHelpers.N(item.Bic),
                    ModelHelpers.N(item.Aic),
                    ModelHelpers.N(item.Fit.LogLikelihood * n),
                    item.Components == chosen.Components ? "●" : string.Empty
                }).ToArray(),
                Note: "BIC más bajo = mejor equilibrio entre ajuste y complejidad."),
            new TableSection("Pesos y medias de los componentes", weightHeaders, weightRows)
        };
        AddProfile(sections, raw, names, labels, chosen.Components);
        var charts = new List<ChartSpec>
        {
            new SeriesChart(
                "BIC y AIC según el número de componentes",
                [
                    new XYSeries("BIC", candidates.Select(item => (double)item.Components).ToArray(), candidates.Select(item => item.Bic).ToArray()),
                    new XYSeries("AIC", candidates.Select(item => (double)item.Components).ToArray(), candidates.Select(item => item.Aic).ToArray())
                ],
                "Componentes",
                "Criterio"),
            ClusterChart(values, labels, chosen.Components)
        };

        var datasetColumns = new List<(string Name, double[] Values)>
        {
            ("grupo", labels.Select(label => label + 1.0).ToArray()),
            ("certeza", certainty)
        };
        for (var component = 0; component < chosen.Components; component++)
        {
            var index = component;
            datasetColumns.Add(($"prob_grupo_{component + 1}", Enumerable.Range(0, n).Select(row => probabilities[row][index]).ToArray()));
        }
        var dataset = ResultFrame(frame, usedRows, [.. datasetColumns]);
        return Result.Ok(new AnalysisResult(
            "mixture",
            "Mezcla de gaussianas (agrupamiento probabilístico)",
            sections,
            Subtitle: $"{chosen.Components} componente(s) · covarianza {scaleLabel} · n = {n}",
            Charts: charts,
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal) { ["grupos"] = dataset },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["components"] = chosen.Components,
                ["bic"] = chosen.Bic,
                ["certainty"] = certainty.Average(),
                ["entropy"] = PlainNumber(entropy),
                ["weights"] = chosen.Fit.Weights
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static void AddProfile(List<AnalysisSection> sections, double[][] raw, IReadOnlyList<string> names, int[] labels, int components)
    {
        var n = raw.Length;
        var d = names.Count;
        var overall = new double[d];
        var squares = new double[d];
        for (var column = 0; column < d; column++)
        {
            var sum = 0.0;
            for (var row = 0; row < n; row++) sum += raw[row][column];
            overall[column] = sum / n;
            for (var row = 0; row < n; row++)
            {
                var delta = raw[row][column] - overall[column];
                squares[column] += delta * delta;
            }
        }
        var deviation = squares.Select(value => Math.Sqrt(value / (n - 1))).ToArray();
        var rows = new List<IReadOnlyList<string>>();
        var differences = new List<IReadOnlyList<string>>();
        for (var component = 0; component < components; component++)
        {
            var members = Enumerable.Range(0, n).Where(row => labels[row] == component).ToArray();
            var groupMeans = new double[d];
            for (var column = 0; column < d; column++)
            {
                groupMeans[column] = members.Length == 0 ? double.NaN : members.Average(row => raw[row][column]);
            }
            var profileRow = new List<string>
            {
                $"Grupo {component + 1}",
                JsNumber.ToString(members.Length),
                ModelHelpers.N(100.0 * members.Length / n)
            };
            var differenceRow = new List<string> { $"Grupo {component + 1}" };
            for (var column = 0; column < d; column++)
            {
                profileRow.Add(ModelHelpers.N(groupMeans[column]));
                differenceRow.Add(ModelHelpers.N(deviation[column] > 0 ? (groupMeans[column] - overall[column]) / deviation[column] : 0.0));
            }
            rows.Add(profileRow);
            differences.Add(differenceRow);
        }
        var profileHeaders = new List<string> { "Grupo", "n", "%" };
        profileHeaders.AddRange(names);
        var differenceHeaders = new List<string> { "Grupo" };
        differenceHeaders.AddRange(names);
        sections.Add(new TableSection(
            "Perfil de los grupos asignados (medias)",
            profileHeaders,
            rows,
            Note: "Medias en las unidades originales de cada variable."));
        sections.Add(new TableSection(
            "Diferencia de cada grupo respecto al promedio general (en desviaciones estándar)",
            differenceHeaders,
            differences,
            Note: "En negrita: |diferencia| ≥ 0.5 desviaciones estándar, lo que define el carácter del grupo."));
    }

    private static ChartSpec ClusterChart(double[][] values, int[] labels, int components)
    {
        var n = values.Length;
        var d = values[0].Length;
        var centred = new double[n][];
        var mean = new double[d];
        for (var row = 0; row < n; row++) for (var column = 0; column < d; column++) mean[column] += values[row][column] / n;
        for (var row = 0; row < n; row++)
        {
            centred[row] = new double[d];
            for (var column = 0; column < d; column++) centred[row][column] = values[row][column] - mean[column];
        }
        var covariance = LinearAlgebra.Zeros(d, d);
        for (var row = 0; row < n; row++)
        {
            for (var i = 0; i < d; i++) for (var j = 0; j < d; j++) covariance[i][j] += centred[row][i] * centred[row][j] / n;
        }
        var eigen = LinearAlgebra.EigenSymmetric(covariance);
        var order = Enumerable.Range(0, d).OrderByDescending(index => eigen.Values[index]).ToArray();
        var total = eigen.Values.Sum(value => Math.Max(value, 0));
        var share = order.Take(2).Sum(index => Math.Max(eigen.Values[index], 0));
        var groups = new List<ScatterGroup>();
        for (var component = 0; component < components; component++)
        {
            var points = new List<ScatterPoint>();
            for (var row = 0; row < n; row++)
            {
                if (labels[row] != component) continue;
                var x = order.Length > 0 ? centred[row].Select((value, column) => value * eigen.Vectors[column][order[0]]).Sum() : 0;
                var y = order.Length > 1 ? centred[row].Select((value, column) => value * eigen.Vectors[column][order[1]]).Sum() : 0;
                points.Add(new ScatterPoint(x, y));
            }
            groups.Add(new ScatterGroup($"Grupo {component + 1}", points));
        }
        return new ScatterGroupsChart(
            $"Grupos sobre los dos primeros componentes principales ({(total > 0 ? 100 * share / total : 0):0} % de la varianza)",
            groups,
            "Componente 1",
            "Componente 2");
    }

    private static object PlainNumber(double value) => value == 0 && double.IsNegative(value) ? "-0" : value;

    private static int ParameterCount(int components, int features, string covariance) =>
        covariance switch
        {
            "full" => components * features + components * features * (features + 1) / 2 + components - 1,
            "tied" => features * (features + 1) / 2 + components * features + components - 1,
            "diag" => components * features + components * features + components - 1,
            _ => components + components * features + components - 1
        };

    private static DatasetResult ResultFrame(Frame source, int[] usedRows, params (string Name, double[] Values)[] columns)
    {
        var result = new List<DataColumn>(source.Columns.Count + columns.Length);
        result.AddRange(source.Columns);
        foreach (var (name, values) in columns)
        {
            var series = Enumerable.Repeat(double.NaN, source.RowCount).ToArray();
            for (var index = 0; index < Math.Min(values.Length, usedRows.Length); index++) series[usedRows[index]] = values[index];
            result.Add(DataColumn.Numeric(name, series));
        }
        return new DatasetResult("Grupo y probabilidad de cada fila", result);
    }

    // ------------------------------------------------------------------ mezcla gaussiana (sklearn)

    private static MixtureFit Fit(double[][] x, int components, int restarts, ulong seed, string covariance)
    {
        var random = new NumpyRandomState(seed);
        MixtureFit? best = null;
        var bestLower = double.NegativeInfinity;
        for (var restart = 0; restart < restarts; restart++)
        {
            var labels = components == 1 ? new int[x.Length] : KMeansLabels(x, components, random);
            var fit = FitEm(x, components, labels, covariance);
            if (fit.LowerBound > bestLower)
            {
                bestLower = fit.LowerBound;
                best = fit;
            }
        }
        return best!;
    }

    /// <summary>k-means++ de scikit-learn (n_local_trials = 2 + ⌊ln k⌋) seguido de Lloyd.</summary>
    private static int[] KMeansLabels(double[][] x, int k, NumpyRandomState random)
    {
        var n = x.Length;
        var localTrials = 2 + (int)Math.Log(k);
        var centres = new double[k][];
        centres[0] = (double[])x[Math.Min(n - 1, (int)(random.RandomSample() * n))].Clone();
        var closest = SquaredDistances(x, centres[0]);
        var potential = closest.Sum();
        for (var component = 1; component < k; component++)
        {
            var draws = random.Uniform(localTrials);
            var candidates = new int[localTrials];
            for (var trial = 0; trial < localTrials; trial++)
            {
                var target = draws[trial] * potential;
                var running = 0.0;
                var index = n - 1;
                for (var row = 0; row < n; row++)
                {
                    running += closest[row];
                    if (running >= target)
                    {
                        index = row;
                        break;
                    }
                }
                candidates[trial] = index;
            }
            var distances = new double[localTrials][];
            var potentials = new double[localTrials];
            for (var trial = 0; trial < localTrials; trial++)
            {
                distances[trial] = SquaredDistances(x, x[candidates[trial]]);
                for (var row = 0; row < n; row++) distances[trial][row] = Math.Min(closest[row], distances[trial][row]);
                potentials[trial] = distances[trial].Sum();
            }
            var best = 0;
            for (var trial = 1; trial < localTrials; trial++)
            {
                if (potentials[trial] < potentials[best]) best = trial;
            }
            potential = potentials[best];
            closest = distances[best];
            centres[component] = (double[])x[candidates[best]].Clone();
        }

        var tolerance = MeanVariance(x) * 1e-4;
        var labels = new int[n];
        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            for (var row = 0; row < n; row++)
            {
                var best = 0;
                var bestDistance = double.PositiveInfinity;
                for (var component = 0; component < k; component++)
                {
                    var distance = 0.0;
                    for (var column = 0; column < x[row].Length; column++)
                    {
                        var delta = x[row][column] - centres[component][column];
                        distance += delta * delta;
                    }
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = component;
                    }
                }
                labels[row] = best;
            }
            var updated = new double[k][];
            var counts = new int[k];
            for (var component = 0; component < k; component++)
            {
                updated[component] = new double[x[0].Length];
            }
            for (var row = 0; row < n; row++)
            {
                counts[labels[row]]++;
                for (var column = 0; column < x[row].Length; column++) updated[labels[row]][column] += x[row][column];
            }
            var shift = 0.0;
            for (var component = 0; component < k; component++)
            {
                if (counts[component] == 0)
                {
                    updated[component] = (double[])centres[component].Clone();
                    continue;
                }
                for (var column = 0; column < x[0].Length; column++) updated[component][column] /= counts[component];
                for (var column = 0; column < x[0].Length; column++)
                {
                    var delta = updated[component][column] - centres[component][column];
                    shift += delta * delta;
                }
            }
            centres = updated;
            if (shift <= tolerance) break;
        }
        return labels;
    }

    private static double MeanVariance(double[][] x)
    {
        var n = x.Length;
        var d = x[0].Length;
        var total = 0.0;
        for (var column = 0; column < d; column++)
        {
            var mean = 0.0;
            for (var row = 0; row < n; row++) mean += x[row][column];
            mean /= n;
            var variance = 0.0;
            for (var row = 0; row < n; row++)
            {
                var delta = x[row][column] - mean;
                variance += delta * delta;
            }
            total += variance / n;
        }
        return total / d;
    }

    private static double[] SquaredDistances(double[][] x, double[] centre)
    {
        var distances = new double[x.Length];
        for (var row = 0; row < x.Length; row++)
        {
            var distance = 0.0;
            for (var column = 0; column < centre.Length; column++)
            {
                var delta = x[row][column] - centre[column];
                distance += delta * delta;
            }
            distances[row] = distance;
        }
        return distances;
    }

    private static MixtureFit FitEm(double[][] x, int k, int[] labels, string covariance)
    {
        var n = x.Length;
        var d = x[0].Length;
        var responsibilities = new double[n][];
        for (var row = 0; row < n; row++)
        {
            responsibilities[row] = new double[k];
            responsibilities[row][labels[row]] = 1;
        }
        var (weights, means, covariances) = Estimate(x, responsibilities, covariance);
        var previous = double.NegativeInfinity;
        var lower = double.NegativeInfinity;
        var converged = false;
        for (var iteration = 1; iteration <= MaxIterations; iteration++)
        {
            var (logProbabilities, logNormalizers) = LogProbabilities(x, weights, means, covariances, out _);
            lower = logNormalizers.Average();
            for (var row = 0; row < n; row++)
            {
                responsibilities[row] = new double[k];
                var sum = 0.0;
                for (var component = 0; component < k; component++)
                {
                    responsibilities[row][component] = Math.Exp(logProbabilities[row][component] - logNormalizers[row]);
                    sum += responsibilities[row][component];
                }
                for (var component = 0; component < k; component++) responsibilities[row][component] /= sum;
            }
            (weights, means, covariances) = Estimate(x, responsibilities, covariance);
            if (Math.Abs(lower - previous) < ConvergenceTolerance)
            {
                converged = true;
                break;
            }
            previous = lower;
        }
        var (_, finalNormalizers) = LogProbabilities(x, weights, means, covariances, out _);
        return new MixtureFit(weights, means, covariances, lower, finalNormalizers.Average(), converged);
    }

    private static (double[] Weights, double[][] Means, double[][][] Covariances) Estimate(double[][] x, double[][] responsibilities, string covariance)
    {
        var n = x.Length;
        var d = x[0].Length;
        var k = responsibilities[0].Length;
        var weights = new double[k];
        var means = new double[k][];
        for (var component = 0; component < k; component++)
        {
            var count = 0.0;
            for (var row = 0; row < n; row++) count += responsibilities[row][component];
            weights[component] = count / n;
            means[component] = new double[d];
            for (var column = 0; column < d; column++)
            {
                var sum = 0.0;
                for (var row = 0; row < n; row++) sum += responsibilities[row][component] * x[row][column];
                means[component][column] = sum / count;
            }
        }
        var covariances = new double[k][][];
        for (var component = 0; component < k; component++) covariances[component] = LinearAlgebra.Zeros(d, d);
        switch (covariance)
        {
            case "tied":
            {
                var common = LinearAlgebra.Zeros(d, d);
                for (var component = 0; component < k; component++)
                {
                    for (var row = 0; row < n; row++)
                    {
                        var weight = responsibilities[row][component];
                        for (var i = 0; i < d; i++)
                        {
                            var left = x[row][i] - means[component][i];
                            for (var j = 0; j < d; j++) common[i][j] += weight * left * (x[row][j] - means[component][j]);
                        }
                    }
                }
                for (var i = 0; i < d; i++)
                {
                    for (var j = 0; j < d; j++) common[i][j] /= n;
                    common[i][i] += Regularization;
                }
                for (var component = 0; component < k; component++) covariances[component] = (double[][])common.Clone();
                break;
            }
            case "diag":
            {
                for (var component = 0; component < k; component++)
                {
                    for (var column = 0; column < d; column++)
                    {
                        var sum = 0.0;
                        for (var row = 0; row < n; row++)
                        {
                            var delta = x[row][column] - means[component][column];
                            sum += responsibilities[row][component] * delta * delta;
                        }
                        covariances[component][column][column] = sum / weights[component] / n + Regularization;
                    }
                }
                break;
            }
            case "spherical":
            {
                for (var component = 0; component < k; component++)
                {
                    var sum = 0.0;
                    for (var row = 0; row < n; row++)
                    {
                        var distance = 0.0;
                        for (var column = 0; column < d; column++)
                        {
                            var delta = x[row][column] - means[component][column];
                            distance += delta * delta;
                        }
                        sum += responsibilities[row][component] * distance;
                    }
                    var value = sum / weights[component] / n / d + Regularization;
                    for (var i = 0; i < d; i++) covariances[component][i][i] = value;
                }
                break;
            }
            default:
            {
                for (var component = 0; component < k; component++)
                {
                    var count = weights[component] * n;
                    for (var row = 0; row < n; row++)
                    {
                        var weight = responsibilities[row][component];
                        for (var i = 0; i < d; i++)
                        {
                            var left = x[row][i] - means[component][i];
                            for (var j = 0; j < d; j++) covariances[component][i][j] += weight * left * (x[row][j] - means[component][j]);
                        }
                    }
                    for (var i = 0; i < d; i++)
                    {
                        for (var j = 0; j < d; j++) covariances[component][i][j] /= count;
                        covariances[component][i][i] += Regularization;
                    }
                }
                break;
            }
        }
        return (weights, means, covariances);
    }

    private static (double[][] LogProbabilities, double[] LogNormalizers) LogProbabilities(
        double[][] x,
        double[] weights,
        double[][] means,
        double[][][] covariances,
        out double[] logNormalizers)
    {
        var n = x.Length;
        var d = x[0].Length;
        var k = weights.Length;
        var logProbabilities = new double[n][];
        for (var row = 0; row < n; row++) logProbabilities[row] = new double[k];
        for (var component = 0; component < k; component++)
        {
            var cholesky = Cholesky(covariances[component]);
            var logDet = 0.0;
            for (var i = 0; i < d; i++) logDet += 2 * Math.Log(cholesky[i][i]);
            var logWeight = Math.Log(weights[component]);
            for (var row = 0; row < n; row++)
            {
                var quadratic = 0.0;
                var solved = new double[d];
                for (var i = 0; i < d; i++)
                {
                    var value = x[row][i] - means[component][i];
                    for (var j = 0; j < i; j++) value -= cholesky[i][j] * solved[j];
                    solved[i] = value / cholesky[i][i];
                    quadratic += solved[i] * solved[i];
                }
                logProbabilities[row][component] = -0.5 * (d * Math.Log(2 * Math.PI) + logDet + quadratic) + logWeight;
            }
        }
        logNormalizers = new double[n];
        for (var row = 0; row < n; row++)
        {
            var maximum = double.NegativeInfinity;
            for (var component = 0; component < k; component++) maximum = Math.Max(maximum, logProbabilities[row][component]);
            var sum = 0.0;
            for (var component = 0; component < k; component++) sum += Math.Exp(logProbabilities[row][component] - maximum);
            logNormalizers[row] = maximum + Math.Log(sum);
        }
        return (logProbabilities, logNormalizers);
    }

    private static double[][] Cholesky(double[][] matrix)
    {
        var n = matrix.Length;
        var lower = LinearAlgebra.Zeros(n, n);
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j <= i; j++)
            {
                var sum = matrix[i][j];
                for (var k = 0; k < j; k++) sum -= lower[i][k] * lower[j][k];
                if (i == j)
                {
                    lower[i][i] = Math.Sqrt(Math.Max(sum, 1e-300));
                }
                else
                {
                    lower[i][j] = sum / lower[j][j];
                }
            }
        }
        return lower;
    }
}
