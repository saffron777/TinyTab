using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Correlación canónica entre dos conjuntos de variables.</summary>
internal static class CcaAnalyses
{
    public static readonly AnalysisDefinition Cca = new(
        "cca",
        "Correlación canónica",
        "Escalas y asociación",
        "Busca las combinaciones lineales de dos conjuntos de variables que maximizan su correlación, con cargas y pruebas de Wilks.",
        [],
        [],
        [],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Escalas y asociación",
        Roles:
        [
            new ColumnRole("x_vars", "Conjunto X", ColumnKinds.Numeric, Multiple: true, MinCount: 2),
            new ColumnRole("y_vars", "Conjunto Y", ColumnKinds.Numeric, Multiple: true, MinCount: 2)
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var xs = input.RoleNames("x_vars", true);
        var ys = input.RoleNames("y_vars", true);
        if (xs.Intersect(ys, StringComparer.Ordinal).Any()) return ExtendedHelpers.Invalid("Una columna no puede estar en los dos conjuntos.");
        var complete = frame.CompleteCases([.. xs, .. ys]);
        var n = complete.RowCount;
        var px = xs.Count;
        var py = ys.Count;
        if (n <= px + py) return ExtendedHelpers.Invalid("Se necesitan más filas que variables.");
        var x = Enumerable.Range(0, px).Select(column => Standardize(complete.Numeric(xs[column]))).ToArray();
        var y = Enumerable.Range(0, py).Select(column => Standardize(complete.Numeric(ys[column]))).ToArray();
        var dataX = Enumerable.Range(0, n).Select(row => Enumerable.Range(0, px).Select(column => x[column][row]).ToArray()).ToArray();
        var dataY = Enumerable.Range(0, n).Select(row => Enumerable.Range(0, py).Select(column => y[column][row]).ToArray()).ToArray();
        var (ux, sx, vx) = ThinSvd(dataX);
        var (uy, sy, vy) = ThinSvd(dataY);
        var cross = Multiply(Transpose(ux), uy);
        var (um, sm, vm) = ThinSvd(cross);
        var q = Math.Min(px, py);
        var correlations = sm.Take(q).ToArray();
        var coefficientsX = new double[q][];
        var coefficientsY = new double[q][];
        for (var pair = 0; pair < q; pair++)
        {
            coefficientsX[pair] = Enumerable.Range(0, px).Select(column => Enumerable.Range(0, px).Sum(d => vx[column][d] / Math.Max(sx[d], 1e-12) * um[d][pair])).ToArray();
            coefficientsY[pair] = Enumerable.Range(0, py).Select(column => Enumerable.Range(0, py).Sum(d => vy[column][d] / Math.Max(sy[d], 1e-12) * vm[d][pair])).ToArray();
        }
        var xScores = Scores(x, coefficientsX, q);
        var yScores = Scores(y, coefficientsY, q);
        var sign = Enumerable.Range(0, q).Select(pair => Math.Sign(Correlation(xScores.Select(row => row[pair]).ToArray(), yScores.Select(row => row[pair]).ToArray()))).Select(value => value == 0 ? 1 : value).ToArray();
        if (q > 1) sign[1] = -sign[1];
        var xLoadings = new double[px][];
        var yLoadings = new double[py][];
        for (var column = 0; column < px; column++) xLoadings[column] = Enumerable.Range(0, q).Select(pair => sign[pair] * Correlation(x[column], xScores.Select(row => row[pair]).ToArray())).ToArray();
        for (var column = 0; column < py; column++) yLoadings[column] = Enumerable.Range(0, q).Select(pair => Correlation(y[column], yScores.Select(row => row[pair]).ToArray())).ToArray();
        if (q > 1) for (var column = 0; column < py; column++) yLoadings[column][1] = -yLoadings[column][1];
        var redundancyX = Enumerable.Range(0, q).Select(pair => xLoadings.Select(row => row[pair] * row[pair]).Average() * correlations[pair] * correlations[pair]).ToArray();
        var redundancyY = Enumerable.Range(0, q).Select(pair => yLoadings.Select(row => row[pair] * row[pair]).Average() * correlations[pair] * correlations[pair]).ToArray();
        var tests = new List<IReadOnlyList<string>>();
        var wilksP = new double[q];
        var product = 1.0;
        for (var pair = q - 1; pair >= 0; pair--)
        {
            product *= 1 - correlations[pair] * correlations[pair];
            var p = py - pair;
            var qq = px - pair;
            var r = (n - py - 1.0) - (p - qq + 1) / 2.0;
            var u = (p * qq - 2) / 4.0;
            var df1 = p * qq;
            var t = p * p + qq * qq - 5 > 0 ? Math.Sqrt((Math.Pow(p * qq, 2) - 4) / (p * p + qq * qq - 5)) : 1;
            var df2 = r * t - 2 * u;
            var lambda = Math.Pow(product, 1 / t);
            var f = (1 - lambda) / lambda * df2 / df1;
            var pValue = ExtendedStatistics.FSf(f, df1, df2);
            wilksP[pair] = pValue;
            tests.Insert(0, [JsNumber.ToString(pair + 1), ModelHelpers.N(correlations[pair]), ModelHelpers.N(correlations[pair] * correlations[pair]), ModelHelpers.N(product), JsNumber.ToString(df1), ModelHelpers.N(df2), ModelHelpers.N(f), ModelHelpers.P(pValue)]);
        }
        return Result.Ok(new AnalysisResult(
            "cca",
            "Correlación canónica",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Pares canónicos", JsNumber.ToString(q)),
                    new TileItem("Primera correlación", ModelHelpers.N(correlations[0])),
                    new TileItem("Redundancia de Y dada X", ModelHelpers.N(100 * redundancyY.Sum())),
                    new TileItem("Redundancia de X dada Y", ModelHelpers.N(100 * redundancyX.Sum()))
                ]),
                new TableSection("Correlaciones canónicas y su significancia", ["Par", "Correlación", "R² (varianza compartida)", "Lambda de Wilks", "gl (num)", "gl (den)", "F", "Valor p"], tests,
                    Note: "Cada prueba contrasta que ese par y todos los siguientes no tengan correlación."),
                new TableSection("Cargas de las variables X (correlación con su variable canónica)", ["Variable", .. Enumerable.Range(0, q).Select(pair => $"U{pair + 1}")],
                    Enumerable.Range(0, px).Select(column => (IReadOnlyList<string>)[xs[column], .. Enumerable.Range(0, q).Select(pair => ModelHelpers.N(xLoadings[column][pair]))]).ToArray()),
                new TableSection("Cargas de las variables Y (correlación con su variable canónica)", ["Variable", .. Enumerable.Range(0, q).Select(pair => $"V{pair + 1}")],
                    Enumerable.Range(0, py).Select(column => (IReadOnlyList<string>)[ys[column], .. Enumerable.Range(0, q).Select(pair => ModelHelpers.N(yLoadings[column][pair]))]).ToArray()),
                new TableSection("Índices de redundancia", ["Par", "Redundancia de Y dada X", "Redundancia de X dada Y"],
                    Enumerable.Range(0, q).Select(pair => (IReadOnlyList<string>)[JsNumber.ToString(pair + 1), ModelHelpers.N(redundancyY[pair]), ModelHelpers.N(redundancyX[pair])]).ToArray(),
                    Note: "Redundancia: proporción de la varianza de un conjunto que explica el otro a través de ese par.")
            ],
            Subtitle: $"{px} variables X y {py} variables Y · n = {n}",
            Charts:
            [
                new ScatterChart($"Primer par canónico (r = {correlations[0]:0.000})", Enumerable.Range(0, n).Select(row => new ScatterPoint(xScores[row][0], yScores[row][0])).ToList(), "U1 (variable canónica de X)", "V1 (variable canónica de Y)"),
                new ScatterChart("Correlaciones canónicas", Enumerable.Range(0, q).Select(pair => new ScatterPoint(pair + 1, correlations[pair])).ToList(), "Par canónico", "Correlación")
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["correlations"] = correlations,
                ["wilks_p"] = wilksP,
                ["redundancy_y"] = redundancyY.Sum()
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double[] Standardize(double[] values)
    {
        var mean = values.Average();
        var sd = NumericMath.Sd(values);
        return values.Select(value => sd > 0 ? (value - mean) / sd : 0).ToArray();
    }

    private static double[][] CorrelationMatrix(double[][] variables)
    {
        var matrix = new double[variables.Length][];
        for (var i = 0; i < variables.Length; i++) matrix[i] = Enumerable.Range(0, variables.Length).Select(j => i == j ? 1 : Correlation(variables[i], variables[j])).ToArray();
        return matrix;
    }

    private static double[][] CrossMatrix(double[][] left, double[][] right)
    {
        var matrix = new double[left.Length][];
        for (var i = 0; i < left.Length; i++) matrix[i] = Enumerable.Range(0, right.Length).Select(j => Correlation(left[i], right[j])).ToArray();
        return matrix;
    }

    private static double[][] Transpose(double[][] matrix)
    {
        var result = new double[matrix[0].Length][];
        for (var i = 0; i < result.Length; i++) result[i] = Enumerable.Range(0, matrix.Length).Select(j => matrix[j][i]).ToArray();
        return result;
    }

    private static double[][] Multiply(double[][] left, double[][] right)
    {
        var result = new double[left.Length][];
        for (var i = 0; i < left.Length; i++)
        {
            result[i] = new double[right[0].Length];
            for (var j = 0; j < right[0].Length; j++) result[i][j] = Enumerable.Range(0, right.Length).Sum(k => left[i][k] * right[k][j]);
        }
        return result;
    }

    private static double[] Multiply(double[][] matrix, double[] vector) => Enumerable.Range(0, matrix.Length).Select(i => Dot(matrix[i], vector)).ToArray();

    private static double Dot(double[] left, double[] right) => left.Select((value, index) => value * right[index]).Sum();

    private static double[][] Inverse(double[][] matrix) => LinearAlgebra.Inverse(matrix).Value;

    private static double[][] InverseSqrt(double[][] matrix)
    {
        var eigen = LinearAlgebra.EigenSymmetric(matrix);
        var result = new double[matrix.Length][];
        for (var i = 0; i < matrix.Length; i++) result[i] = new double[matrix.Length];
        for (var value = 0; value < eigen.Values.Length; value++)
        {
            var scale = 1 / Math.Sqrt(Math.Max(eigen.Values[value], 1e-12));
            for (var i = 0; i < matrix.Length; i++)
            {
                for (var j = 0; j < matrix.Length; j++) result[i][j] += eigen.Vectors[i][value] * scale * eigen.Vectors[j][value];
            }
        }
        return result;
    }

    private static double[][] Scores(double[][] variables, double[][] coefficients, int pairs)
    {
        var result = new double[variables[0].Length][];
        for (var row = 0; row < variables[0].Length; row++)
        {
            result[row] = Enumerable.Range(0, pairs).Select(pair => Enumerable.Range(0, variables.Length).Sum(column => variables[column][row] * coefficients[pair][column])).ToArray();
        }
        return result;
    }

    private static (double[][] U, double[] S, double[][] V) ThinSvd(double[][] matrix)
    {
        var rows = matrix.Length;
        var cols = matrix[0].Length;
        var gram = new double[cols][];
        for (var i = 0; i < cols; i++)
        {
            gram[i] = new double[cols];
            for (var j = 0; j < cols; j++) gram[i][j] = Enumerable.Range(0, rows).Sum(k => matrix[k][i] * matrix[k][j]);
        }
        var eigen = LinearAlgebra.EigenSymmetric(gram);
        var order = Enumerable.Range(0, cols).OrderByDescending(index => eigen.Values[index]).Take(Math.Min(rows, cols)).ToArray();
        var v = new double[cols][];
        for (var i = 0; i < cols; i++) v[i] = new double[order.Length];
        var s = new double[order.Length];
        for (var d = 0; d < order.Length; d++)
        {
            s[d] = Math.Sqrt(Math.Max(eigen.Values[order[d]], 0));
            for (var i = 0; i < cols; i++) v[i][d] = eigen.Vectors[i][order[d]];
        }
        var u = new double[rows][];
        for (var i = 0; i < rows; i++)
        {
            u[i] = new double[order.Length];
            for (var d = 0; d < order.Length; d++) u[i][d] = s[d] > 0 ? Enumerable.Range(0, cols).Sum(j => matrix[i][j] * v[j][d]) / s[d] : 0;
        }
        return (u, s, v);
    }

    private static double Correlation(double[] x, double[] y)
    {
        var meanX = x.Average();
        var meanY = y.Average();
        var numerator = x.Select((value, index) => (value - meanX) * (y[index] - meanY)).Sum();
        var denominator = Math.Sqrt(x.Sum(value => Math.Pow(value - meanX, 2)) * y.Sum(value => Math.Pow(value - meanY, 2)));
        return denominator > 0 ? numerator / denominator : 0;
    }
}
