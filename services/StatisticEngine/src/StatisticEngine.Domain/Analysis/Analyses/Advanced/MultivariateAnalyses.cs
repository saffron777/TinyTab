using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Multivariate;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses;

/// <summary>
/// Análisis multivariantes del catálogo (port 1:1 de <c>registry-advanced.ts:466-563</c>).
/// </summary>
public static class MultivariateAnalyses
{
    public static IReadOnlyList<AnalysisDefinition> All { get; } =
    [
        new AnalysisDefinition(
            "multivariate-pca",
            "Análisis de componentes principales",
            "Multivariante",
            "PCA sobre la matriz de correlaciones con cargas y puntuaciones.",
            [new ColumnRequirement("x", "Variables", true, true)],
            [],
            [],
            RunPca),
        new AnalysisDefinition(
            "multivariate-factor",
            "Análisis factorial",
            "Multivariante",
            "Extracción por componentes principales con rotación varimax.",
            [new ColumnRequirement("x", "Variables", true, true)],
            [],
            [new AnalysisParamDefinition("factors", "Número de factores", AnalysisParamKinds.Integer, 2.0, [], false)],
            RunFactor),
        new AnalysisDefinition(
            "multivariate-kmeans",
            "Análisis de conglomerados K-means",
            "Multivariante",
            "Agrupamiento k-means++ sobre variables estandarizadas.",
            [new ColumnRequirement("x", "Variables", true, true)],
            [],
            [new AnalysisParamDefinition("k", "Número de grupos", AnalysisParamKinds.Integer, 3.0, [], false)],
            RunKMeans),
        new AnalysisDefinition(
            "multivariate-lda",
            "Análisis discriminante (2 grupos)",
            "Multivariante",
            "Función discriminante lineal y exactitud de clasificación.",
            [new ColumnRequirement("x", "Variables", true, true)],
            [new ColumnRequirement("group", "Grupo (2 niveles)", true, false)],
            [],
            RunLda)
    ];

    private static Result<AnalysisResult> RunPca(AnalysisInput input)
    {
        var keys = Keys(input);
        if (keys.Count < 2) return Result<AnalysisResult>.Err(DomainErrorCode.InvalidInput, "Seleccione al menos dos variables.");
        var result = Pca.Analyze(BuildRows(input, keys), keys);
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;

        var componentHeaders = new List<string>(value.Eigenvalues.Length);
        var varianceRows = new List<IReadOnlyList<object?>>(value.Eigenvalues.Length);
        for (var index = 0; index < value.Eigenvalues.Length; index++)
        {
            var name = $"PC{JsNumber.ToString(index + 1)}";
            componentHeaders.Add(name);
            varianceRows.Add([
                name,
                FormatHelpers.N(value.Eigenvalues[index], 4),
                FormatHelpers.N(value.Explained[index], 2),
                FormatHelpers.N(value.Cumulative[index], 2)
            ]);
        }

        var loadingHeaders = new List<string>(value.Eigenvalues.Length + 1) { "Variable" };
        loadingHeaders.AddRange(componentHeaders);
        var loadingRows = new List<IReadOnlyList<object?>>(value.Loadings.Length);
        for (var index = 0; index < value.Loadings.Length; index++)
        {
            var row = new List<object?>(value.Loadings[index].Length + 1) { value.Labels[index] };
            foreach (var loading in value.Loadings[index]) row.Add(FormatHelpers.N(loading, 3));
            loadingRows.Add(row);
        }

        var sections = FormatHelpers.Sections(
            FormatHelpers.Table(
                "Varianza explicada",
                ["Componente", "Eigenvalor", "% explicado", "% acumulado"],
                varianceRows),
            FormatHelpers.Table("Cargas", loadingHeaders, loadingRows));
        return Result.Ok(new AnalysisResult(
            "multivariate-pca",
            "Análisis de componentes principales",
            sections,
            Charts: [new PcaChart("Varianza explicada", value.Explained, value.Cumulative)]));
    }

    private static Result<AnalysisResult> RunFactor(AnalysisInput input)
    {
        var keys = Keys(input);
        var result = FactorAnalysis.Analyze(
            BuildRows(input, keys),
            keys,
            Math.Max(1, (int)Math.Floor(input.Pnum("factors", 2))));
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;

        // paridad: registry-advanced.ts:509 — una cabecera «Factor k» por columna de cargas.
        var headers = new List<string> { "Variable" };
        if (value.Loadings.Length > 0)
        {
            for (var index = 0; index < value.Loadings[0].Length; index++)
            {
                headers.Add($"Factor {JsNumber.ToString(index + 1)}");
            }
        }
        var loadingRows = new List<IReadOnlyList<object?>>(value.Loadings.Length);
        for (var index = 0; index < value.Loadings.Length; index++)
        {
            var row = new List<object?>(value.Loadings[index].Length + 1) { value.Labels[index] };
            foreach (var loading in value.Loadings[index]) row.Add(FormatHelpers.N(loading, 3));
            loadingRows.Add(row);
        }

        var communityRows = new List<IReadOnlyList<object?>>(value.Communalities.Length);
        for (var index = 0; index < value.Communalities.Length; index++)
        {
            communityRows.Add([value.Labels[index], FormatHelpers.N(value.Communalities[index], 4)]);
        }

        var sections = FormatHelpers.Sections(
            FormatHelpers.Table("Cargas rotadas (varimax)", headers, loadingRows),
            FormatHelpers.Table("Comunalidades", ["Variable", "Comunalidad"], communityRows));
        return Result.Ok(new AnalysisResult(
            "multivariate-factor",
            "Análisis factorial",
            sections));
    }

    private static Result<AnalysisResult> RunKMeans(AnalysisInput input)
    {
        var keys = Keys(input);
        var result = KMeans.Cluster(BuildRows(input, keys), Math.Max(2, (int)Math.Floor(input.Pnum("k", 3))));
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;

        // paridad: registry-advanced.ts:533 — sumas intra unidas con " / ".
        var within = string.Join(" / ", value.WithinSS.Select(item => FormatHelpers.N(item, 3)));
        var assignmentRows = new List<IReadOnlyList<object?>>(value.Labels.Length);
        for (var index = 0; index < value.Labels.Length; index++)
        {
            assignmentRows.Add([
                JsNumber.ToString(index + 1),
                JsNumber.ToString(value.Labels[index] + 1)
            ]);
        }

        var sections = FormatHelpers.Sections(
            FormatHelpers.Table(
                "Resumen",
                ["Grupos", "Suma de cuadrados intra", "Total intra"],
                [[JsNumber.ToString(value.K), within, FormatHelpers.N(value.TotalWithinSS, 3)]]),
            FormatHelpers.Table("Asignación", ["Observación", "Grupo"], assignmentRows));
        return Result.Ok(new AnalysisResult(
            "multivariate-kmeans",
            "Análisis de conglomerados K-means",
            sections));
    }

    private static Result<AnalysisResult> RunLda(AnalysisInput input)
    {
        var keys = Keys(input);
        var result = Lda.Fit(BuildRows(input, keys), input.ColText("group"));
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;

        var coefficientRows = new List<IReadOnlyList<object?>>(value.Coefficients.Length);
        for (var index = 0; index < value.Coefficients.Length; index++)
        {
            coefficientRows.Add([keys[index], FormatHelpers.N(value.Coefficients[index], 4)]);
        }

        var sections = FormatHelpers.Sections(
            FormatHelpers.Table("Coeficientes", ["Variable", "Coeficiente"], coefficientRows),
            FormatHelpers.Table(
                "Clasificación",
                ["Exactitud", "N"],
                [[$"{FormatHelpers.N(value.Accuracy * 100, 2)} %", JsNumber.ToString(value.N)]]));
        return Result.Ok(new AnalysisResult(
            "multivariate-lda",
            "Análisis discriminante",
            sections));
    }

    private static List<string> Keys(AnalysisInput input) => [.. input.NumericKeys];

    private static double[][] BuildRows(AnalysisInput input, List<string> keys)
    {
        if (keys.Count == 0) return [];
        var columns = new double[keys.Count][];
        for (var index = 0; index < keys.Count; index++) columns[index] = input.Num(keys[index]);
        var count = columns[0].Length;
        var rows = new double[count][];
        for (var rowIndex = 0; rowIndex < count; rowIndex++)
        {
            var row = new double[columns.Length];
            for (var column = 0; column < columns.Length; column++)
            {
                // paridad: `columns.map(column => column[index])` con filas incompletas => undefined => NaN.
                row[column] = rowIndex < columns[column].Length ? columns[column][rowIndex] : double.NaN;
            }
            rows[rowIndex] = row;
        }
        return rows;
    }
}
