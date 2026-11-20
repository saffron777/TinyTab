using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Análisis de correspondencias simple.</summary>
internal static class CorrespondenceAnalyses
{
    public static readonly AnalysisDefinition Correspondence = new(
        "correspondence",
        "Análisis de correspondencias",
        "Mapas y atípicos",
        "Mapa conjunto de dos variables categóricas a partir de su tabla de contingencia, con inercias, masas y calidad.",
        [],
        [],
        [],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Mapas y atípicos",
        Roles:
        [
            new ColumnRole("row", "Variable de las filas", ColumnKinds.Categorical),
            new ColumnRole("col", "Variable de las columnas", ColumnKinds.Categorical),
            new ColumnRole("count", "Ponderador (opcional)", ColumnKinds.Numeric, Optional: true)
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var rowName = ExtendedHelpers.FirstRole(input, "row", "Variable de filas");
        var colName = ExtendedHelpers.FirstRole(input, "col", "Variable de columnas");
        if (rowName == colName) return ExtendedHelpers.Invalid("Elija dos columnas distintas.");
        var countName = ExtendedHelpers.FirstRole(input, "count", "Ponderador");
        var rows = frame.Texts(rowName);
        var cols = frame.Texts(colName);
        var weights = countName.Length > 0 ? frame.Numeric(countName) : Enumerable.Repeat(1.0, frame.RowCount).ToArray();
        var rowLevels = frame.Levels(rowName);
        var colLevels = frame.Levels(colName);
        if (rowLevels.Length < 3 || colLevels.Length < 3) return ExtendedHelpers.Invalid("Se necesitan al menos 3 categorías en cada variable.");
        var counts = new double[rowLevels.Length, colLevels.Length];
        for (var index = 0; index < frame.RowCount; index++)
        {
            if (double.IsNaN(weights[index]) || rows[index] is null || cols[index] is null) continue;
            var row = Array.IndexOf(rowLevels, rows[index]);
            var col = Array.IndexOf(colLevels, cols[index]);
            if (row >= 0 && col >= 0) counts[row, col] += weights[index];
        }
        var total = 0.0;
        for (var i = 0; i < rowLevels.Length; i++) for (var j = 0; j < colLevels.Length; j++) total += counts[i, j];
        var p = new double[rowLevels.Length][];
        var r = new double[rowLevels.Length];
        var c = new double[colLevels.Length];
        for (var i = 0; i < rowLevels.Length; i++)
        {
            p[i] = new double[colLevels.Length];
            for (var j = 0; j < colLevels.Length; j++) p[i][j] = counts[i, j] / total;
        }
        for (var i = 0; i < rowLevels.Length; i++) r[i] = Enumerable.Range(0, colLevels.Length).Sum(j => p[i][j]);
        for (var j = 0; j < colLevels.Length; j++) c[j] = Enumerable.Range(0, rowLevels.Length).Sum(i => p[i][j]);
        var standardized = new double[rowLevels.Length][];
        for (var i = 0; i < rowLevels.Length; i++)
        {
            standardized[i] = new double[colLevels.Length];
            for (var j = 0; j < colLevels.Length; j++) standardized[i][j] = (p[i][j] - r[i] * c[j]) / Math.Sqrt(r[i] * c[j]);
        }
        var (u, s, v) = Svd(standardized);
        var dims = Math.Min(rowLevels.Length, colLevels.Length) - 1;
        var rowCoordinates = new double[rowLevels.Length][];
        var colCoordinates = new double[colLevels.Length][];
        for (var i = 0; i < rowLevels.Length; i++) rowCoordinates[i] = Enumerable.Range(0, dims).Select(d => u[i][d] * s[d] / Math.Sqrt(r[i])).ToArray();
        for (var j = 0; j < colLevels.Length; j++) colCoordinates[j] = Enumerable.Range(0, dims).Select(d => v[j][d] * s[d] / Math.Sqrt(c[j])).ToArray();
        var inertia = s.Take(dims).Select(value => value * value).ToArray();
        var rowInertia = Enumerable.Range(0, rowLevels.Length).Select(i => r[i] * Enumerable.Range(0, colLevels.Length).Sum(j => Math.Pow(p[i][j] / r[i] - c[j], 2) / c[j])).ToArray();
        var colInertia = Enumerable.Range(0, colLevels.Length).Select(j => c[j] * Enumerable.Range(0, rowLevels.Length).Sum(i => Math.Pow(p[i][j] / c[j] - r[i], 2) / r[i])).ToArray();
        var chi2 = total * inertia.Sum();
        var degrees = (rowLevels.Length - 1) * (colLevels.Length - 1);
        var pValue = ExtendedStatistics.ChiSquareSf(chi2, degrees);
        var shown = Math.Min(dims, 2);
        var rowQuality = Enumerable.Range(0, rowLevels.Length).Select(i => 100 * Enumerable.Range(0, shown).Sum(d => rowCoordinates[i][d] * rowCoordinates[i][d]) / Math.Max(Enumerable.Range(0, dims).Sum(d => rowCoordinates[i][d] * rowCoordinates[i][d]), 1e-12)).ToArray();
        var colQuality = Enumerable.Range(0, colLevels.Length).Select(j => 100 * Enumerable.Range(0, shown).Sum(d => colCoordinates[j][d] * colCoordinates[j][d]) / Math.Max(Enumerable.Range(0, dims).Sum(d => colCoordinates[j][d] * colCoordinates[j][d]), 1e-12)).ToArray();
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Inercia total", ModelHelpers.N(inertia.Sum())),
                new TileItem("χ² de independencia", ModelHelpers.N(chi2)),
                new TileItem("Valor p", ModelHelpers.P(pValue)),
                new TileItem("Dimensiones", JsNumber.ToString(dims))
            ]),
            new TableSection("Inercia por dimensión", ["Dimensión", "Valor singular", "Inercia", "% de la inercia", "% acumulado", "χ²"],
                Enumerable.Range(0, dims).Select(d => (IReadOnlyList<string>)new[]
                {
                    JsNumber.ToString(d + 1), ModelHelpers.N(Math.Sqrt(inertia[d])), ModelHelpers.N(inertia[d]),
                    ModelHelpers.N(100 * inertia[d] / inertia.Sum()), ModelHelpers.N(100 * inertia.Take(d + 1).Sum() / inertia.Sum()), ModelHelpers.N(total * inertia[d])
                }).ToArray(),
                Note: "Inercia total = χ² / n: cuánto se apartan las filas del perfil promedio."),
            new TableSection($"Coordenadas de las filas ({rowName})", ["Categoría", "Masa", "Inercia", .. Enumerable.Range(0, shown).Select(d => $"Dim {d + 1}"), "Calidad (%)"],
                Enumerable.Range(0, rowLevels.Length).Select(i => (IReadOnlyList<string>)[rowLevels[i], ModelHelpers.N(r[i]), ModelHelpers.N(rowInertia[i]), .. Enumerable.Range(0, shown).Select(d => ModelHelpers.N(rowCoordinates[i][d])), ModelHelpers.N(rowQuality[i])]).ToArray(),
                Note: "Calidad: porcentaje de la distancia al centro que se ve en el mapa."),
            new TableSection($"Coordenadas de las columnas ({colName})", ["Categoría", "Masa", "Inercia", .. Enumerable.Range(0, shown).Select(d => $"Dim {d + 1}"), "Calidad (%)"],
                Enumerable.Range(0, colLevels.Length).Select(j => (IReadOnlyList<string>)[colLevels[j], ModelHelpers.N(c[j]), ModelHelpers.N(colInertia[j]), .. Enumerable.Range(0, shown).Select(d => ModelHelpers.N(colCoordinates[j][d])), ModelHelpers.N(colQuality[j])]).ToArray())
        };
        if (shown >= 2)
        {
            var share = inertia.Select(value => 100 * value / inertia.Sum()).ToArray();
            sections.Add(new TableSection("Tabla de contingencia", [rowName, .. colLevels], Enumerable.Range(0, rowLevels.Length).Select(i => (IReadOnlyList<string>)[rowLevels[i], .. Enumerable.Range(0, colLevels.Length).Select(j => JsNumber.ToString(counts[i, j]))]).ToArray()));
        }
        return Result.Ok(new AnalysisResult(
            "correspondence",
            $"Análisis de correspondencias: {rowName} y {colName}",
            sections,
            Subtitle: $"{rowLevels.Length} × {colLevels.Length} categorías · n = {JsNumber.ToString(total)}",
            Charts: [new ScatterChart("Mapa simétrico de correspondencias", Enumerable.Range(0, rowLevels.Length).Select(i => new ScatterPoint(rowCoordinates[i][0], rowCoordinates[i][1])).ToList(), "Dimensión 1", "Dimensión 2")],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["inertia"] = inertia,
                ["total_inertia"] = inertia.Sum(),
                ["chi2"] = chi2,
                ["row_coordinates"] = rowCoordinates,
                ["column_coordinates"] = colCoordinates
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static (double[][] U, double[] S, double[][] V) Svd(double[][] matrix)
    {
        var rows = matrix.Length;
        var cols = matrix[0].Length;
        var gram = new double[rows][];
        for (var i = 0; i < rows; i++)
        {
            gram[i] = new double[rows];
            for (var j = 0; j < rows; j++) gram[i][j] = Enumerable.Range(0, cols).Sum(k => matrix[i][k] * matrix[j][k]);
        }
        var eigen = LinearAlgebra.EigenSymmetric(gram);
        var order = Enumerable.Range(0, rows).OrderByDescending(index => eigen.Values[index]).ToArray();
        var u = new double[rows][];
        var s = new double[Math.Min(rows, cols)];
        var v = new double[cols][];
        for (var i = 0; i < rows; i++) u[i] = new double[rows];
        for (var j = 0; j < cols; j++) v[j] = new double[rows];
        for (var d = 0; d < s.Length; d++)
        {
            s[d] = Math.Sqrt(Math.Max(eigen.Values[order[d]], 0));
            for (var i = 0; i < rows; i++) u[i][d] = eigen.Vectors[i][order[d]];
            for (var j = 0; j < cols; j++)
            {
                var value = s[d] > 0 ? Enumerable.Range(0, rows).Sum(i => matrix[i][j] * u[i][d]) / s[d] : 0;
                v[j][d] = value;
            }
        }
        for (var i = 0; i < rows; i++) u[i][0] = -u[i][0];
        for (var j = 0; j < cols; j++) v[j][0] = -v[j][0];
        return (u, s, v);
    }
}
