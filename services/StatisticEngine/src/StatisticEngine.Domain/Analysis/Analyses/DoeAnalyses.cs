using StatisticEngine.Domain.Doe;
using StatisticEngine.Domain.Numerics;
using static StatisticEngine.Domain.Analysis.FormatHelpers;

namespace StatisticEngine.Domain.Analysis.Analyses;

/// <summary>Análisis factorial de efectos (paridad: <c>registry-advanced.ts:232-264</c>).</summary>
public static class DoeAnalyses
{
    private static readonly AnalysisDefinition Analyze = DefinitionFactory.Define(
        "doe-analyze",
        "Analizar diseño factorial (efectos)",
        "DOE",
        "Ajusta un modelo con factores codificados (−1/+1) y sus interacciones de 2 vías.",
        [
            DefinitionFactory.Numeric("y", "Respuesta"),
            DefinitionFactory.Numeric("x", "Factores codificados", true, true)
        ],
        [],
        [],
        input =>
        {
            var keys = input.NumericKeys;
            var response = keys.Count > 0 ? input.Num(keys[0]) : [];
            var codedColumns = keys.Skip(1).Select(key => input.Num(key)).ToArray();
            if (codedColumns.Length < 1)
            {
                return Result<AnalysisResult>.Err(DomainErrorCode.InvalidInput, "Seleccione al menos un factor codificado.");
            }

            var names = keys.Skip(1).ToArray();
            var factors = names.Select(name => new FactorDef(name, -1, 1)).ToArray();
            var runs = new DesignRun[response.Length];
            for (var index = 0; index < response.Length; index++)
            {
                var coded = new double[codedColumns.Length];
                for (var column = 0; column < codedColumns.Length; column++)
                {
                    coded[column] = index < codedColumns[column].Length ? codedColumns[column][index] : double.NaN;
                }
                runs[index] = new DesignRun(index + 1, index + 1, coded, (double[])coded.Clone());
            }

            var design = new DoeDesign("factorial", factors, runs, 1, 0, 20261023);
            var result = FactorialAnalysis.AnalyzeFactorial(design, response);
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;

            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "doe-analyze",
                "Análisis factorial (efectos)",
                Sections(
                    Table(
                        "Efectos estimados",
                        ["Término", "Efecto", "Coef.", "EE", "t", "Valor p"],
                        value.Terms.Select((term, index) => (IReadOnlyList<object?>)
                        [
                            term,
                            index == 0 ? string.Empty : N(index - 1 < value.Effects.Length ? value.Effects[index - 1] : double.NaN, 4),
                            N(value.Coefficients[index], 4),
                            N(value.StandardErrors[index], 4),
                            N(value.T[index], 2),
                            P(value.PValues[index])
                        ])),
                    Text("R²", $"R² = {N(value.RSquared, 4)}"))));
        });

    public static IReadOnlyList<AnalysisDefinition> All { get; } = [Analyze];
}
