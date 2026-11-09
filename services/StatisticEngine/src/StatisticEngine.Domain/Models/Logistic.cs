using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Models;

/// <summary>Resultado de una regresión logística binaria ajustada por IRLS.</summary>
public sealed record LogisticResult(
    string[] Terms,
    double[] Coefficients,
    double[] StandardErrors,
    double[] Z,
    double[] PValues,
    double Deviance,
    double NullDeviance,
    double Aic,
    double Iterations,
    double[] Predicted);

/// <summary>
/// Regresión logística binaria por mínimos cuadrados reponderados (port 1:1 de
/// <c>regression.ts</c>): máximo 50 iteraciones y convergencia estricta en el paso máximo.
/// </summary>
public static class Logistic
{
    private const int MaxIterations = 50;
    private const double StepTolerance = 1e-8;

    public static Result<LogisticResult> LogisticRegression(
        double[] response,
        double[][] predictors,
        string[] names)
    {
        foreach (var value in response)
        {
            if (value != 0 && value != 1)
            {
                return Result<LogisticResult>.Err(DomainErrorCode.InvalidInput, "La respuesta debe ser 0/1.");
            }
        }

        if (predictors.Length == 0)
        {
            return Result<LogisticResult>.Err(DomainErrorCode.InvalidInput, "Se necesita al menos un predictor.");
        }

        var fit = Fit(Regression.BuildDesign(predictors, response.Length), response);
        if (!fit.IsOk)
        {
            return Result<LogisticResult>.Err(fit.Error.Code, fit.Error.Message);
        }

        var terms = new string[1 + names.Length];
        terms[0] = "Constante";
        for (var index = 0; index < names.Length; index++) terms[index + 1] = names[index];
        return Result<LogisticResult>.Ok(fit.Value with { Terms = terms });
    }

    private static Result<LogisticResult> Fit(double[][] x, double[] y)
    {
        var n = y.Length;
        var p = (x.Length > 0 ? x[0].Length : 0) + 1;
        var beta = new double[p];
        var design = new double[n][];
        for (var i = 0; i < n; i++)
        {
            var row = new double[p];
            row[0] = 1;
            for (var j = 1; j < p; j++) row[j] = x[i][j - 1];
            design[i] = row;
        }

        var eta = new double[n];
        var mu = new double[n];
        Array.Fill(mu, 0.5);
        var iterations = 0;
        var converged = false;
        for (; iterations < MaxIterations; iterations++)
        {
            for (var i = 0; i < n; i++)
            {
                var linear = 0.0;
                var row = design[i];
                for (var j = 0; j < p; j++) linear += row[j] * beta[j];
                eta[i] = linear;
                mu[i] = 1 / (1 + Math.Exp(-linear));
            }

            var info = new double[p][];
            for (var a = 0; a < p; a++) info[a] = new double[p];
            var score = new double[p];
            for (var i = 0; i < n; i++)
            {
                var weight = mu[i] * (1 - mu[i]);
                var row = design[i];
                for (var a = 0; a < p; a++)
                {
                    score[a] += row[a] * (y[i] - mu[i]);
                    for (var b = 0; b < p; b++) info[a][b] += weight * row[a] * row[b];
                }
            }

            var inverse = Regression.InvertSmall(info);
            if (inverse is null)
            {
                return Result<LogisticResult>.Err(DomainErrorCode.SingularMatrix, "Matriz de información singular en la regresión logística.");
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

        if (!converged && iterations >= MaxIterations)
        {
            return Result<LogisticResult>.Err(DomainErrorCode.NonConvergence, "La regresión logística no convergió.");
        }

        for (var i = 0; i < n; i++)
        {
            var linear = 0.0;
            var row = design[i];
            for (var j = 0; j < p; j++) linear += row[j] * beta[j];
            mu[i] = 1 / (1 + Math.Exp(-linear));
        }

        var finalInfo = new double[p][];
        for (var a = 0; a < p; a++) finalInfo[a] = new double[p];
        for (var i = 0; i < n; i++)
        {
            var weight = mu[i] * (1 - mu[i]);
            var row = design[i];
            for (var a = 0; a < p; a++)
            {
                for (var b = 0; b < p; b++) finalInfo[a][b] += weight * row[a] * row[b];
            }
        }

        var finalInverse = Regression.InvertSmall(finalInfo);
        if (finalInverse is null)
        {
            return Result<LogisticResult>.Err(DomainErrorCode.SingularMatrix, "Matriz de información singular.");
        }

        var prevalence = 0.0;
        foreach (var value in y) prevalence += value;
        prevalence /= n;

        var deviance = 0.0;
        var nullDeviance = 0.0;
        for (var i = 0; i < n; i++)
        {
            var probability = Math.Min(1 - 1e-12, Math.Max(1e-12, mu[i]));
            deviance += -2 * (y[i] * Math.Log(probability) + (1 - y[i]) * Math.Log(1 - probability));
            nullDeviance += -2 * (y[i] * Math.Log(prevalence) + (1 - y[i]) * Math.Log(1 - prevalence));
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

        return Result<LogisticResult>.Ok(new LogisticResult(
            ["Constante"],
            beta,
            standardErrors,
            z,
            pValues,
            deviance,
            nullDeviance,
            deviance + 2 * p,
            iterations,
            mu));
    }
}
