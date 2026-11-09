using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Random;

namespace StatisticEngine.Domain.Multivariate;

/// <summary>Resultado de k-means (paridad: <c>multivariate.ts:147-153</c>).</summary>
public sealed record ClusterResult(int K, int[] Labels, double[][] Centers, double[] WithinSS, double TotalWithinSS);

/// <summary>
/// k-means con inicialización k-means++ sobre datos estandarizados.
/// Paridad 1:1 con <c>multivariate.ts:155-207</c>: 100 iteraciones como máximo y el centro de
/// un clúster vacío se conserva.
/// </summary>
public static class KMeans
{
    public static Result<ClusterResult> Cluster(double[][] rows, int k, uint seed = Xoshiro128.DefaultSeed)
    {
        var n = rows.Length;
        var dims = rows.Length > 0 ? rows[0].Length : 0;
        if (n < k || k < 2 || dims < 1)
        {
            return Result<ClusterResult>.Err(DomainErrorCode.InvalidParameter, "Configuración de k-means inválida.");
        }

        var standardized = Pca.Standardize(rows);
        var rng = new Xoshiro128(seed);
        var centers = new List<double[]>();
        var first = (int)Math.Floor(rng.Next() * n);
        centers.Add((double[])standardized[first].Clone());

        while (centers.Count < k)
        {
            var distances = new double[n];
            for (var i = 0; i < n; i++)
            {
                var best = double.PositiveInfinity;
                for (var c = 0; c < centers.Count; c++)
                {
                    var sum = 0.0;
                    for (var j = 0; j < dims; j++)
                    {
                        var delta = standardized[i][j] - centers[c][j];
                        sum += delta * delta;
                    }
                    best = JsMath.Min(best, sum);
                }
                distances[i] = best;
            }
            var total = 0.0;
            for (var i = 0; i < n; i++) total += distances[i];
            var target = rng.Next() * total;
            var index = 0;
            for (var i = 0; i < n; i++)
            {
                target -= distances[i];
                if (target <= 0)
                {
                    index = i;
                    break;
                }
            }
            centers.Add((double[])standardized[index].Clone());
        }

        var labels = new int[n];
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var changed = false;
            for (var i = 0; i < n; i++)
            {
                var best = 0;
                var bestDistance = double.PositiveInfinity;
                for (var c = 0; c < k; c++)
                {
                    var sum = 0.0;
                    for (var j = 0; j < dims; j++)
                    {
                        var delta = standardized[i][j] - centers[c][j];
                        sum += delta * delta;
                    }
                    if (sum < bestDistance)
                    {
                        bestDistance = sum;
                        best = c;
                    }
                }
                if (labels[i] != best)
                {
                    labels[i] = best;
                    changed = true;
                }
            }

            for (var c = 0; c < k; c++)
            {
                var members = new List<int>();
                for (var i = 0; i < n; i++)
                {
                    if (labels[i] == c) members.Add(i);
                }
                if (members.Count == 0) continue;
                var center = new double[dims];
                for (var j = 0; j < dims; j++)
                {
                    var sum = 0.0;
                    for (var m = 0; m < members.Count; m++) sum += standardized[members[m]][j];
                    center[j] = sum / members.Count;
                }
                centers[c] = center;
            }

            if (!changed) break;
        }

        var withinSs = new double[k];
        var totalWithin = 0.0;
        for (var c = 0; c < k; c++)
        {
            var sum = 0.0;
            for (var i = 0; i < n; i++)
            {
                if (labels[i] != c) continue;
                for (var j = 0; j < dims; j++)
                {
                    var delta = standardized[i][j] - centers[c][j];
                    sum += delta * delta;
                }
            }
            withinSs[c] = sum;
            totalWithin += sum;
        }

        return Result<ClusterResult>.Ok(new ClusterResult(k, labels, [.. centers], withinSs, totalWithin));
    }
}
