using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Models;

/// <summary>Mínimos cuadrados parciales (PLS1) con el algoritmo NIPALS de sklearn.</summary>
public sealed record PlsFit(
    double[] Coefficients,
    double[] Fitted,
    double[] XScoreVariances,
    double[] YScoreVariances,
    double XTotalVariance,
    double YTotalVariance,
    int Components,
    double R2);

public static class PlsRegression
{
    public static Result<PlsFit> Fit(double[][] x, double[] y, int components, bool scale)
    {
        var n = x.Length;
        var p = x[0].Length;
        if (n < 12) return Result.Err<PlsFit>(DomainErrorCode.InsufficientData, "Se necesitan al menos 12 filas completas.");
        var count = Math.Min(components, p);
        if (count < 1) return Result.Err<PlsFit>(DomainErrorCode.InvalidParameter, "Se necesita al menos una componente.");

        var xMean = new double[p];
        var xStd = Enumerable.Repeat(1.0, p).ToArray();
        for (var column = 0; column < p; column++)
        {
            xMean[column] = x.Sum(row => row[column]) / n;
            if (scale)
            {
                var variance = x.Sum(row => Math.Pow(row[column] - xMean[column], 2)) / (n - 1.0);
                xStd[column] = variance > 0 ? Math.Sqrt(variance) : 1.0;
            }
        }
        var yMean = y.Average();
        var yStd = 1.0;
        if (scale)
        {
            var variance = y.Sum(value => Math.Pow(value - yMean, 2)) / (n - 1.0);
            yStd = variance > 0 ? Math.Sqrt(variance) : 1.0;
        }

        var xk = new double[n][];
        var yk = new double[n];
        for (var row = 0; row < n; row++)
        {
            xk[row] = new double[p];
            for (var column = 0; column < p; column++) xk[row][column] = (x[row][column] - xMean[column]) / xStd[column];
            yk[row] = (y[row] - yMean) / yStd;
        }

        var xWeights = new double[p][];
        var yWeights = new double[count];
        var xLoadings = new double[p][];
        var yLoadings = new double[count];
        var xScores = new double[n][];
        var yScores = new double[n][];
        for (var column = 0; column < p; column++)
        {
            xWeights[column] = new double[count];
            xLoadings[column] = new double[count];
        }
        for (var row = 0; row < n; row++)
        {
            xScores[row] = new double[count];
            yScores[row] = new double[count];
        }

        var epsilon = double.Epsilon * 1e6;
        for (var component = 0; component < count; component++)
        {
            var yNorm = yk.Sum(value => value * value);
            if (yNorm <= 0) break;
            var weight = new double[p];
            for (var column = 0; column < p; column++)
            {
                var sum = 0.0;
                for (var row = 0; row < n; row++) sum += xk[row][column] * yk[row];
                weight[column] = sum / yNorm;
            }
            var weightNorm = Math.Sqrt(weight.Sum(value => value * value)) + epsilon;
            for (var column = 0; column < p; column++) weight[column] /= weightNorm;

            var score = new double[n];
            for (var row = 0; row < n; row++)
            {
                var sum = 0.0;
                for (var column = 0; column < p; column++) sum += xk[row][column] * weight[column];
                score[row] = sum;
            }
            var scoreNorm = score.Sum(value => value * value);
            var yWeight = 0.0;
            for (var row = 0; row < n; row++) yWeight += yk[row] * score[row];
            yWeight /= scoreNorm;

            // _svd_flip_1d: el signo lo fija el mayor valor absoluto de x_weights.
            var signIndex = 0;
            for (var column = 1; column < p; column++)
            {
                if (Math.Abs(weight[column]) > Math.Abs(weight[signIndex])) signIndex = column;
            }
            var sign = weight[signIndex] < 0 ? -1.0 : 1.0;
            if (sign < 0)
            {
                for (var column = 0; column < p; column++) weight[column] = -weight[column];
                yWeight = -yWeight;
            }
            for (var row = 0; row < n; row++) score[row] = 0;
            for (var row = 0; row < n; row++)
            {
                var sum = 0.0;
                for (var column = 0; column < p; column++) sum += xk[row][column] * weight[column];
                score[row] = sum;
            }
            scoreNorm = score.Sum(value => value * value);
            var yScore = new double[n];
            var yWeightSquared = yWeight * yWeight;
            for (var row = 0; row < n; row++) yScore[row] = yWeightSquared > 0 ? yk[row] * yWeight / yWeightSquared : 0;

            var loading = new double[p];
            for (var column = 0; column < p; column++)
            {
                var sum = 0.0;
                for (var row = 0; row < n; row++) sum += xk[row][column] * score[row];
                loading[column] = sum / scoreNorm;
            }
            var yLoading = 0.0;
            for (var row = 0; row < n; row++) yLoading += yk[row] * score[row];
            yLoading /= scoreNorm;

            for (var column = 0; column < p; column++) xWeights[column][component] = weight[column];
            yWeights[component] = yWeight;
            for (var column = 0; column < p; column++) xLoadings[column][component] = loading[column];
            yLoadings[component] = yLoading;
            for (var row = 0; row < n; row++)
            {
                xScores[row][component] = score[row];
                yScores[row][component] = yScore[row];
                for (var column = 0; column < p; column++) xk[row][column] -= score[row] * loading[column];
                yk[row] -= score[row] * yLoading;
            }
        }

        var loadingWeights = new double[count][];
        for (var i = 0; i < count; i++)
        {
            loadingWeights[i] = new double[count];
            for (var j = 0; j < count; j++)
            {
                var sum = 0.0;
                for (var column = 0; column < p; column++) sum += xLoadings[column][i] * xWeights[column][j];
                loadingWeights[i][j] = sum;
            }
        }
        var inverse = LinearAlgebra.Inverse(loadingWeights);
        if (inverse.IsError) return Result.Err<PlsFit>(inverse.Error.Code, inverse.Error.Message);
        var rotations = new double[p][];
        for (var column = 0; column < p; column++)
        {
            rotations[column] = new double[count];
            for (var j = 0; j < count; j++)
            {
                var sum = 0.0;
                for (var k = 0; k < count; k++) sum += xWeights[column][k] * inverse.Value[k][j];
                rotations[column][j] = sum;
            }
        }
        var coefficients = new double[p];
        for (var column = 0; column < p; column++)
        {
            var sum = 0.0;
            for (var component = 0; component < count; component++) sum += rotations[column][component] * yLoadings[component];
            coefficients[column] = sum * yStd / xStd[column];
        }

        var fitted = new double[n];
        var r2Numerator = 0.0;
        var r2Denominator = 0.0;
        for (var row = 0; row < n; row++)
        {
            var prediction = yMean;
            for (var column = 0; column < p; column++) prediction += (x[row][column] - xMean[column]) * coefficients[column];
            fitted[row] = prediction;
            r2Numerator += Math.Pow(y[row] - prediction, 2);
            r2Denominator += Math.Pow(y[row] - yMean, 2);
        }

        var xScoreVariances = new double[count];
        var yScoreVariances = new double[count];
        var xTotal = 0.0;
        for (var column = 0; column < p; column++)
        {
            var mean = x.Sum(row => row[column]) / n;
            xTotal += x.Sum(row => Math.Pow(row[column] - mean, 2)) / n;
        }
        var yAverage = y.Average();
        var yTotal = y.Sum(value => Math.Pow(value - yAverage, 2)) / n;
        for (var component = 0; component < count; component++)
        {
            var xsMean = xScores.Sum(row => row[component]) / n;
            var ysMean = yScores.Sum(row => row[component]) / n;
            xScoreVariances[component] = xScores.Sum(row => Math.Pow(row[component] - xsMean, 2)) / n / Math.Max(1e-300, xTotal);
            yScoreVariances[component] = yScores.Sum(row => Math.Pow(row[component] - ysMean, 2)) / n / Math.Max(1e-300, yTotal);
        }

        return Result.Ok(new PlsFit(
            coefficients,
            fitted,
            xScoreVariances,
            yScoreVariances,
            xTotal,
            yTotal,
            count,
            r2Denominator > 0 ? 1 - r2Numerator / r2Denominator : double.NaN));
    }
}
