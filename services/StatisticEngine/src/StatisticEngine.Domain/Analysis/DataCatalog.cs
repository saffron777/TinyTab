using StatisticEngine.Domain.Data.Operations;

namespace StatisticEngine.Domain.Analysis;

/// <summary>
/// Catálogo de operaciones de datos (transformaciones, filas, estructura, faltantes y atípicos).
/// Se publican en <c>/api/v1/data</c>, no en <c>/api/v1/analyses</c>: reciben y devuelven frames
/// tipados y pueden ofrecer datasets de salida.
/// </summary>
public static class DataCatalog
{
    private static readonly AnalysisDefinition Standardize = new(
        "data-standardize",
        "Tipificar y normalizar",
        "Datos",
        "Estandariza o reescala columnas numéricas (z, [0,1], robusto, centrado) y muestra los parámetros usados.",
        [],
        [],
        [
            new AnalysisParamDefinition(
                "method",
                "Método",
                AnalysisParamKinds.Select,
                "zscore",
                [
                    new AnalysisParamOption("zscore", "Tipificar (z): (x − media) / desv. estándar"),
                    new AnalysisParamOption("minmax", "Escalar a [0, 1]: (x − mín) / (máx − mín)"),
                    new AnalysisParamOption("robust", "Robusto: (x − mediana) / rango intercuartílico"),
                    new AnalysisParamOption("maxabs", "Dividir por el máximo absoluto"),
                    new AnalysisParamOption("center", "Centrar: x − media")
                ],
                false),
            new AnalysisParamDefinition(
                "mode",
                "Resultado",
                AnalysisParamKinds.Select,
                "new",
                [
                    new AnalysisParamOption("new", "Añadir columnas nuevas"),
                    new AnalysisParamOption("replace", "Reemplazar las columnas originales")
                ],
                false)
        ],
        StandardizeOperation.Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Crear y modificar variables",
        Roles: [new ColumnRole("cols", "Columnas numéricas", ColumnKinds.Numeric, Multiple: true)],
        Outputs: [new AnalysisOutputSpec(StandardizeOperation.ResultKey, "Datos transformados", Required: true)],
        NeedsDataFrame: true);

    public static IReadOnlyList<AnalysisDefinition> All { get; } =
    [
        Standardize,
        RowOperations.Sort,
        RowOperations.SelectColumns,
        RowOperations.Duplicates
    ];

    public static AnalysisDefinition? Find(string id) =>
        All.FirstOrDefault(operation => string.Equals(operation.Id, id, StringComparison.Ordinal));
}
