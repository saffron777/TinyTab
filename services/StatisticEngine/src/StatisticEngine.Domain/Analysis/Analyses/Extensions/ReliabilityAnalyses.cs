using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Fiabilidad de una escala: alfa, omega, ítems y dos mitades.</summary>
internal static class ReliabilityAnalyses
{
    public static readonly AnalysisDefinition Reliability = new(
        "reliability",
        "Fiabilidad de una escala (alfa de Cronbach)",
        "Escalas",
        "Alfa, omega, análisis de ítems, dos mitades y qué ítems conviene quitar o invertir.",
        [],
        [],
        [],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Escalas",
        Roles:
        [
            new ColumnRole("vars", "Ítems de la escala (3 o más)", ColumnKinds.Numeric, Multiple: true, MinCount: 3),
            new ColumnRole("reverse", "Ítems redactados al revés (opcional)", ColumnKinds.Numeric, Multiple: true, Optional: true)
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var names = input.RoleNames("vars", true);
        var reverse = input.RoleNames("reverse");
        if (names.Count < 3) return ExtendedHelpers.Invalid("Un análisis de fiabilidad necesita al menos 3 ítems.");
        foreach (var column in reverse)
        {
            if (!names.Contains(column)) return ExtendedHelpers.Invalid($"Las columnas a invertir deben estar entre los ítems: «{column}» no lo está.");
        }
        var complete = frame.CompleteCases(names);
        var n = complete.RowCount;
        var k = names.Count;
        if (n < 10) return ExtendedHelpers.Invalid("Se necesitan al menos 10 filas completas.");
        var items = new double[n][];
        for (var row = 0; row < n; row++)
        {
            items[row] = new double[k];
            for (var column = 0; column < k; column++)
            {
                var values = complete.Numeric(names[column]);
                items[row][column] = reverse.Contains(names[column])
                    ? values.Max() + values.Min() - complete.Numeric(names[column])[row]
                    : complete.Numeric(names[column])[row];
            }
        }
        var alpha = Cronbach(items, k);
        var correlations = CorrelationMatrix(items, k);
        var meanR = correlations.SelectMany((row, i) => row.Where((_, j) => i != j)).Average();
        var standardized = k * meanR / (1 + (k - 1) * meanR);
        var total = Enumerable.Range(0, n).Select(row => items[row].Sum()).ToArray();
        var itemRows = new List<IReadOnlyList<string>>();
        var itemTotal = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var column = 0; column < k; column++)
        {
            var rest = Enumerable.Range(0, n).Select(row => total[row] - items[row][column]).ToArray();
            var corrected = Correlation(items.Select(row => row[column]).ToArray(), rest);
            var values = Enumerable.Range(0, n).Select(row => items[row][column]).ToArray();
            var without = Enumerable.Range(0, n).Select(row => Enumerable.Range(0, k).Where(item => item != column).Select(item => items[row][item]).ToArray()).ToArray();
            var alphaWithout = k > 2 ? Cronbach(without, k - 1) : double.NaN;
            itemRows.Add([
                names[column] + (reverse.Contains(names[column]) ? " (invertido)" : string.Empty),
                ModelHelpers.N(values.Average()),
                ModelHelpers.N(NumericMath.Sd(values)),
                ModelHelpers.N(corrected),
                ModelHelpers.N(alphaWithout)
            ]);
            itemTotal[names[column]] = corrected;
        }
        var loading = MlLoadings(correlations, k);
        var omega = Math.Pow(loading.Sum(), 2) / (Math.Pow(loading.Sum(), 2) + loading.Sum(value => 1 - value * value));
        var odd = Enumerable.Range(0, n).Select(row => Enumerable.Range(0, k).Where(column => column % 2 == 0).Sum(column => items[row][column])).ToArray();
        var even = Enumerable.Range(0, n).Select(row => Enumerable.Range(0, k).Where(column => column % 2 == 1).Sum(column => items[row][column])).ToArray();
        var splitR = Correlation(odd, even);
        var spearmanBrown = 2 * splitR / (1 + splitR);
        var sem = NumericMath.Sd(total) * Math.Sqrt(1 - alpha);

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Alfa de Cronbach", ModelHelpers.N(alpha)),
                new TileItem("Alfa estandarizado", ModelHelpers.N(standardized)),
                new TileItem("Omega de McDonald", ModelHelpers.N(omega)),
                new TileItem("Correlación media entre ítems", ModelHelpers.N(meanR))
            ])
        };
        var quality = alpha >= 0.9 ? "excelente" : alpha >= 0.8 ? "buena" : alpha >= 0.7 ? "aceptable" : alpha >= 0.6 ? "cuestionable" : "pobre";
        sections.Add(new AlertSection(
            "Conclusión",
            alpha >= 0.7 ? "success" : "warning",
            $"Fiabilidad {quality} (alfa = {alpha.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}). Referencia habitual: ≥ 0.7 para investigación, ≥ 0.9 para decisiones individuales."));
        sections.Add(new TableSection(
            "Análisis de los ítems",
            ["Ítem", "Media", "Desv. est.", "Correlación ítem-total corregida", "Alfa si se elimina el ítem"],
            itemRows,
            Note: "Un ítem cuyo «alfa si se elimina» supera al alfa total, o con correlación ítem-total baja (< 0.3), empeora la escala."));
        var better = itemRows.Where((row, index) => double.TryParse(row[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && value > alpha + 0.005).Select(row => row[0]).ToArray();
        if (better.Length > 0) sections.Add(new AlertSection("Aviso", "info", "Quitar estos ítems aumentaría el alfa: " + string.Join(", ", better) + "."));
        var negative = itemRows.Where(row => double.TryParse(row[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && value < 0).Select(row => row[0]).ToArray();
        if (negative.Length > 0)
        {
            sections.Add(new AlertSection("Aviso", "warning", "Correlación ítem-total negativa en " + string.Join(", ", negative) + ": probablemente estén redactados al revés; use «Ítems a invertir»."));
        }
        sections.Add(new TableSection(
            "Otras medidas",
            ["Medida", "Valor"],
            [
                new[] { "Omega total (un factor, máxima verosimilitud)", ModelHelpers.N(omega) },
                new[] { "Dos mitades (impares/pares): correlación", ModelHelpers.N(splitR) },
                new[] { "Spearman-Brown (fiabilidad de la prueba completa)", ModelHelpers.N(spearmanBrown) },
                new[] { "Error estándar de medida", ModelHelpers.N(sem) },
                new[] { "Puntuación media de la escala", ModelHelpers.N(total.Average()) },
                new[] { "Desv. est. de la puntuación total", ModelHelpers.N(NumericMath.Sd(total)) }
            ]));
        var points = Enumerable.Range(0, k).Select(column => new ScatterPoint(column + 1, loading[column])).ToList();
        return Result.Ok(new AnalysisResult(
            "reliability",
            "Fiabilidad de una escala",
            sections,
            Subtitle: $"{k} ítems · n = {n}" + (reverse.Count > 0 ? $" · {reverse.Count} ítem(s) invertido(s)" : string.Empty),
            Charts:
            [
                new ScatterChart("Cargas del factor único (omega)", points, "Ítem", "Carga")
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["alpha"] = alpha,
                ["standardized_alpha"] = standardized,
                ["omega"] = omega,
                ["mean_inter_item"] = meanR,
                ["item_total"] = itemTotal,
                ["split_half"] = spearmanBrown
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double Cronbach(double[][] items, int k)
    {
        var n = items.Length;
        var variances = Enumerable.Range(0, k).Select(column => NumericMath.Sd(items.Select(row => row[column]).ToArray())).Select(sd => sd * sd).Sum();
        var total = items.Select(row => row.Sum()).ToArray();
        var totalVariance = Math.Pow(NumericMath.Sd(total), 2);
        return k / (k - 1.0) * (1 - variances / totalVariance);
    }

    private static double[][] CorrelationMatrix(double[][] items, int k)
    {
        var matrix = new double[k][];
        for (var i = 0; i < k; i++)
        {
            matrix[i] = new double[k];
            for (var j = 0; j < k; j++)
            {
                matrix[i][j] = i == j ? 1 : Correlation(items.Select(row => row[i]).ToArray(), items.Select(row => row[j]).ToArray());
            }
        }
        return matrix;
    }

    private static double Correlation(double[] x, double[] y)
    {
        var meanX = x.Average();
        var meanY = y.Average();
        var numerator = x.Select((value, index) => (value - meanX) * (y[index] - meanY)).Sum();
        var denominator = Math.Sqrt(x.Sum(value => Math.Pow(value - meanX, 2)) * y.Sum(value => Math.Pow(value - meanY, 2)));
        return denominator > 0 ? numerator / denominator : 0;
    }

    private static double[] MlLoadings(double[][] correlation, int k)
    {
        double Objective(double[] loadings)
        {
            if (loadings.Any(value => 1 - value * value <= 1e-8)) return 1e9;
            var sigma = new double[k][];
            for (var i = 0; i < k; i++)
            {
                sigma[i] = new double[k];
                for (var j = 0; j < k; j++) sigma[i][j] = loadings[i] * loadings[j] + (i == j ? 1 - loadings[i] * loadings[i] : 0);
            }
            var inverse = LinearAlgebra.Inverse(sigma);
            if (inverse.IsError) return 1e9;
            var trace = 0.0;
            for (var i = 0; i < k; i++)
            {
                for (var j = 0; j < k; j++) trace += correlation[i][j] * inverse.Value[i][j];
            }
            return Math.Log(Math.Max(LinearAlgebra.Determinant(sigma), 1e-300)) + trace;
        }
        var start = Enumerable.Repeat(0.7, k).ToArray();
        var current = Objective(start);
        var gradient = new double[k];
        for (var iteration = 0; iteration < 500; iteration++)
        {
            for (var i = 0; i < k; i++)
            {
                var step = 1e-6;
                var high = (double[])start.Clone();
                var low = (double[])start.Clone();
                high[i] += step;
                low[i] -= step;
                gradient[i] = (Objective(high) - Objective(low)) / (2 * step);
            }
            var direction = gradient.Select(value => -value).ToArray();
            var slope = gradient.Zip(direction, (a, b) => a * b).Sum();
            var stepSize = 1.0;
            var candidate = new double[k];
            var candidateValue = current;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                for (var i = 0; i < k; i++) candidate[i] = start[i] + stepSize * direction[i];
                candidateValue = Objective(candidate);
                if (candidateValue <= current + 1e-4 * stepSize * slope) break;
                stepSize *= 0.5;
            }
            var difference = candidate.Select((value, index) => Math.Abs(value - start[index])).Max();
            start = candidate;
            current = candidateValue;
            if (difference < 1e-10) break;
        }
        return start;
    }
}
