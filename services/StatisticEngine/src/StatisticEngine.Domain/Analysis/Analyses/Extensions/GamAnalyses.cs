using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>
/// Modelo aditivo generalizado (GAM). Port de <c>models_advanced._gam</c>: base de B-splines
/// cúbicos por predictor (nudos en los cuantiles de los datos, como patsy) y ajuste por mínimos
/// cuadrados. Sigma crea <c>GLMGam(y, X, smoother=BSplines(...))</c> con <c>alpha = 0</c>, de modo
/// que el modelo es la proyección de la respuesta sobre <c>[X, base]</c>; el diseño es
/// rank-deficiente (los predictores están en el espacio de sus splines), así que el rango se
/// obtiene con una QR con pivoteo y se usa en el AIC, igual que statsmodels.
/// </summary>
internal static class GamAnalyses
{
    private const int Degree = 3;

    private const int MinimumRows = 12;

    public static readonly AnalysisDefinition Gam = new(
        "regression-gam",
        "Modelo aditivo generalizado (GAM)",
        "Modelos aditivos y reducción",
        "Ajusta la respuesta como suma de funciones suaves (splines) de los predictores, sin imponer linealidad.",
        [],
        [],
        [
            new("splines", "Grados de libertad por spline", AnalysisParamKinds.Integer, 5.0, [], false, Minimum: 3, Maximum: 15)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Modelos aditivos y reducción",
        Roles:
        [
            new ColumnRole("response", "Respuesta (Y)", ColumnKinds.Numeric),
            new ColumnRole("predictors", "Predictores numéricos", ColumnKinds.Numeric, Multiple: true, MaxCount: 30)
        ],
        Outputs: [new AnalysisOutputSpec(ModelHelpers.PredictionsKey, ModelHelpers.PredictionsLabel)],
        NeedsDataFrame: true);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var response = ExtendedHelpers.FirstRole(input, "response", "Respuesta (Y)");
        var predictors = input.RoleNames("predictors", true);
        var responseValues = frame.Numeric(response);
        var columns = predictors.Select(name => frame.Numeric(name)).ToArray();
        var usedRows = Enumerable.Range(0, frame.RowCount)
            .Where(row => !double.IsNaN(responseValues[row]) && columns.All(column => !double.IsNaN(column[row])))
            .ToArray();
        var n = usedRows.Length;
        if (n < MinimumRows) return ExtendedHelpers.Invalid($"Se necesitan al menos {MinimumRows} filas completas (hay {n}).");

        var y = usedRows.Select(row => responseValues[row]).ToArray();
        var k = predictors.Count;
        var splines = Math.Clamp(input.Pint("splines", 5), 3, 15);
        var design = new double[n][];
        for (var row = 0; row < n; row++) design[row] = new double[k * splines];
        for (var column = 0; column < k; column++)
        {
            var values = usedRows.Select(row => columns[column][row]).ToArray();
            var basis = SplineBasis(values, splines);
            for (var row = 0; row < n; row++)
            {
                design[row][column] = values[row];
                for (var j = 1; j < splines; j++) design[row][column + j * k] = basis[row][j - 1];
            }
        }

        var (fitted, rank) = Project(design, y);
        var residuals = new double[n];
        var rss = 0.0;
        var mean = y.Average();
        var sst = 0.0;
        for (var row = 0; row < n; row++)
        {
            residuals[row] = y[row] - fitted[row];
            rss += residuals[row] * residuals[row];
            var delta = y[row] - mean;
            sst += delta * delta;
        }
        if (!(rss > 0)) return ExtendedHelpers.Invalid("El modelo ajusta exactamente los datos: no se puede calcular la verosimilitud.");
        var logLikelihood = -0.5 * n * (Math.Log(2 * Math.PI * rss / n) + 1);
        var aic = -2 * logLikelihood + 2 * rank;
        var r2 = sst > 0 ? 1 - rss / sst : 1;
        var parameters = k * splines;

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("AIC", ModelHelpers.N(aic)),
                new TileItem("R²", ModelHelpers.N(r2)),
                new TileItem("Parámetros", JsNumber.ToString(parameters))
            ]),
            new AlertSection(
                "Aviso",
                "info",
                "El GAM suma funciones suaves (splines) de cada predictor: capta relaciones no lineales sin imponer una forma.")
        };
        var points = Enumerable.Range(0, n).Select(row => new ScatterPoint(y[row], fitted[row])).ToList();
        return Result.Ok(new AnalysisResult(
            "regression-gam",
            "Modelo aditivo generalizado (GAM)",
            sections,
            Subtitle: $"n = {n} · {k} predictor(es) · splines con {splines} grados de libertad",
            Charts: [new ScatterChart("Observado vs ajustado", points, "Observado", "Ajustado")],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
            {
                [ModelHelpers.PredictionsKey] = new DatasetResult(
                    ModelHelpers.PredictionsLabel,
                    [
                        DataColumn.Numeric("observado", y),
                        DataColumn.Numeric("ajustado", fitted),
                        DataColumn.Numeric("residuo", residuals)
                    ])
            },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["aic"] = aic,
                ["r2"] = r2
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    /// <summary>
    /// Base de B-splines cúbicos de patsy/statsmodels: nudos interiores en los cuantiles
    /// (R tipo 7) de la variable, nudos de frontera repetidos «grado + 1» veces y la primera
    /// columna (la constante) descartada. Devuelve <c>splines - 1</c> columnas.
    /// </summary>
    private static double[][] SplineBasis(double[] values, int splines)
    {
        var order = Degree + 1;
        var inner = splines - order;
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        var knots = new List<double>(2 * order + Math.Max(inner, 0));
        for (var copy = 0; copy < order; copy++)
        {
            knots.Add(sorted[0]);
            knots.Add(sorted[^1]);
        }
        for (var index = 1; index <= inner; index++)
        {
            var probability = (double)index / (inner + 1);
            knots.Add(Percentile(sorted, probability));
        }
        knots.Sort();
        var all = knots.ToArray();
        var functions = all.Length - Degree - 1;
        var basis = new double[values.Length][];
        var left = new double[Degree + 1];
        var right = new double[Degree + 1];
        var temp = new double[Degree + 1];
        for (var row = 0; row < values.Length; row++)
        {
            var value = values[row];
            var span = value >= all[^1] ? functions - 1 : Degree;
            if (value < all[^1])
            {
                while (span < functions - 1 && all[span + 1] <= value) span++;
            }
            Array.Clear(temp);
            temp[0] = 1.0;
            for (var j = 1; j <= Degree; j++)
            {
                left[j] = value - all[span + 1 - j];
                right[j] = all[span + j] - value;
                var saved = 0.0;
                for (var r = 0; r < j; r++)
                {
                    var denominator = right[r + 1] + left[j - r];
                    var candidate = denominator != 0 ? temp[r] / denominator : 0.0;
                    temp[r] = saved + right[r + 1] * candidate;
                    saved = left[j - r] * candidate;
                }
                temp[j] = saved;
            }
            basis[row] = new double[splines - 1];
            for (var r = 0; r <= Degree; r++)
            {
                var index = span - Degree + r;
                if (index >= 1 && index < splines) basis[row][index - 1] = temp[r];
            }
        }
        return basis;
    }

    /// <summary>Cuantil con interpolación lineal (método 7 de R, el de <c>np.percentile</c>).</summary>
    private static double Percentile(double[] sorted, double probability)
    {
        var position = (sorted.Length - 1) * probability;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper) return sorted[lower];
        return sorted[lower] + (position - lower) * (sorted[upper] - sorted[lower]);
    }

    /// <summary>
    /// Proyección de <paramref name="y"/> sobre el espacio de columnas de <paramref name="design"/>
    /// con una QR de Householder con pivoteo (el diseño es rank-deficiente por construcción).
    /// Devuelve los valores ajustados y el rango numérico.
    /// </summary>
    private static (double[] Fitted, int Rank) Project(double[][] design, double[] y)
    {
        var n = design.Length;
        var p = design[0].Length;
        var columns = new double[p][];
        for (var column = 0; column < p; column++)
        {
            columns[column] = new double[n];
            for (var row = 0; row < n; row++) columns[column][row] = design[row][column];
        }
        var permutation = Enumerable.Range(0, p).ToArray();
        var reflectors = new List<(double[] Vector, double Beta)>();
        var transformed = (double[])y.Clone();
        var scale = 0.0;
        for (var column = 0; column < p; column++)
        {
            var norm = 0.0;
            for (var row = 0; row < n; row++) norm += columns[column][row] * columns[column][row];
            scale = Math.Max(scale, Math.Sqrt(norm));
        }
        var tolerance = 1e-9 * Math.Max(1.0, scale);
        var rank = 0;
        for (var step = 0; step < Math.Min(n, p); step++)
        {
            var pivot = step;
            var pivotNorm = -1.0;
            for (var column = step; column < p; column++)
            {
                var norm = 0.0;
                for (var row = step; row < n; row++) norm += columns[permutation[column]][row] * columns[permutation[column]][row];
                if (norm > pivotNorm)
                {
                    pivotNorm = norm;
                    pivot = column;
                }
            }
            if (Math.Sqrt(pivotNorm) <= tolerance) break;
            (permutation[step], permutation[pivot]) = (permutation[pivot], permutation[step]);
            var target = columns[permutation[step]];
            var normStep = 0.0;
            for (var row = step; row < n; row++) normStep += target[row] * target[row];
            var alpha = target[step] > 0 ? -Math.Sqrt(normStep) : Math.Sqrt(normStep);
            var vector = new double[n];
            for (var row = step; row < n; row++) vector[row] = target[row];
            vector[step] -= alpha;
            var squares = 0.0;
            for (var row = step; row < n; row++) squares += vector[row] * vector[row];
            var beta = squares > 0 ? 2.0 / squares : 0.0;
            for (var column = step; column < p; column++)
            {
                var item = columns[permutation[column]];
                var dot = 0.0;
                for (var row = step; row < n; row++) dot += vector[row] * item[row];
                dot *= beta;
                for (var row = step; row < n; row++) item[row] -= dot * vector[row];
            }
            var yDot = 0.0;
            for (var row = step; row < n; row++) yDot += vector[row] * transformed[row];
            yDot *= beta;
            for (var row = step; row < n; row++) transformed[row] -= yDot * vector[row];
            reflectors.Add((vector, beta));
            rank++;
        }
        var fitted = new double[n];
        for (var row = 0; row < rank; row++) fitted[row] = transformed[row];
        for (var step = rank - 1; step >= 0; step--)
        {
            var (vector, beta) = reflectors[step];
            var dot = 0.0;
            for (var row = step; row < n; row++) dot += vector[row] * fitted[row];
            dot *= beta;
            for (var row = step; row < n; row++) fitted[row] -= dot * vector[row];
        }
        return (fitted, rank);
    }
}
