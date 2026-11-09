using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Models;

/// <summary>Paso del procedimiento stepwise: término añadido o eliminado.</summary>
public sealed record StepwiseStep(double Step, string Action, string Term, double RSquared, double Aic);

public sealed record StepwiseResult(string[] Selected, StepwiseStep[] Steps, RegressionResult Model);

/// <summary>
/// Selección de variables por AIC (port 1:1 de <c>regression.ts</c>): forward o backward,
/// umbral 1e-9 y NON_CONVERGENCE si no retiene ningún término.
/// </summary>
public static class Stepwise
{
    public static Result<StepwiseResult> StepwiseRegression(RegressionInput input, string direction = "forward")
    {
        var p = input.Predictors.Length;
        if (p == 0)
        {
            return Result<StepwiseResult>.Err(DomainErrorCode.InvalidInput, "Sin predictores.");
        }

        var n = input.Response.Length;
        (double RSquared, double Aic)? FitFor(int[] indices)
        {
            if (indices.Length == 0)
            {
                var responseMean = NumericMath.Mean(input.Response);
                var sst = 0.0;
                foreach (var value in input.Response) sst += Math.Pow(value - responseMean, 2);
                return (0, n * Math.Log(sst / n) + 2 * 2);
            }
            var predictors = new double[indices.Length][];
            for (var i = 0; i < indices.Length; i++) predictors[i] = input.Predictors[indices[i]];
            var fit = LinearAlgebra.LeastSquares(Regression.BuildDesign(predictors, n), input.Response);
            if (!fit.IsOk) return null;
            var sse = 0.0;
            foreach (var value in fit.Value.Residuals) sse += value * value;
            return (fit.Value.RSquared, Math.Max(1e-300, n * Math.Log(Math.Max(sse / n, 1e-300)) + 2 * (indices.Length + 2)));
        }

        var steps = new List<StepwiseStep>();
        var selected = new List<int>();
        if (string.Equals(direction, "forward", StringComparison.Ordinal))
        {
            var current = FitFor([.. selected]);
            for (var step = 1; step <= p; step++)
            {
                var bestIndex = -1;
                (double RSquared, double Aic)? best = current;
                for (var i = 0; i < p; i++)
                {
                    if (selected.Contains(i)) continue;
                    var candidate = FitFor([.. selected, i]);
                    if (candidate is { } fitted && best is { } incumbent && fitted.Aic < incumbent.Aic - 1e-9)
                    {
                        best = fitted;
                        bestIndex = i;
                    }
                }
                if (bestIndex < 0 || best is not { } chosen) break;
                selected.Add(bestIndex);
                current = chosen;
                steps.Add(new StepwiseStep(
                    step,
                    "Añadido",
                    bestIndex < input.Names.Length ? input.Names[bestIndex] : $"X{bestIndex + 1}",
                    chosen.RSquared,
                    chosen.Aic));
            }
        }
        else
        {
            selected = [.. Enumerable.Range(0, p)];
            var current = FitFor([.. selected]);
            for (var step = 1; step <= p; step++)
            {
                var worstIndex = -1;
                (double RSquared, double Aic)? best = current;
                foreach (var index in selected)
                {
                    var candidate = FitFor([.. selected.Where(item => item != index)]);
                    if (candidate is { } fitted && best is { } incumbent && fitted.Aic < incumbent.Aic - 1e-9)
                    {
                        best = fitted;
                        worstIndex = index;
                    }
                }
                if (worstIndex < 0 || best is not { } chosen) break;
                selected = [.. selected.Where(item => item != worstIndex)];
                current = chosen;
                steps.Add(new StepwiseStep(
                    step,
                    "Eliminado",
                    worstIndex < input.Names.Length ? input.Names[worstIndex] : $"X{worstIndex + 1}",
                    chosen.RSquared,
                    chosen.Aic));
            }
        }

        if (selected.Count == 0)
        {
            return Result<StepwiseResult>.Err(DomainErrorCode.NonConvergence, "El stepwise no retuvo predictores.");
        }

        var modelPredictors = new double[selected.Count][];
        var modelNames = new string[selected.Count];
        for (var i = 0; i < selected.Count; i++)
        {
            modelPredictors[i] = input.Predictors[selected[i]];
            modelNames[i] = selected[i] < input.Names.Length ? input.Names[selected[i]] : $"X{selected[i] + 1}";
        }

        var model = Regression.MultipleRegression(new RegressionInput(input.Response, modelPredictors, modelNames));
        if (!model.IsOk)
        {
            return Result<StepwiseResult>.Err(model.Error.Code, model.Error.Message);
        }

        var selectedNames = new string[selected.Count];
        for (var i = 0; i < selected.Count; i++)
        {
            selectedNames[i] = selected[i] < input.Names.Length ? input.Names[selected[i]] : $"X{selected[i] + 1}";
        }
        return Result<StepwiseResult>.Ok(new StepwiseResult(selectedNames, [.. steps], model.Value));
    }
}
