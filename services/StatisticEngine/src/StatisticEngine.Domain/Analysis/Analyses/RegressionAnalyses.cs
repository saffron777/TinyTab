using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using static StatisticEngine.Domain.Analysis.FormatHelpers;

namespace StatisticEngine.Domain.Analysis.Analyses;

/// <summary>Regresión múltiple, stepwise, subconjuntos, logística y Poisson (paridad: <c>registry-basic.ts:595-703</c>).</summary>
public static class RegressionAnalyses
{
    private static Result<AnalysisResult> RegressionOutput(
        string id,
        string title,
        Result<RegressionResult> result,
        string? extra = null)
    {
        if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
        var value = result.Value;

        var coefficients = value.Coefficients.Select((coefficient, index) => (IReadOnlyList<object?>)
        [
            coefficient.Term,
            N(coefficient.Estimate, 5),
            N(coefficient.StandardError, 4),
            N(coefficient.T, 3),
            P(coefficient.PValue),
            index == 0 ? string.Empty : N(index - 1 < value.Vif.Length ? value.Vif[index - 1] : double.NaN, 3)
        ]);

        return Result<AnalysisResult>.Ok(new AnalysisResult(
            id,
            title,
            Sections(
                Table("Coeficientes", ["Término", "Coef.", "EE coef.", "t", "Valor p", "VIF"], coefficients),
                Table(
                    "Resumen del modelo",
                    ["R²", "R² ajustado", "Error estándar", "Observaciones", "GL error", "F", "Valor p", "Durbin-Watson"],
                    [[
                        N(value.RSquared, 4),
                        N(value.AdjustedRSquared, 4),
                        N(value.StandardError, 4),
                        JsNumber.ToString(value.Observations),
                        JsNumber.ToString(value.DfError),
                        N(value.F, 3),
                        P(value.PValue),
                        N(value.DurbinWatson, 3)
                    ]]),
                extra is null ? null : Text("Nota", extra)),
            Charts:
            [
                new ResidualsChart(
                    "Residuos",
                    value.Fitted,
                    value.Residuals,
                    Enumerable.Range(1, value.Residuals.Length).Select(index => (double)index).ToArray())
            ]));
    }

    private static (RegressionInput Input, string[] Keys) BuildRegressionInput(AnalysisInput input)
    {
        var keys = input.NumericKeys;
        var response = keys.Count > 0 ? input.Num(keys[0]) : [];
        var predictors = keys.Skip(1).Select(key => input.Num(key)).ToArray();
        var names = keys.Skip(1).ToArray();
        return (new RegressionInput(response, predictors, names), [.. keys]);
    }

    private static readonly AnalysisDefinition Multiple = DefinitionFactory.Define(
        "regression-multiple",
        "Regresión múltiple",
        "Regresión",
        "Ajuste por mínimos cuadrados (QR) con diagnóstico de residuos y VIF.",
        [
            DefinitionFactory.Numeric("y", "Respuesta"),
            DefinitionFactory.Numeric("x", "Predictores", true, true)
        ],
        [],
        [],
        input =>
        {
            var (regressionInput, _) = BuildRegressionInput(input);
            return RegressionOutput("regression-multiple", "Regresión múltiple", Models.Regression.MultipleRegression(regressionInput));
        });

    private static readonly AnalysisDefinition Stepwise = DefinitionFactory.Define(
        "regression-stepwise",
        "Regresión stepwise",
        "Regresión",
        "Selección de predictores por AIC (forward/backward).",
        [
            DefinitionFactory.Numeric("y", "Respuesta"),
            DefinitionFactory.Numeric("x", "Predictores", true, true)
        ],
        [],
        [
            DefinitionFactory.Select(
                "direction",
                "Dirección",
                "forward",
                DefinitionFactory.Option("forward", "Forward"),
                DefinitionFactory.Option("backward", "Backward"))
        ],
        input =>
        {
            var (regressionInput, _) = BuildRegressionInput(input);
            var result = Models.Stepwise.StepwiseRegression(regressionInput, input.Pstr("direction", "forward"));
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            return RegressionOutput(
                "regression-stepwise",
                "Regresión stepwise",
                Result<RegressionResult>.Ok(result.Value.Model));
        });

    private static readonly AnalysisDefinition BestSubsetsDefinition = DefinitionFactory.Define(
        "regression-best-subsets",
        "Mejores subconjuntos",
        "Regresión",
        "Modelos con mayor R² ajustado hasta 4 predictores.",
        [
            DefinitionFactory.Numeric("y", "Respuesta"),
            DefinitionFactory.Numeric("x", "Predictores", true, true)
        ],
        [],
        [],
        input =>
        {
            var (regressionInput, _) = BuildRegressionInput(input);
            var result = BestSubsetSearch.BestSubsets(regressionInput, 4);
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "regression-best-subsets",
                "Mejores subconjuntos",
                [Table(
                    "Modelos (ordenados por R² ajustado)",
                    ["Términos", "R²", "R² ajustado", "AIC", "Cp Mallows"],
                    result.Value.Select(model => (IReadOnlyList<object?>)
                    [
                        string.Join(", ", model.Names),
                        N(model.RSquared, 4),
                        N(model.AdjustedRSquared, 4),
                        N(model.Aic, 2),
                        N(model.MallowCp, 2)
                    ]))]));
        });

    private static readonly AnalysisDefinition Logistic = DefinitionFactory.Define(
        "regression-logistic",
        "Regresión logística binaria",
        "Regresión",
        "Modelo logit ajustado por IRLS con desviación y AIC.",
        [
            DefinitionFactory.Numeric("y", "Respuesta 0/1"),
            DefinitionFactory.Numeric("x", "Predictores", true, true)
        ],
        [],
        [],
        input =>
        {
            var (regressionInput, _) = BuildRegressionInput(input);
            var result = Models.Logistic.LogisticRegression(
                regressionInput.Response,
                regressionInput.Predictors,
                regressionInput.Names);
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "regression-logistic",
                "Regresión logística binaria",
                Sections(
                    Table(
                        "Coeficientes",
                        ["Término", "Coef.", "EE", "Z", "Valor p", "Odds ratio"],
                        value.Terms.Select((term, index) => (IReadOnlyList<object?>)
                        [
                            term,
                            N(value.Coefficients[index], 5),
                            N(value.StandardErrors[index], 4),
                            N(value.Z[index], 3),
                            P(value.PValues[index]),
                            N(Math.Exp(value.Coefficients[index]), 4)
                        ])),
                    Table(
                        "Bondad de ajuste",
                        ["Desviación", "Desviación nula", "AIC", "Iteraciones"],
                        [[
                            N(value.Deviance, 3),
                            N(value.NullDeviance, 3),
                            N(value.Aic, 3),
                            JsNumber.ToString(value.Iterations)
                        ]]))));
        });

    private static readonly AnalysisDefinition Poisson = DefinitionFactory.Define(
        "regression-poisson",
        "Regresión de Poisson",
        "Regresión",
        "Modelo log-lineal para respuestas de conteo (IRLS).",
        [
            DefinitionFactory.Numeric("y", "Conteo"),
            DefinitionFactory.Numeric("x", "Predictores", true, true)
        ],
        [],
        [],
        input =>
        {
            var (regressionInput, _) = BuildRegressionInput(input);
            var result = Models.Poisson.PoissonRegression(
                regressionInput.Response,
                regressionInput.Predictors,
                regressionInput.Names);
            if (result.IsError) return Result<AnalysisResult>.Err(result.Error.Code, result.Error.Message);
            var value = result.Value;
            return Result<AnalysisResult>.Ok(new AnalysisResult(
                "regression-poisson",
                "Regresión de Poisson",
                Sections(
                    Table(
                        "Coeficientes",
                        ["Término", "Coef.", "EE", "Z", "Valor p"],
                        value.Terms.Select((term, index) => (IReadOnlyList<object?>)
                        [
                            term,
                            N(value.Coefficients[index], 5),
                            N(value.StandardErrors[index], 4),
                            N(value.Z[index], 3),
                            P(value.PValues[index])
                        ])),
                    Table(
                        "Ajuste",
                        ["Desviación", "AIC", "Iteraciones"],
                        [[N(value.Deviance, 3), N(value.Aic, 3), JsNumber.ToString(value.Iterations)]]))));
        });

    public static IReadOnlyList<AnalysisDefinition> All { get; } =
    [
        Multiple,
        Stepwise,
        BestSubsetsDefinition,
        Logistic,
        Poisson
    ];
}
