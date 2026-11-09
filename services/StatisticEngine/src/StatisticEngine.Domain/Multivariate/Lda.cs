using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Multivariate;

/// <summary>Media del discriminante por grupo (paridad: <c>multivariate.ts:253-256</c>).</summary>
public sealed record GroupMean(string Group, double Mean);

/// <summary>Resultado del análisis discriminante lineal (paridad: <c>multivariate.ts:209-215</c>).</summary>
public sealed record LdaResult(
    double[] Coefficients,
    double Constant,
    GroupMean[] GroupMeans,
    double Accuracy,
    int N);

/// <summary>
/// Análisis discriminante lineal para exactamente dos grupos.
/// Paridad 1:1 con <c>multivariate.ts:217-260</c>: covarianza pooled con divisor
/// <c>nA + nB - 2</c> y clasificación por el signo de la puntuación.
/// </summary>
public static class Lda
{
    public static Result<LdaResult> Fit(double[][] rows, IReadOnlyList<string> groups)
    {
        var uniqueGroups = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < groups.Count; i++)
        {
            if (seen.Add(groups[i])) uniqueGroups.Add(groups[i]);
        }
        if (uniqueGroups.Count != 2) return Result<LdaResult>.Err(DomainErrorCode.InvalidInput, "LDA requiere exactamente dos grupos.");

        var groupA = uniqueGroups[0];
        var groupB = uniqueGroups[1];
        var rowsA = new List<int>();
        var rowsB = new List<int>();
        for (var i = 0; i < rows.Length; i++)
        {
            if (string.Equals(groups[i], groupA, StringComparison.Ordinal)) rowsA.Add(i);
            else if (string.Equals(groups[i], groupB, StringComparison.Ordinal)) rowsB.Add(i);
        }
        if (rowsA.Count < 2 || rowsB.Count < 2)
        {
            return Result<LdaResult>.Err(DomainErrorCode.InsufficientData, "Cada grupo necesita al menos 2 observaciones.");
        }

        var dims = rows.Length > 0 ? rows[0].Length : 0;
        var meanA = new double[dims];
        var meanB = new double[dims];
        for (var j = 0; j < dims; j++)
        {
            var sumA = 0.0;
            for (var m = 0; m < rowsA.Count; m++) sumA += rows[rowsA[m]][j];
            meanA[j] = sumA / rowsA.Count;
            var sumB = 0.0;
            for (var m = 0; m < rowsB.Count; m++) sumB += rows[rowsB[m]][j];
            meanB[j] = sumB / rowsB.Count;
        }

        var pooled = new double[dims][];
        for (var i = 0; i < dims; i++) pooled[i] = new double[dims];
        for (var i = 0; i < dims; i++)
        {
            for (var j = 0; j < dims; j++)
            {
                var acc = 0.0;
                for (var m = 0; m < rowsA.Count; m++)
                {
                    var row = rows[rowsA[m]];
                    acc += (row[i] - meanA[i]) * (row[j] - meanA[j]);
                }
                for (var m = 0; m < rowsB.Count; m++)
                {
                    var row = rows[rowsB[m]];
                    acc += (row[i] - meanB[i]) * (row[j] - meanB[j]);
                }
                pooled[i][j] = acc / (rowsA.Count + rowsB.Count - 2);
            }
        }

        var inverse = InvertMatrix(pooled);
        if (inverse is null) return Result<LdaResult>.Err(DomainErrorCode.SingularMatrix, "La matriz de covarianza es singular.");

        var difference = new double[dims];
        for (var i = 0; i < dims; i++) difference[i] = meanA[i] - meanB[i];

        var coefficients = new double[dims];
        for (var i = 0; i < dims; i++)
        {
            var sum = 0.0;
            for (var j = 0; j < dims; j++) sum += inverse[i][j] * difference[j];
            coefficients[i] = sum;
        }

        var constant = 0.0;
        for (var i = 0; i < dims; i++) constant += coefficients[i] * (meanA[i] + meanB[i]);
        constant = -0.5 * constant;

        var correct = 0;
        for (var i = 0; i < rows.Length; i++)
        {
            var score = constant;
            for (var j = 0; j < dims; j++) score += rows[i][j] * coefficients[j];
            var predicted = score >= 0 ? groupA : groupB;
            if (string.Equals(predicted, groups[i], StringComparison.Ordinal)) correct++;
        }

        var meanScoreA = 0.0;
        var meanScoreB = 0.0;
        for (var j = 0; j < dims; j++)
        {
            meanScoreA += coefficients[j] * meanA[j];
            meanScoreB += coefficients[j] * meanB[j];
        }
        meanScoreA += constant;
        meanScoreB += constant;

        return Result<LdaResult>.Ok(new LdaResult(
            coefficients,
            constant,
            [new GroupMean(groupA, meanScoreA), new GroupMean(groupB, meanScoreB)],
            correct / (double)rows.Length,
            rows.Length));
    }

    /// <summary>Inversa por Gauss-Jordan con pivoteo parcial (paridad: <c>multivariate.ts:262-286</c>).</summary>
    private static double[][]? InvertMatrix(double[][] matrix)
    {
        var n = matrix.Length;
        var augmented = new double[n][];
        for (var i = 0; i < n; i++)
        {
            augmented[i] = new double[2 * n];
            for (var j = 0; j < n; j++) augmented[i][j] = matrix[i][j];
            augmented[i][n + i] = 1;
        }

        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var row = col + 1; row < n; row++)
            {
                if (Math.Abs(augmented[row][col]) > Math.Abs(augmented[pivot][col])) pivot = row;
            }
            var pivotValue = augmented[pivot][col];
            if (Math.Abs(pivotValue) < 1e-12) return null;
            if (pivot != col) (augmented[pivot], augmented[col]) = (augmented[col], augmented[pivot]);
            for (var j = 0; j < 2 * n; j++) augmented[col][j] /= pivotValue;
            for (var row = 0; row < n; row++)
            {
                if (row == col) continue;
                var factor = augmented[row][col];
                if (factor == 0) continue;
                for (var j = 0; j < 2 * n; j++) augmented[row][j] -= factor * augmented[col][j];
            }
        }

        var inverse = new double[n][];
        for (var i = 0; i < n; i++)
        {
            inverse[i] = new double[n];
            for (var j = 0; j < n; j++) inverse[i][j] = augmented[i][n + j];
        }
        return inverse;
    }
}
