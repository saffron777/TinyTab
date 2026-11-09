using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Models;

/// <summary>Modelo candidato de la búsqueda exhaustiva de subconjuntos.</summary>
public sealed record SubsetModel(
    double[] Terms,
    string[] Names,
    double RSquared,
    double AdjustedRSquared,
    double Aic,
    double MallowCp);

/// <summary>
/// Búsqueda exhaustiva de los mejores subconjuntos de predictores (port 1:1 de
/// <c>regression.ts</c>): hasta 14 predictores, descarta los modelos con más de
/// <c>maxPredictors</c> términos, ordena por R² ajustado y devuelve como máximo 20.
/// </summary>
public static class BestSubsetSearch
{
    public static Result<SubsetModel[]> BestSubsets(RegressionInput input, int maxPredictors = 4)
    {
        var p = input.Predictors.Length;
        if (p > 14)
        {
            return Result<SubsetModel[]>.Err(DomainErrorCode.NotSupported, "Demasiados predictores para búsqueda exhaustiva (máximo 14).");
        }

        var candidates = new List<SubsetModel>();
        var totalMse = TotalMse(input);
        var n = input.Response.Length;
        var limit = 1 << p;
        for (var mask = 1; mask < limit; mask++)
        {
            var terms = new List<int>();
            for (var i = 0; i < p; i++)
            {
                if ((mask & (1 << i)) != 0) terms.Add(i);
            }
            if (terms.Count > maxPredictors) continue;

            var predictors = new double[terms.Count][];
            var names = new string[terms.Count];
            for (var position = 0; position < terms.Count; position++)
            {
                var index = terms[position];
                predictors[position] = input.Predictors[index];
                names[position] = index < input.Names.Length ? input.Names[index] : $"X{index + 1}";
            }

            var fit = LinearAlgebra.LeastSquares(Regression.BuildDesign(predictors, n), input.Response);
            if (!fit.IsOk) continue;

            var sse = 0.0;
            foreach (var value in fit.Value.Residuals) sse += value * value;
            var aic = n * Math.Log(sse / n) + 2 * (terms.Count + 2);
            var mallow = sse / totalMse - (n - 2 * (terms.Count + 1));

            var termNumbers = new double[terms.Count];
            for (var position = 0; position < terms.Count; position++) termNumbers[position] = terms[position];
            candidates.Add(new SubsetModel(
                termNumbers,
                names,
                fit.Value.RSquared,
                fit.Value.AdjustedRSquared,
                aic,
                mallow));
        }

        // paridad: regression.ts:175-176 — orden estable por R² ajustado y tope de 20 modelos.
        var ordered = candidates
            .OrderByDescending(candidate => candidate.AdjustedRSquared)
            .Take(20)
            .ToArray();
        return Result<SubsetModel[]>.Ok(ordered);
    }

    private static double TotalMse(RegressionInput input)
    {
        var fit = LinearAlgebra.LeastSquares(
            Regression.BuildDesign(input.Predictors, input.Response.Length),
            input.Response);
        return fit.IsOk ? Math.Pow(fit.Value.StandardError, 2) : double.NaN;
    }
}
