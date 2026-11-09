namespace StatisticEngine.Domain.Analysis;

/// <summary>Constructores abreviados de los metadatos de un análisis.</summary>
internal static class DefinitionFactory
{
    public static ColumnRequirement Numeric(string key, string label, bool required = true, bool multiple = false) =>
        new(key, label, required, multiple);

    public static ColumnRequirement Text(string key, string label, bool required = true) =>
        new(key, label, required, false);

    public static AnalysisParamDefinition Param(
        string key,
        string label,
        string kind,
        object defaultValue,
        bool optional = false,
        params AnalysisParamOption[] options) => new(key, label, kind, defaultValue, options, optional);

    public static AnalysisParamDefinition Number(string key, string label, double defaultValue, bool optional = false) =>
        Param(key, label, AnalysisParamKinds.Number, defaultValue, optional);

    public static AnalysisParamDefinition Integer(string key, string label, double defaultValue, bool optional = false) =>
        Param(key, label, AnalysisParamKinds.Integer, defaultValue, optional);

    public static AnalysisParamDefinition Select(
        string key,
        string label,
        string defaultValue,
        params AnalysisParamOption[] options) =>
        Param(key, label, AnalysisParamKinds.Select, defaultValue, false, options);

    public static AnalysisParamOption Option(string value, string label) => new(value, label);

    public static AnalysisDefinition Define(
        string id,
        string label,
        string category,
        string description,
        ColumnRequirement[] numericColumns,
        ColumnRequirement[] textColumns,
        AnalysisParamDefinition[] parameters,
        Func<AnalysisInput, Result<AnalysisResult>> run) =>
        new(id, label, category, description, numericColumns, textColumns, parameters, run);
}
