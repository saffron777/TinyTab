namespace StatisticEngine.Domain.Quality;

/// <summary>
/// Constantes de cartas de control para un tamaño de subgrupo.
/// Paridad con el objeto <c>CHART_CONSTANTS[n]</c> de <c>spc.ts:33-48</c>
/// (<c>D3Chart</c> representa la constante <c>D3</c> de la carta R, que convive con
/// la constante <c>d3</c> de la desviación estándar).
/// </summary>
internal sealed record ChartConstant(
    double D2,
    double D3,
    double A2,
    double D3Chart,
    double D4,
    double B3,
    double B4);

/// <summary>Tabla y resolución de constantes (paridad: <c>spc.ts:33-56</c>).</summary>
internal static class ChartConstants
{
    private static readonly int[] Sizes = [2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 15, 20, 25];

    private static readonly Dictionary<int, ChartConstant> BySize = new()
    {
        [2] = new ChartConstant(1.128, 0.853, 1.88, 0, 3.267, 0, 3.267),
        [3] = new ChartConstant(1.693, 0.888, 1.023, 0, 2.574, 0, 2.568),
        [4] = new ChartConstant(2.059, 0.88, 0.729, 0, 2.282, 0, 2.266),
        [5] = new ChartConstant(2.326, 0.864, 0.577, 0, 2.114, 0, 2.089),
        [6] = new ChartConstant(2.534, 0.848, 0.483, 0, 2.004, 0.03, 1.97),
        [7] = new ChartConstant(2.704, 0.833, 0.419, 0.076, 1.924, 0.118, 1.882),
        [8] = new ChartConstant(2.847, 0.82, 0.373, 0.136, 1.864, 0.185, 1.815),
        [9] = new ChartConstant(2.97, 0.808, 0.337, 0.184, 1.816, 0.239, 1.761),
        [10] = new ChartConstant(3.078, 0.797, 0.308, 0.223, 1.777, 0.284, 1.716),
        [11] = new ChartConstant(3.173, 0.787, 0.285, 0.256, 1.744, 0.321, 1.679),
        [12] = new ChartConstant(3.258, 0.778, 0.266, 0.283, 1.717, 0.354, 1.646),
        [15] = new ChartConstant(3.472, 0.755, 0.223, 0.347, 1.653, 0.428, 1.572),
        [20] = new ChartConstant(3.735, 0.729, 0.18, 0.411, 1.585, 0.51, 1.49),
        [25] = new ChartConstant(3.931, 0.708, 0.153, 0.459, 1.541, 0.565, 1.435)
    };

    /// <summary>
    /// Constante exacta si el tamaño está tabulado; si no, la del mayor tamaño tabulado
    /// menor o igual (y 2 como último recurso), igual que <c>constantsFor</c>.
    /// </summary>
    public static ChartConstant For(int n)
    {
        if (BySize.TryGetValue(n, out var exact)) return exact;
        var lower = 2;
        foreach (var size in Sizes)
        {
            if (size <= n) lower = size;
        }
        return BySize[lower];
    }
}
