using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Doe;

/// <summary>Análisis de efectos de un diseño factorial por mínimos cuadrados.</summary>
public sealed record EffectsAnalysis(
    string[] Terms,
    double[] Coefficients,
    double[] StandardErrors,
    double[] T,
    double[] PValues,
    double[] Effects,
    double RSquared,
    double[] Fitted,
    double[] Residuals);

/// <summary>
/// Análisis de un diseño factorial 2^k por OLS sobre las variables codificadas
/// (port 1:1 de <c>doe.ts</c>).
/// </summary>
public static class FactorialAnalysis
{
    public static Result<EffectsAnalysis> AnalyzeFactorial(DoeDesign design, double[] response)
    {
        if (response.Length != design.Runs.Length)
        {
            return Result<EffectsAnalysis>.Err(DomainErrorCode.InvalidInput, "La respuesta debe tener una fila por corrida.");
        }

        var k = design.Factors.Length;
        var terms = new List<string>();
        var predictors = new List<double[]>();
        for (var i = 0; i < k; i++)
        {
            terms.Add(design.Factors[i].Name);
            predictors.Add(CodedColumn(design.Runs, i));
        }
        for (var i = 0; i < k - 1; i++)
        {
            for (var j = i + 1; j < k; j++)
            {
                terms.Add($"{design.Factors[i].Name}*{design.Factors[j].Name}");
                predictors.Add(InteractionColumn(design.Runs, i, j));
            }
        }

        // paridad: doe.ts:175,180-183 — la matriz X se indexa con `standardOrder - 1` mientras
        // la respuesta va en orden de corrida; con randomize=true queda desalineada a propósito.
        var designMatrix = BuildStandardOrderDesign(design.Runs, predictors);
        var fit = LinearAlgebra.LeastSquares(designMatrix, response);
        if (!fit.IsOk)
        {
            return Result<EffectsAnalysis>.Err(fit.Error.Code, fit.Error.Message);
        }

        var n = response.Length;
        var p = terms.Count + 1;
        var xt = new double[design.Runs.Length][];
        for (var row = 0; row < design.Runs.Length; row++)
        {
            var standardIndex = (int)(design.Runs[row].StandardOrder - 1);
            var values = new double[p];
            values[0] = 1;
            for (var column = 0; column < predictors.Count; column++)
            {
                var predictor = predictors[column];
                values[column + 1] = standardIndex >= 0 && standardIndex < predictor.Length
                    ? predictor[standardIndex]
                    : double.NaN;
            }
            xt[row] = values;
        }

        var xtx = new double[p][];
        for (var i = 0; i < p; i++)
        {
            xtx[i] = new double[p];
            for (var j = 0; j < p; j++)
            {
                var acc = 0.0;
                for (var row = 0; row < xt.Length; row++) acc += xt[row][i] * xt[row][j];
                xtx[i][j] = acc;
            }
        }
        var xtxInverse = Regression.InvertSmall(xtx);
        var mse = Math.Pow(fit.Value.StandardError, 2);
        double dfError = Math.Max(1, n - p);
        var coefficients = fit.Value.Coefficients;

        var standardErrors = new double[coefficients.Length];
        var t = new double[coefficients.Length];
        var pValues = new double[coefficients.Length];
        for (var index = 0; index < coefficients.Length; index++)
        {
            var diagonal = xtxInverse is not null && index < xtxInverse.Length
                ? xtxInverse[index][index]
                : double.NaN;
            standardErrors[index] = Math.Sqrt(mse * diagonal);
            t[index] = coefficients[index] / standardErrors[index];
            var cdf = TCdfLocal(t[index], dfError);
            pValues[index] = 2 * Math.Min(cdf, 1 - cdf);
        }

        var effects = new double[coefficients.Length - 1];
        for (var index = 0; index < effects.Length; index++) effects[index] = coefficients[index + 1] * 2;

        var allTerms = new string[terms.Count + 1];
        allTerms[0] = "Constante";
        for (var index = 0; index < terms.Count; index++) allTerms[index + 1] = terms[index];

        return Result<EffectsAnalysis>.Ok(new EffectsAnalysis(
            allTerms,
            coefficients,
            standardErrors,
            t,
            pValues,
            effects,
            fit.Value.RSquared,
            fit.Value.Fitted,
            fit.Value.Residuals));
    }

    private static double[] CodedColumn(DesignRun[] runs, int factor)
    {
        var column = new double[runs.Length];
        for (var row = 0; row < runs.Length; row++)
        {
            var coded = runs[row].Coded;
            column[row] = factor < coded.Length ? coded[factor] : double.NaN;
        }
        return column;
    }

    private static double[] InteractionColumn(DesignRun[] runs, int left, int right)
    {
        var column = new double[runs.Length];
        for (var row = 0; row < runs.Length; row++)
        {
            var coded = runs[row].Coded;
            var first = left < coded.Length ? coded[left] : double.NaN;
            var second = right < coded.Length ? coded[right] : double.NaN;
            column[row] = first * second;
        }
        return column;
    }

    /// <summary>Matriz de diseño indexada por orden estándar (el defecto del motor, reproducido).</summary>
    private static double[][] BuildStandardOrderDesign(DesignRun[] runs, List<double[]> predictors)
    {
        var matrix = new double[runs.Length][];
        for (var row = 0; row < runs.Length; row++)
        {
            var standardIndex = (int)(runs[row].StandardOrder - 1);
            var values = new double[predictors.Count];
            for (var column = 0; column < predictors.Count; column++)
            {
                var predictor = predictors[column];
                values[column] = standardIndex >= 0 && standardIndex < predictor.Length
                    ? predictor[standardIndex]
                    : double.NaN;
            }
            matrix[row] = values;
        }
        return matrix;
    }

    /// <summary>CDF de t con la rama de aproximación normal para df &gt; 300 de <c>doe.ts</c>.</summary>
    private static double TCdfLocal(double x, double df)
    {
        if (df <= 0) return double.NaN;
        if (df > 300) return 0.5 * (1 + ErfLocal(x / Math.Sqrt(2)));
        var tail = 0.5 * BetaiLocal(df / 2, 0.5, df / (df + x * x));
        return x >= 0 ? 1 - tail : tail;
    }

    private static double BetaiLocal(double a, double b, double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        var logBeta = Lgamma(a + b) - Lgamma(a) - Lgamma(b) + a * Math.Log(x) + b * Math.Log(1 - x);
        var front = Math.Exp(logBeta);
        if (x < (a + 1) / (a + b + 2)) return front * ContinuedFraction(a, b, x) / a;
        return 1 - front * ContinuedFraction(b, a, 1 - x) / b;
    }

    private static double ContinuedFraction(double a, double b, double x)
    {
        var qab = a + b;
        var qap = a + 1;
        var qam = a - 1;
        var c = 1.0;
        var d = 1 - qab * x / qap;
        if (Math.Abs(d) < 1e-300) d = 1e-300;
        d = 1 / d;
        var h = d;
        for (var m = 1; m < 300; m++)
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

    private static double Lgamma(double x)
    {
        if (x < 0.5) return Math.Log(Math.PI / Math.Sin(Math.PI * x)) - Lgamma(1 - x);
        var z = x - 1;
        var a = LanczosCoefficients[0];
        var t = z + 7.5;
        for (var i = 1; i < LanczosCoefficients.Length; i++) a += LanczosCoefficients[i] / (z + i);
        return 0.5 * Math.Log(2 * Math.PI) + (z + 0.5) * Math.Log(t) - t + Math.Log(a);
    }

    private static double ErfLocal(double x)
    {
        var sign = x < 0 ? -1 : 1;
        var ax = Math.Abs(x);
        var term = ax;
        var total = ax;
        for (var n = 1; n < 60; n++)
        {
            term *= -ax * ax / n;
            total += term / (2 * n + 1);
            if (Math.Abs(term) < 1e-17) break;
        }
        return sign * 2 * total / Math.Sqrt(Math.PI);
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
