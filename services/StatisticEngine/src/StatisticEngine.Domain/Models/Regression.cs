using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Models;

/// <summary>Coeficiente estimado de un término del modelo de regresión.</summary>
public sealed record RegressionCoefficient(
    string Term,
    double Estimate,
    double StandardError,
    double T,
    double PValue);

/// <summary>Resultado de una regresión lineal múltiple por mínimos cuadrados.</summary>
public sealed record RegressionResult(
    string[] Terms,
    RegressionCoefficient[] Coefficients,
    double RSquared,
    double AdjustedRSquared,
    double StandardError,
    double Observations,
    double DfError,
    double SsRegression,
    double SsError,
    double SsTotal,
    double F,
    double PValue,
    double[] Residuals,
    double[] Fitted,
    double[] Vif,
    double DurbinWatson);

/// <summary>Entrada común de los modelos de regresión: respuesta, predictores y nombres.</summary>
public sealed record RegressionInput(double[] Response, double[][] Predictors, string[] Names);

/// <summary>
/// Regresión lineal múltiple (port 1:1 de <c>regression.ts</c>): QR de Householder, errores
/// estándar con la inversa de X'X, VIF y Durbin-Watson.
/// </summary>
public static class Regression
{
    /// <summary>Ajuste por mínimos cuadrados con coeficientes, tabla de bondad e inflación de varianza.</summary>
    public static Result<RegressionResult> MultipleRegression(RegressionInput input)
    {
        var rows = input.Response.Length;
        if (input.Predictors.Length == 0)
        {
            return Result<RegressionResult>.Err(DomainErrorCode.InvalidInput, "Se necesita al menos un predictor.");
        }
        if (rows < input.Predictors.Length + 2)
        {
            return Result<RegressionResult>.Err(DomainErrorCode.InsufficientData, "Pocas observaciones para el número de predictores.");
        }

        var design = BuildDesign(input.Predictors, rows);
        var fit = LinearAlgebra.LeastSquares(design, input.Response);
        if (!fit.IsOk)
        {
            return Result<RegressionResult>.Err(fit.Error.Code, fit.Error.Message);
        }

        var p = input.Predictors.Length + 1;
        var n = rows;
        double dfError = n - p;
        var mse = Math.Pow(fit.Value.StandardError, 2);

        var xt = new double[n][];
        for (var i = 0; i < n; i++)
        {
            var row = new double[p];
            row[0] = 1;
            for (var j = 1; j < p; j++) row[j] = design[i][j - 1];
            xt[i] = row;
        }

        var xtx = new double[p][];
        for (var i = 0; i < p; i++)
        {
            xtx[i] = new double[p];
            for (var j = 0; j < p; j++)
            {
                var acc = 0.0;
                for (var row = 0; row < n; row++) acc += xt[row][i] * xt[row][j];
                xtx[i][j] = acc;
            }
        }

        // paridad: regression.ts:62-63 — inversa explícita de X'X para los errores estándar.
        var xtxInverse = InvertSmall(xtx);
        if (xtxInverse is null)
        {
            return Result<RegressionResult>.Err(DomainErrorCode.SingularMatrix, "Predictores colineales: no se puede estimar el modelo.");
        }

        var terms = new string[1 + input.Names.Length];
        terms[0] = "Constante";
        for (var index = 0; index < input.Names.Length; index++) terms[index + 1] = input.Names[index];

        var coefficients = new RegressionCoefficient[fit.Value.Coefficients.Length];
        for (var index = 0; index < coefficients.Length; index++)
        {
            var estimate = fit.Value.Coefficients[index];
            var variance = mse * xtxInverse[index][index];
            var standardError = Math.Sqrt(variance);
            var t = estimate / standardError;
            coefficients[index] = new RegressionCoefficient(
                index < terms.Length ? terms[index] : $"b{index}",
                estimate,
                standardError,
                t,
                2 * Math.Min(
                    InferenceDistributions.TCdf(t, dfError),
                    1 - InferenceDistributions.TCdf(t, dfError)));
        }

        var responseMean = NumericMath.Mean(input.Response);
        var sst = 0.0;
        foreach (var value in input.Response) sst += Math.Pow(value - responseMean, 2);
        var sse = 0.0;
        foreach (var value in fit.Value.Residuals) sse += value * value;
        var ssr = sst - sse;

        var vif = new double[input.Predictors.Length];
        for (var index = 0; index < input.Predictors.Length; index++)
        {
            if (input.Predictors.Length == 1)
            {
                vif[index] = 1;
                continue;
            }
            var others = new double[input.Predictors.Length - 1][];
            var position = 0;
            for (var j = 0; j < input.Predictors.Length; j++)
            {
                if (j == index) continue;
                others[position++] = input.Predictors[j];
            }
            var subFit = LinearAlgebra.LeastSquares(BuildDesign(others, rows), input.Predictors[index]);
            vif[index] = subFit.IsOk
                ? 1 / Math.Max(1e-12, 1 - subFit.Value.RSquared)
                : double.NaN;
        }

        var f = ssr / (p - 1) / mse;
        return Result<RegressionResult>.Ok(new RegressionResult(
            terms,
            coefficients,
            fit.Value.RSquared,
            fit.Value.AdjustedRSquared,
            fit.Value.StandardError,
            n,
            dfError,
            ssr,
            sse,
            sst,
            f,
            FUpper(f, p - 1, dfError),
            fit.Value.Residuals,
            fit.Value.Fitted,
            vif,
            DurbinWatson(fit.Value.Residuals)));
    }

    /// <summary>Estadístico de Durbin-Watson sobre los residuales del modelo.</summary>
    public static double DurbinWatson(double[] residuals)
    {
        if (residuals.Length < 2) return double.NaN;
        var numerator = 0.0;
        var denominator = 0.0;
        for (var i = 0; i < residuals.Length; i++)
        {
            denominator += Math.Pow(residuals[i], 2);
            if (i > 0) numerator += Math.Pow(residuals[i] - residuals[i - 1], 2);
        }
        return denominator > 0 ? numerator / denominator : double.NaN;
    }

    /// <summary>Matriz de diseño n×p sin columna de unos; un índice ausente produce NaN como en JS.</summary>
    internal static double[][] BuildDesign(double[][] predictors, int rows)
    {
        var design = new double[rows][];
        for (var i = 0; i < rows; i++)
        {
            var row = new double[predictors.Length];
            for (var j = 0; j < predictors.Length; j++)
            {
                var column = predictors[j];
                row[j] = i < column.Length ? column[i] : double.NaN;
            }
            design[i] = row;
        }
        return design;
    }

    /// <summary>
    /// Inversa por eliminación gaussiana con pivoteo parcial, idéntica a <c>invertSmall</c>
    /// de <c>regression.ts</c> (umbral 1e-12 y pivoteo por valor absoluto).
    /// </summary>
    internal static double[][]? InvertSmall(double[][] matrix)
    {
        var n = matrix.Length;
        var augmented = new double[n][];
        for (var i = 0; i < n; i++)
        {
            var row = new double[2 * n];
            for (var j = 0; j < n; j++)
            {
                row[j] = matrix[i][j];
                row[n + j] = i == j ? 1 : 0;
            }
            augmented[i] = row;
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
            if (pivot != col)
            {
                (augmented[pivot], augmented[col]) = (augmented[col], augmented[pivot]);
            }
            var pivotRow = augmented[col];
            for (var j = 0; j < 2 * n; j++) pivotRow[j] /= pivotValue;
            for (var row = 0; row < n; row++)
            {
                if (row == col) continue;
                var factor = augmented[row][col];
                if (factor == 0) continue;
                var target = augmented[row];
                for (var j = 0; j < 2 * n; j++) target[j] -= factor * pivotRow[j];
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

    /// <summary>Cola superior de F para las tablas de regresión (equivalente local de <c>regression.ts</c>).</summary>
    private static double FUpper(double f, double df1, double df2)
    {
        if (!(f >= 0)) return double.NaN;
        return 1 - BetaCdf(df1 * f / (df1 * f + df2), df1 / 2, df2 / 2);
    }

    /// <summary>Beta incompleta regularizada con los topes de iteración de <c>regression.ts</c>.</summary>
    private static double BetaCdf(double x, double a, double b)
    {
        var logBeta = GammaLog(a + b) - GammaLog(a) - GammaLog(b)
            + a * Math.Log(Math.Max(x, 1e-300)) + b * Math.Log(Math.Max(1 - x, 1e-300));
        var front = Math.Exp(logBeta);
        if (x < (a + 1) / (a + b + 2)) return front * BetaContinuedFraction(a, b, x) / a;
        return 1 - front * BetaContinuedFraction(b, a, 1 - x) / b;
    }

    private static double BetaContinuedFraction(double a, double b, double x)
    {
        var qab = a + b;
        var qap = a + 1;
        var qam = a - 1;
        var c = 1.0;
        var d = 1 - qab * x / qap;
        if (Math.Abs(d) < 1e-300) d = 1e-300;
        d = 1 / d;
        var h = d;
        for (var m = 1; m < 2000; m++)
        {
            var m2 = 2 * m;
            var aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1 + aa * d;
            if (Math.Abs(d) < 1e-300) d = 1e-300;
            c = 1 + aa / c;
            if (Math.Abs(c) < 1e-300) c = 1e-300;
            d = 1 / d;
            h *= d * c;
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1 + aa * d;
            if (Math.Abs(d) < 1e-300) d = 1e-300;
            c = 1 + aa / c;
            if (Math.Abs(c) < 1e-300) c = 1e-300;
            d = 1 / d;
            var delta = d * c;
            h *= delta;
            if (Math.Abs(delta - 1) < 1e-16) break;
        }
        return h;
    }

    /// <summary>lgamma de Lanczos con los mismos coeficientes que <c>regression.ts</c>.</summary>
    private static double GammaLog(double x)
    {
        if (x < 0.5) return Math.Log(Math.PI / Math.Sin(Math.PI * x)) - GammaLog(1 - x);
        var z = x - 1;
        var a = LanczosCoefficients[0];
        var t = z + 7.5;
        for (var i = 1; i < LanczosCoefficients.Length; i++) a += LanczosCoefficients[i] / (z + i);
        return 0.5 * Math.Log(2 * Math.PI) + (z + 0.5) * Math.Log(t) - t + Math.Log(a);
    }

    private static readonly double[] LanczosCoefficients =
    [
        0.99999999999980993,
        676.5203681218851,
        -1259.1392167224028,
        771.32342877765313,
        -176.61502916214059,
        12.507343278686905,
        -0.13857109526572012,
        9.9843695780195716e-6,
        1.5056327351493116e-7
    ];
}
