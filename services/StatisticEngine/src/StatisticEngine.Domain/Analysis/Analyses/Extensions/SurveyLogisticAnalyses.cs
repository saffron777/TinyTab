using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Regresión logística con pesos muestrales (port de <c>survey_regression._survey_logistic</c>).</summary>
internal static class SurveyLogisticAnalyses
{
    public static readonly AnalysisDefinition SurveyLogistic = new(
        "survey-logistic",
        "Regresión logística de encuestas (SURVEYLOGISTIC)",
        "Encuestas",
        "Regresión logística ponderada con pesos muestrales para una respuesta binaria.",
        [],
        [],
        [ModelHelpers.Alpha()],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Regresión de encuestas",
        Roles:
        [
            new ColumnRole("response", "Respuesta (Y, binaria)", ColumnKinds.Categorical),
            new ColumnRole("predictors", "Predictores (números y categorías)", ColumnKinds.Any, Multiple: true),
            new ColumnRole("weight", "Peso muestral (opcional)", ColumnKinds.Numeric, Optional: true)
        ],
        Outputs: [new AnalysisOutputSpec("predicciones", "Probabilidad estimada y residuos")]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var response = ExtendedHelpers.FirstRole(input, "response", "Respuesta (Y, binaria)");
        var predictors = input.RoleNames("predictors", true);
        var weightColumn = ExtendedHelpers.FirstRole(input, "weight", "Peso muestral");
        var alpha = input.Pnum("alpha", 0.05);
        var levels = frame.Levels(response);
        if (levels.Length != 2)
        {
            return ExtendedHelpers.Invalid($"«{response}» debe tener exactamente 2 valores distintos (tiene {levels.Length}).");
        }
        var eventLabel = levels[1];
        var otherLabel = levels[0];
        var labels = frame.Texts(response);
        var encoded = labels.Select(label => label == eventLabel ? 1.0 : 0.0).ToArray();
        var working = frame.With(DataColumn.Numeric(response, encoded));
        var extra = string.IsNullOrEmpty(weightColumn)
            ? null
            : new Dictionary<string, string>(StringComparer.Ordinal) { ["weights"] = weightColumn };
        var designResult = DesignBuilder.Build(working, response, predictors, extra: extra);
        if (designResult.IsError) return Result.Err<AnalysisResult>(designResult.Error.Code, designResult.Error.Message);
        var design = designResult.Value;
        var enough = ModelHelpers.NeedEnough(design, design.Matrix[0].Length);
        if (enough.IsError) return Result.Err<AnalysisResult>(enough.Error.Code, enough.Error.Message);
        var n = design.RowCount;
        var y = design.Response;
        var weights = Enumerable.Repeat(1.0, n).ToArray();
        if (extra is not null)
        {
            var token = design.Names.First(pair => pair.Value == weightColumn).Key;
            weights = design.Data.Numeric(token);
            if (weights.Any(value => value <= 0)) return ExtendedHelpers.Invalid("Los pesos deben ser positivos.");
        }

        var fitResult = Fit(design.Matrix, y, weights);
        if (fitResult.IsError) return Result.Err<AnalysisResult>(fitResult.Error.Code, fitResult.Error.Message);
        var fit = fitResult.Value;
        var k = fit.Coefficients.Length;
        var totalWeight = weights.Sum();
        var aic = -2 * fit.LogLikelihood + 2 * k;
        var bic = -2 * fit.LogLikelihood + k * Math.Log(totalWeight);

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("Evento", eventLabel),
                new TileItem("AIC", ModelHelpers.N(aic)),
                new TileItem("BIC", ModelHelpers.N(bic))
            ]),
            new AlertSection(
                "Aviso",
                "info",
                "Regresión logística ponderada: los pesos muestrales ajustan la contribución de cada observación.")
        };
        ModelHelpers.DroppedNote(sections, design);

        var critical = SpecialFunctions.NormalInv(1 - alpha / 2);
        var rows = new List<IReadOnlyList<string>>();
        for (var index = 0; index < k; index++)
        {
            var name = design.ColumnNames[index] == "Intercept" ? "Constante" : design.Pretty(design.ColumnNames[index]);
            var coefficient = fit.Coefficients[index];
            var error = fit.StandardErrors[index];
            var low = coefficient - critical * error;
            var high = coefficient + critical * error;
            rows.Add([
                name,
                ModelHelpers.N(coefficient),
                ModelHelpers.N(error),
                ModelHelpers.N(coefficient / error),
                ModelHelpers.P(2 * (1 - SpecialFunctions.NormalCdf(Math.Abs(coefficient / error)))),
                ModelHelpers.N(low),
                ModelHelpers.N(high),
                ModelHelpers.N(Math.Exp(coefficient)),
                ModelHelpers.N(Math.Exp(low)),
                ModelHelpers.N(Math.Exp(high))
            ]);
        }
        sections.Add(new TableSection(
            "Coeficientes",
            [
                "Término", "Coeficiente", "Error est.", "z", "Valor p",
                .. ModelHelpers.CiColumns(alpha), "Odds ratio",
                $"IC {SigmaText.General(100 * (1 - alpha))} % inf. (Odds ratio)", "IC sup. (Odds ratio)"
            ],
            rows));

        var predictions = ModelHelpers.Predictions(
            frame,
            design,
            ("probabilidad", fit.Fitted),
            ("residuo", y.Select((value, index) => value - fit.Fitted[index]).ToArray()));
        predictions = predictions with { Label = "Probabilidad estimada y residuos" };

        return Result.Ok(new AnalysisResult(
            "survey-logistic",
            "Regresión logística con pesos muestrales",
            sections,
            Subtitle: $"n = {n} · evento «{eventLabel}»",
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal) { ["predicciones"] = predictions },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["aic"] = aic,
                ["bic"] = bic
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private sealed record WeightedFit(double[] Coefficients, double[] StandardErrors, double[] Fitted, double LogLikelihood);

    private static Result<WeightedFit> Fit(double[][] x, double[] y, double[] weights)
    {
        var n = x.Length;
        var p = x[0].Length;
        var beta = new double[p];
        var mu = new double[n];
        for (var iteration = 0; iteration < 200; iteration++)
        {
            for (var row = 0; row < n; row++)
            {
                var eta = 0.0;
                for (var column = 0; column < p; column++) eta += x[row][column] * beta[column];
                mu[row] = 1 / (1 + Math.Exp(-eta));
            }
            var xtwx = new double[p][];
            var xtwz = new double[p];
            for (var i = 0; i < p; i++)
            {
                xtwx[i] = new double[p];
                for (var j = 0; j < p; j++)
                {
                    var sum = 0.0;
                    for (var row = 0; row < n; row++) sum += weights[row] * mu[row] * (1 - mu[row]) * x[row][i] * x[row][j];
                    xtwx[i][j] = sum;
                }
                var sumZ = 0.0;
                for (var row = 0; row < n; row++)
                {
                    var eta = 0.0;
                    for (var column = 0; column < p; column++) eta += x[row][column] * beta[column];
                    var z = eta + (y[row] - mu[row]) / Math.Max(mu[row] * (1 - mu[row]), 1e-12);
                    sumZ += weights[row] * mu[row] * (1 - mu[row]) * x[row][i] * z;
                }
                xtwz[i] = sumZ;
            }
            var solved = LinearAlgebra.SolveLinear(xtwx, xtwz);
            if (solved.IsError) return Result.Err<WeightedFit>(solved.Error.Code, solved.Error.Message);
            var change = solved.Value.Select((value, index) => Math.Abs(value - beta[index])).Max();
            beta = solved.Value;
            if (change < 1e-10) break;
            if (iteration == 199) return Result.Err<WeightedFit>(DomainErrorCode.NonConvergence, "La regresión logística ponderada no convergió.");
        }

        var finalMu = new double[n];
        var logLikelihood = 0.0;
        for (var row = 0; row < n; row++)
        {
            var eta = 0.0;
            for (var column = 0; column < p; column++) eta += x[row][column] * beta[column];
            finalMu[row] = 1 / (1 + Math.Exp(-eta));
            logLikelihood += weights[row] * (y[row] * Math.Log(Math.Max(finalMu[row], 1e-12))
                               + (1 - y[row]) * Math.Log(Math.Max(1 - finalMu[row], 1e-12)));
        }
        var xtwxFinal = new double[p][];
        for (var i = 0; i < p; i++)
        {
            xtwxFinal[i] = new double[p];
            for (var j = 0; j < p; j++)
            {
                var sum = 0.0;
                for (var row = 0; row < n; row++) sum += weights[row] * finalMu[row] * (1 - finalMu[row]) * x[row][i] * x[row][j];
                xtwxFinal[i][j] = sum;
            }
        }
        var inverse = LinearAlgebra.Inverse(xtwxFinal);
        if (inverse.IsError) return Result.Err<WeightedFit>(inverse.Error.Code, inverse.Error.Message);
        var standardErrors = Enumerable.Range(0, p).Select(index => Math.Sqrt(Math.Max(0, inverse.Value[index][index]))).ToArray();
        return Result.Ok(new WeightedFit(beta, standardErrors, finalMu, logLikelihood));
    }
}
