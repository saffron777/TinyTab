namespace StatisticEngine.Domain.Multivariate;

/// <summary>Resultado del análisis factorial (paridad: <c>multivariate.ts:114-120</c>).</summary>
public sealed record FactorAnalysisResult(
    string[] Labels,
    double[][] Loadings,
    double[] Communalities,
    double[] VarianceExplained,
    double[] Eigenvalues);

/// <summary>
/// Análisis factorial por componentes principales con rotación varimax.
/// Paridad 1:1 con <c>multivariate.ts:122-145</c>.
/// </summary>
public static class FactorAnalysis
{
    public static Result<FactorAnalysisResult> Analyze(double[][] rows, IReadOnlyList<string> labels, int factors = 2)
    {
        var pcaResult = Pca.Analyze(rows, labels);
        if (pcaResult.IsError) return Result<FactorAnalysisResult>.Err(pcaResult.Error.Code, pcaResult.Error.Message);

        var pca = pcaResult.Value;
        var componentCount = pca.Loadings.Length > 0 ? pca.Loadings[0].Length : 1;
        if (factors < 1 || factors > Math.Min(componentCount, 5))
        {
            return Result<FactorAnalysisResult>.Err(DomainErrorCode.InvalidParameter, "Número de factores inválido.");
        }

        var loadings = new double[pca.Loadings.Length][];
        for (var i = 0; i < loadings.Length; i++)
        {
            loadings[i] = new double[factors];
            for (var j = 0; j < factors; j++) loadings[i][j] = pca.Loadings[i][j];
        }
        var rotated = Varimax.Rotate(loadings);

        var communalities = new double[rotated.Length];
        for (var i = 0; i < rotated.Length; i++)
        {
            var sum = 0.0;
            for (var j = 0; j < factors; j++) sum += rotated[i][j] * rotated[i][j];
            communalities[i] = sum;
        }

        var varianceExplained = new double[factors];
        for (var j = 0; j < factors; j++)
        {
            var sum = 0.0;
            for (var i = 0; i < rotated.Length; i++) sum += rotated[i][j] * rotated[i][j];
            varianceExplained[j] = sum / rotated.Length;
        }

        var eigenvalues = new double[factors];
        for (var j = 0; j < factors; j++) eigenvalues[j] = pca.Eigenvalues[j];

        return Result<FactorAnalysisResult>.Ok(new FactorAnalysisResult(
            [.. labels],
            rotated,
            communalities,
            varianceExplained,
            eigenvalues));
    }
}
