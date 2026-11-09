namespace StatisticEngine.Domain.Multivariate;

/// <summary>
/// Rotación varimax de una matriz de cargas (paridad: <c>multivariate.ts:68-112</c>):
/// 100 iteraciones como máximo y corte de 1e-8 en el ángulo y en el cambio acumulado.
/// </summary>
public static class Varimax
{
    public static double[][] Rotate(double[][] loadings)
    {
        var rows = loadings.Length;
        var cols = rows > 0 ? loadings[0].Length : 0;
        var result = new double[rows][];
        for (var i = 0; i < rows; i++) result[i] = (double[])loadings[i].Clone();

        for (var iteration = 0; iteration < 100; iteration++)
        {
            var h = new double[rows];
            for (var i = 0; i < rows; i++)
            {
                var sum = 0.0;
                for (var j = 0; j < cols; j++) sum += result[i][j] * result[i][j];
                h[i] = sum;
            }

            var b = new double[rows][];
            for (var i = 0; i < rows; i++)
            {
                b[i] = new double[cols];
                // paridad: multivariate.ts:79 — `h[i] || 1`.
                var divisor = h[i];
                if (divisor == 0 || double.IsNaN(divisor)) divisor = 1;
                for (var j = 0; j < cols; j++) b[i][j] = Math.Pow(result[i][j] / divisor, 3);
            }

            var changed = 0.0;
            for (var a = 0; a < cols - 1; a++)
            {
                for (var c = a + 1; c < cols; c++)
                {
                    var num = 0.0;
                    var den = 0.0;
                    for (var i = 0; i < rows; i++)
                    {
                        var ba = b[i][a];
                        var bc = b[i][c];
                        var ia = result[i][a];
                        var ic = result[i][c];
                        var diff = ia * ia - ic * ic;
                        num += 2 * ba * bc - diff * 2 * ia * ic;
                        den += ba * ba - bc * bc + diff;
                    }
                    var angle = Math.Atan2(num, den) / 4;
                    if (Math.Abs(angle) < 1e-8) continue;
                    changed += Math.Abs(angle);
                    var cos = Math.Cos(angle);
                    var sin = Math.Sin(angle);
                    for (var i = 0; i < rows; i++)
                    {
                        var ia = result[i][a];
                        var ic = result[i][c];
                        result[i][a] = cos * ia - sin * ic;
                        result[i][c] = sin * ia + cos * ic;
                    }
                }
            }
            if (changed < 1e-8) break;
        }
        return result;
    }
}
