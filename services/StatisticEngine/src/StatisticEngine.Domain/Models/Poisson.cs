using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Models;

/// <summary>Resultado de una regresión Poisson ajustada por IRLS.</summary>
public sealed record PoissonResult(
    string[] Terms,
    double[] Coefficients,
    double[] StandardErrors,
    double[] Z,
    double[] PValues,
    double Deviance,
    double Aic,
    double Iterations);

/// <summary>
/// Regresión Poisson por mínimos cuadrados reponderados (port 1:1 de <c>regression.ts</c>):
/// máximo 100 iteraciones estrictas y predictor lineal acotado a <c>exp(min(50, Xβ))</c>.
/// </summary>
public static class Poisson
{
    private const int MaxIterations = 100;
    private const double StepTolerance = 1e-8;

    public static Result<PoissonResult> PoissonRegression(
        double[] response,
        double[][] predictors,
        string[] names)
    {
        foreach (var value in response)
        {
            if (value < 0 || !JsMath.IsInteger(value))
            {
                return Result<PoissonResult>.Err(DomainErrorCode.InvalidInput, "La respuesta Poisson debe ser un conteo.");
            }
        }

        var n = response.Length;
        var p = predictors.Length + 1;
        var design = WithIntercept(predictors, n);
        var beta = new double[p];
        var iterations = 0;
        var converged = false;
        for (; iterations < MaxIterations; iterations++)
        {
            var mu = Predict(design, beta);
            var info = new double[p][];
            for (var a = 0; a < p; a++) info[a] = new double[p];
            var score = new double[p];
            for (var i = 0; i < n; i++)
            {
                var row = design[i];
                for (var a = 0; a < p; a++)
                {
                    score[a] += row[a] * (response[i] - mu[i]);
                    for (var b = 0; b < p; b++) info[a][b] += mu[i] * row[a] * row[b];
                }
            }

            var inverse = Regression.InvertSmall(info);
            if (inverse is null)
            {
                return Result<PoissonResult>.Err(DomainErrorCode.SingularMatrix, "Matriz de información singular en la regresión Poisson.");
            }

            var maxStep = 0.0;
            for (var a = 0; a < p; a++)
            {
                var delta = 0.0;
                for (var b = 0; b < p; b++) delta += inverse[a][b] * score[b];
                beta[a] += delta;
                maxStep = Math.Max(maxStep, Math.Abs(delta));
            }
            if (maxStep < StepTolerance)
            {
                converged = true;
                break;
            }
        }

        // paridad: regression.ts:397 — Poisson exige convergencia estricta dentro de 100 iteraciones.
        if (!converged)
        {
            return Result<PoissonResult>.Err(DomainErrorCode.NonConvergence, "La regresión Poisson no convergió.");
        }

        var fitted = Predict(design, beta);
        var finalInfo = new double[p][];
        for (var a = 0; a < p; a++) finalInfo[a] = new double[p];
        for (var i = 0; i < n; i++)
        {
            var row = design[i];
            for (var a = 0; a < p; a++)
            {
                for (var b = 0; b < p; b++) finalInfo[a][b] += fitted[i] * row[a] * row[b];
            }
        }

        var finalInverse = Regression.InvertSmall(finalInfo);
        if (finalInverse is null)
        {
            return Result<PoissonResult>.Err(DomainErrorCode.SingularMatrix, "Matriz de información singular.");
        }

        var deviance = 0.0;
        for (var i = 0; i < n; i++)
        {
            var observed = response[i];
            var expected = Math.Max(fitted[i], 1e-12);
            deviance += observed > 0
                ? 2 * (observed * Math.Log(observed / expected) - (observed - expected))
                : 2 * expected;
        }

        var standardErrors = new double[p];
        var z = new double[p];
        var pValues = new double[p];
        for (var index = 0; index < p; index++)
        {
            standardErrors[index] = Math.Sqrt(finalInverse[index][index]);
            z[index] = beta[index] / standardErrors[index];
            pValues[index] = 2 * Math.Min(
                SpecialFunctions.NormalCdf(z[index]),
                1 - SpecialFunctions.NormalCdf(z[index]));
        }

        var terms = new string[1 + names.Length];
        terms[0] = "Constante";
        for (var index = 0; index < names.Length; index++) terms[index + 1] = names[index];

        return Result<PoissonResult>.Ok(new PoissonResult(
            terms,
            beta,
            standardErrors,
            z,
            pValues,
            deviance,
            deviance + 2 * p,
            iterations));
    }

    /// <summary>Diseño con columna de unos; una posición ausente del predictor produce NaN como en JS.</summary>
    private static double[][] WithIntercept(double[][] predictors, int n)
    {
        var design = new double[n][];
        for (var i = 0; i < n; i++)
        {
            var row = new double[predictors.Length + 1];
            row[0] = 1;
            for (var j = 0; j < predictors.Length; j++)
            {
                var column = predictors[j];
                row[j + 1] = i < column.Length ? column[i] : double.NaN;
            }
            design[i] = row;
        }
        return design;
    }

    private static double[] Predict(double[][] design, double[] beta)
    {
        var mu = new double[design.Length];
        for (var i = 0; i < design.Length; i++)
        {
            var row = design[i];
            var linear = 0.0;
            for (var j = 0; j < row.Length; j++) linear += row[j] * beta[j];
            mu[i] = Math.Exp(Math.Min(50, linear));
        }
        return mu;
    }
}
