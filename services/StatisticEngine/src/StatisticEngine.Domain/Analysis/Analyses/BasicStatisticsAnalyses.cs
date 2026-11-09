using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Descriptive;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Numerics;
using static StatisticEngine.Domain.Analysis.FormatHelpers;

namespace StatisticEngine.Domain.Analysis.Analyses;

/// <summary>Estadísticas básicas y prueba de normalidad (paridad: <c>registry-basic.ts:14-192</c>).</summary>
public static class BasicStatisticsAnalyses
{
    private static readonly AnalysisDefinition DescribeDefinition = DefinitionFactory.Define(
        "describe",
        "Estadísticas descriptivas",
        "Estadísticas básicas",
        "Media, desviación estándar, cuartiles, intervalo de confianza y pruebas de normalidad.",
        [DefinitionFactory.Numeric("x", "Variables", true, true)],
        [],
        [DefinitionFactory.Number("confidence", "Nivel de confianza", 0.95)],
        input =>
        {
            var confidence = input.Pnum("confidence", 0.95);
            var rows = new List<IReadOnlyList<object?>>();
            foreach (var key in input.NumericKeys)
            {
                var values = input.Num(key);
                var stats = DescriptiveStats.Describe(values);
                var interval = DescriptiveStats.MeanConfidenceInterval(values, confidence);
                rows.Add(
                [
                    key,
                    N(stats.N),
                    N(stats.Mean),
                    N(stats.SeMean),
                    N(stats.Sd),
                    N(stats.Min),
                    N(stats.Q1),
                    N(stats.Median),
                    N(stats.Q3),
                    N(stats.Max),
                    N(stats.Iqr),
                    $"{N(interval.Lower)} – {N(interval.Upper)}"
                ]);
            }

            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "describe",
                "Estadísticas descriptivas",
                Sections(Table(
                    "Resumen",
                    ["Variable", "N", "Media", "EE media", "Desv.Est.", "Mínimo", "Q1", "Mediana", "Q3", "Máximo", "IQR", "IC media"],
                    rows)),
                Subtitle: $"Intervalo de confianza del {JsNumber.ToFixed(confidence * 100, 1)} %"));
        });

    private static readonly AnalysisDefinition GraphicalSummary = DefinitionFactory.Define(
        "graphical-summary",
        "Resumen gráfico",
        "Estadísticas básicas",
        "Resumen descriptivo con histograma, prueba de normalidad e intervalo de confianza.",
        [DefinitionFactory.Numeric("x", "Variable")],
        [],
        [DefinitionFactory.Number("confidence", "Nivel de confianza", 0.95)],
        input =>
        {
            var values = input.Num("x");
            if (values.Length < 3)
            {
                return Result<AnalysisResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos 3 observaciones.");
            }

            var stats = DescriptiveStats.Describe(values);
            var interval = DescriptiveStats.MeanConfidenceInterval(values, input.Pnum("confidence", 0.95));
            var normality = HypothesisTests.AndersonDarling(values);

            AnalysisSection normalitySection = normality.IsOk
                ? Table(
                    "Prueba de normalidad (Anderson-Darling)",
                    ["Estadístico", "Valor p"],
                    [[N(normality.Value.Statistic, 3), P(normality.Value.PValue)]])
                : Text("Normalidad", normality.Error.Message);

            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "graphical-summary",
                "Resumen gráfico",
                Sections(
                    Table(
                        "Estadísticas",
                        ["N", "Media", "Mediana", "Desv.Est.", "Mínimo", "Máximo", "IQR"],
                        [[stats.N, N(stats.Mean), N(stats.Median), N(stats.Sd), N(stats.Min), N(stats.Max), N(stats.Iqr)]]),
                    Table(
                        "Intervalo de confianza para la media",
                        ["Inferior", "Media", "Superior"],
                        [[N(interval.Lower), N(stats.Mean), N(interval.Upper)]]),
                    normalitySection),
                Charts:
                [
                    new HistogramChart("Histograma", values),
                    new BoxPlotChart("Diagrama de caja", [new BoxPlotGroup("Datos", values)])
                ]));
        });

    private static readonly AnalysisDefinition CorrelationDefinition = DefinitionFactory.Define(
        "correlation",
        "Correlación",
        "Estadísticas básicas",
        "Matriz de correlaciones de Pearson entre dos o más variables.",
        [DefinitionFactory.Numeric("x", "Variables", true, true)],
        [],
        [],
        input =>
        {
            var keys = input.NumericKeys;
            if (keys.Count < 2)
            {
                return Result<AnalysisResult>.Err(DomainErrorCode.InsufficientData, "Seleccione al menos dos variables.");
            }

            var columns = keys.Select(key => (IReadOnlyList<double>)input.Num(key)).ToArray();
            var matrix = DescriptiveStats.CorrelationMatrix(keys, columns);
            var rows = new List<IReadOnlyList<object?>>();
            for (var index = 0; index < matrix.Matrix.Count; index++)
            {
                var row = new List<object?> { keys[index] };
                row.AddRange(matrix.Matrix[index].Select(value => N(value, 4)));
                rows.Add(row);
            }

            var headers = new List<string> { "Variable" };
            headers.AddRange(keys);
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "correlation",
                "Correlación",
                Sections(Table("Matriz de correlaciones", headers, rows))));
        });

    private static readonly AnalysisDefinition CovarianceDefinition = DefinitionFactory.Define(
        "covariance",
        "Covarianza",
        "Estadísticas básicas",
        "Matriz de covarianzas entre dos o más variables.",
        [DefinitionFactory.Numeric("x", "Variables", true, true)],
        [],
        [],
        input =>
        {
            var keys = input.NumericKeys;
            if (keys.Count < 2)
            {
                return Result<AnalysisResult>.Err(DomainErrorCode.InsufficientData, "Seleccione al menos dos variables.");
            }

            var matrix = DescriptiveStats.CovarianceMatrix(keys.Select(key => (IReadOnlyList<double>)input.Num(key)).ToArray());
            var rows = new List<IReadOnlyList<object?>>();
            for (var index = 0; index < matrix.Count; index++)
            {
                var row = new List<object?> { keys[index] };
                row.AddRange(matrix[index].Select(value => N(value, 4)));
                rows.Add(row);
            }

            var headers = new List<string> { "Variable" };
            headers.AddRange(keys);
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "covariance",
                "Covarianza",
                Sections(Table("Matriz de covarianzas", headers, rows))));
        });

    private static readonly AnalysisDefinition Normality = DefinitionFactory.Define(
        "normality",
        "Prueba de normalidad",
        "Estadísticas básicas",
        "Prueba de Anderson-Darling para la normalidad de los datos.",
        [DefinitionFactory.Numeric("x", "Variable")],
        [],
        [],
        input =>
        {
            var result = HypothesisTests.AndersonDarling(input.Num("x"));
            if (result.IsError)
            {
                return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            }

            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "normality",
                "Prueba de normalidad",
                [Table(
                    "Anderson-Darling",
                    ["Estadístico", "Valor p", "N"],
                    [[N(value.Statistic, 3), P(value.PValue), JsNumber.ToString(value.N)]])]));
        });

    public static IReadOnlyList<AnalysisDefinition> All { get; } =
    [
        DescribeDefinition,
        GraphicalSummary,
        CorrelationDefinition,
        CovarianceDefinition,
        Normality
    ];
}
