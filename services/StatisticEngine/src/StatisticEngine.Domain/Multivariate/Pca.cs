using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Multivariate;

/// <summary>Resultado del ACP (paridad: <c>multivariate.ts:19-27</c>).</summary>
public sealed record PcaResult(
    string[] Labels,
    double[] Eigenvalues,
    double[] Explained,
    double[] Cumulative,
    double[][] Loadings,
    double[][] Scores,
    int N);

/// <summary>
/// Análisis de componentes principales sobre la matriz de correlaciones.
/// Paridad 1:1 con <c>multivariate.ts:7-66</c>.
/// </summary>
public static class Pca
{
    /// <summary>Estandarización por columna (paridad: <c>multivariate.ts:7-17</c>); <c>sd || 1</c>.</summary>
    public static double[][] Standardize(double[][] rows)
    {
        var cols = rows.Length > 0 ? rows[0].Length : 0;
        var output = new double[rows.Length][];
        for (var i = 0; i < rows.Length; i++) output[i] = (double[])rows[i].Clone();

        for (var j = 0; j < cols; j++)
        {
            var column = new double[rows.Length];
            for (var i = 0; i < rows.Length; i++) column[i] = rows[i][j];
            var center = NumericMath.Mean(column);
            var scale = NumericMath.Sd(column);
            if (scale == 0 || double.IsNaN(scale)) scale = 1;
            for (var i = 0; i < output.Length; i++) output[i][j] = (rows[i][j] - center) / scale;
        }
        return output;
    }

    /// <summary>ACP con autovalores/autovectores de la matriz de correlaciones (paridad: <c>multivariate.ts:29-66</c>).</summary>
    public static Result<PcaResult> Analyze(double[][] rows, IReadOnlyList<string> labels)
    {
        var n = rows.Length;
        var p = rows.Length > 0 ? rows[0].Length : 0;
        if (n < 3 || p < 2) return Result<PcaResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos 3 filas y 2 variables.");

        var standardized = Standardize(rows);
        var columns = new double[p][];
        for (var j = 0; j < p; j++)
        {
            columns[j] = new double[n];
            for (var i = 0; i < n; i++) columns[j][i] = standardized[i][j];
        }

        var corr = new double[p][];
        for (var i = 0; i < p; i++)
        {
            corr[i] = new double[p];
            for (var j = 0; j < p; j++)
            {
                corr[i][j] = i == j ? 1 : NumericMath.Correlation(columns[i], columns[j]);
            }
        }

        var eigen = LinearAlgebra.EigenSymmetric(corr);
        var order = Enumerable.Range(0, p).OrderByDescending(index => eigen.Values[index]).ToArray();

        var eigenvalues = new double[p];
        for (var i = 0; i < p; i++) eigenvalues[i] = eigen.Values[order[i]];

        var total = 0.0;
        for (var i = 0; i < p; i++) total += eigenvalues[i];
        // paridad: multivariate.ts:45 — `total || 1` (0 y NaN cuentan como falsy).
        if (total == 0 || double.IsNaN(total)) total = 1;

        var explained = new double[p];
        for (var i = 0; i < p; i++) explained[i] = eigenvalues[i] / total * 100;
        var cumulative = new double[p];
        var running = 0.0;
        for (var i = 0; i < p; i++)
        {
            running += explained[i];
            cumulative[i] = running;
        }

        var loadings = new double[p][];
        for (var i = 0; i < p; i++)
        {
            var component = order[i];
            var scale = Math.Sqrt(JsMath.Max(0, eigen.Values[component]));
            loadings[i] = new double[p];
            for (var j = 0; j < p; j++) loadings[i][j] = eigen.Vectors[j][component] * scale;
        }

        var scores = new double[n][];
        for (var i = 0; i < n; i++)
        {
            var row = standardized[i];
            scores[i] = new double[p];
            for (var k = 0; k < p; k++)
            {
                var component = order[k];
                var score = 0.0;
                for (var j = 0; j < p; j++) score += row[j] * eigen.Vectors[j][component];
                scores[i][k] = score;
            }
        }

        return Result<PcaResult>.Ok(new PcaResult(
            [.. labels],
            eigenvalues,
            explained,
            cumulative,
            loadings,
            scores,
            n));
    }
}
