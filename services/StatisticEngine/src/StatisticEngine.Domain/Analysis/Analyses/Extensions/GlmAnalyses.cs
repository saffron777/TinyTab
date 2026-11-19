using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Modelo lineal generalizado (port de <c>models._glm</c>).</summary>
internal static class GlmAnalyses
{
    private static readonly AnalysisParamDefinition Family = new(
        "family",
        "Familia",
        AnalysisParamKinds.Select,
        "gamma",
        [
            new("gaussian", "Normal (gaussiana)"),
            new("binomial", "Binomial (0/1 o proporciones)"),
            new("poisson", "Poisson"),
            new("gamma", "Gamma (positiva y asimétrica)"),
            new("inverse_gaussian", "Gaussiana inversa"),
            new("negative_binomial", "Binomial negativa (α fijo)")
        ],
        false);

    private static readonly AnalysisParamDefinition Link = new(
        "link",
        "Liga",
        AnalysisParamKinds.Select,
        "default",
        [
            new("default", "default"),
            new("identity", "identity"),
            new("log", "log"),
            new("logit", "logit"),
            new("probit", "probit"),
            new("cloglog", "cloglog"),
            new("inverse_power", "inverse power"),
            new("inverse_squared", "inverse squared"),
            new("sqrt", "sqrt")
        ],
        false,
        Help: "«default» usa la liga canónica de la familia.");

    private static readonly AnalysisParamDefinition NbAlpha = new(
        "nb_alpha",
        "α de la binomial negativa",
        AnalysisParamKinds.Number,
        1.0,
        [],
        false,
        Minimum: 1e-6,
        VisibleIf: new("family", ["negative_binomial"]));

    private static readonly AnalysisParamDefinition Scale = new(
        "scale",
        "Escala",
        AnalysisParamKinds.Select,
        "model",
        [
            new("model", "Según la familia"),
            new("pearson", "Estimada con χ² de Pearson (cuasi-verosimilitud)")
        ],
        false);

    public static readonly AnalysisDefinition Glm = new(
        "regression-glm",
        "Modelo lineal generalizado (GLM)",
        "Regresión",
        "Elija la familia y la liga: normal, binomial, Poisson, gamma, gaussiana inversa o binomial negativa.",
        [],
        [],
        [Family, Link, NbAlpha, Scale, ModelHelpers.Interactions(), ModelHelpers.Alpha()],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Conteos y GLM",
        Roles:
        [
            new ColumnRole("response", "Respuesta", ColumnKinds.Numeric),
            new ColumnRole("predictors", "Predictores (números y categorías)", ColumnKinds.Any, Multiple: true)
        ],
        Outputs: [new AnalysisOutputSpec(ModelHelpers.PredictionsKey, ModelHelpers.PredictionsLabel)]);

    private static readonly Dictionary<string, string> FamilyLabels = new(StringComparer.Ordinal)
    {
        ["gaussian"] = "Normal (gaussiana)",
        ["binomial"] = "Binomial (0/1 o proporciones)",
        ["poisson"] = "Poisson",
        ["gamma"] = "Gamma (positiva y asimétrica)",
        ["inverse_gaussian"] = "Gaussiana inversa",
        ["negative_binomial"] = "Binomial negativa (α fijo)"
    };

    private static readonly Dictionary<string, string[]> FamilyLinks = new(StringComparer.Ordinal)
    {
        ["gaussian"] = ["identity", "log", "inverse_power"],
        ["binomial"] = ["logit", "probit", "cloglog", "log"],
        ["poisson"] = ["log", "identity", "sqrt"],
        ["gamma"] = ["inverse_power", "log", "identity"],
        ["inverse_gaussian"] = ["inverse_squared", "log", "identity", "inverse_power"],
        ["negative_binomial"] = ["log", "identity", "sqrt"]
    };

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var response = ExtendedHelpers.FirstRole(input, "response", "Respuesta");
        var predictors = input.RoleNames("predictors", true);
        var alpha = input.Pnum("alpha", 0.05);
        var familyName = input.Pstr("family", "gamma");
        var linkName = input.Pstr("link", "default");
        var interactions = input.Pstr("interactions", "none");
        var scaleOption = input.Pstr("scale", "model");

        var designResult = DesignBuilder.Build(frame, response, predictors, interactions: interactions);
        if (designResult.IsError) return Result.Err<AnalysisResult>(designResult.Error.Code, designResult.Error.Message);
        var design = designResult.Value;
        var y = design.Response;
        if (familyName == "binomial" && y.Any(value => value < 0 || value > 1))
        {
            return ExtendedHelpers.Invalid("Con la familia binomial la respuesta debe estar entre 0 y 1.");
        }
        if (familyName is "gamma" or "inverse_gaussian" && y.Any(value => value <= 0))
        {
            return ExtendedHelpers.Invalid("Con esa familia la respuesta debe ser estrictamente positiva.");
        }
        if (familyName is "poisson" or "negative_binomial" && y.Any(value => value < 0))
        {
            return ExtendedHelpers.Invalid("Con esa familia la respuesta no puede ser negativa.");
        }
        if (!FamilyLinks.TryGetValue(familyName, out var allowedLinks))
        {
            return ExtendedHelpers.Invalid("Familia desconocida.");
        }
        var effectiveLink = linkName == "default" ? allowedLinks[0] : linkName;
        if (!allowedLinks.Contains(effectiveLink))
        {
            return ExtendedHelpers.Invalid(
                $"La liga «{effectiveLink}» no es válida para esa familia; use {string.Join(", ", allowedLinks)}.");
        }
        var family = familyName switch
        {
            "gaussian" => GlmFamily.Gaussian,
            "binomial" => GlmFamily.Binomial,
            "poisson" => GlmFamily.Poisson,
            "gamma" => GlmFamily.Gamma,
            "inverse_gaussian" => GlmFamily.InverseGaussian,
            _ => GlmFamily.NegativeBinomial
        };
        var link = effectiveLink switch
        {
            "identity" => GlmLink.Identity,
            "log" => GlmLink.Log,
            "logit" => GlmLink.Logit,
            "probit" => GlmLink.Probit,
            "cloglog" => GlmLink.CLogLog,
            "inverse_squared" => GlmLink.InverseSquared,
            "sqrt" => GlmLink.Sqrt,
            _ => GlmLink.InversePower
        };

        var fitResult = GlmModel.Fit(
            design.Matrix,
            y,
            family,
            link,
            negativeBinomialAlpha: input.Pnum("nb_alpha", 1),
            pearsonScale: scaleOption == "pearson");
        if (fitResult.IsError) return Result.Err<AnalysisResult>(fitResult.Error.Code, fitResult.Error.Message);
        var fit = fitResult.Value;
        var enough = ModelHelpers.NeedEnough(design, fit.Parameters + 1);
        if (enough.IsError) return Result.Err<AnalysisResult>(enough.Error.Code, enough.Error.Message);
        var n = design.RowCount;
        var nullDeviance = fit.NullDeviance;
        var familyLabel = FamilyLabels[familyName].Split(" (")[0].ToLowerInvariant();

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("Devianza residual", ModelHelpers.N(fit.Deviance)),
                new TileItem("Devianza nula", ModelHelpers.N(nullDeviance)),
                new TileItem("Pseudo-R² (1 − dev/dev nula)", ModelHelpers.N(nullDeviance != 0 ? 1 - fit.Deviance / nullDeviance : 0))
            ])
        };
        ModelHelpers.DroppedNote(sections, design);

        var exponentiate = effectiveLink switch
        {
            "log" => "exp(coef)",
            "logit" => "Odds ratio",
            _ => null
        };
        var confidence = fit.ConfidenceIntervals(alpha);
        var coefficientRows = new List<IReadOnlyList<string>>();
        var headers = new List<string> { "Término", "Coeficiente", "Error est.", "z", "Valor p" };
        headers.AddRange(ModelHelpers.CiColumns(alpha));
        if (exponentiate is not null)
        {
            headers.Add(exponentiate);
            foreach (var column in ModelHelpers.CiColumns(alpha)) headers.Add($"{column} ({exponentiate})");
        }
        for (var index = 0; index < fit.Coefficients.Length; index++)
        {
            var name = design.ColumnNames[index] == "Intercept" ? "Constante" : design.Pretty(design.ColumnNames[index]);
            var row = new List<string>
            {
                name,
                ModelHelpers.N(fit.Coefficients[index]),
                ModelHelpers.N(fit.StandardErrors[index]),
                ModelHelpers.N(fit.ZValues[index]),
                ModelHelpers.P(fit.PValues[index]),
                ModelHelpers.N(confidence[index].Low),
                ModelHelpers.N(confidence[index].High)
            };
            if (exponentiate is not null)
            {
                row.Add(ModelHelpers.N(Math.Exp(fit.Coefficients[index])));
                row.Add(ModelHelpers.N(Math.Exp(confidence[index].Low)));
                row.Add(ModelHelpers.N(Math.Exp(confidence[index].High)));
            }
            coefficientRows.Add(row);
        }
        sections.Add(new TableSection("Coeficientes", headers, coefficientRows));

        var fitRows = new List<IReadOnlyList<string>>
        {
            new[]
            {
                "Devianza residual",
                ModelHelpers.N(fit.Deviance),
                JsNumber.ToString(n - fit.Parameters),
                familyName is "binomial" or "poisson" ? ModelHelpers.P(ExtendedStatistics.ChiSquareSf(fit.Deviance, n - fit.Parameters)) : string.Empty
            },
            new[] { "Devianza nula", ModelHelpers.N(nullDeviance), JsNumber.ToString(n - 1), string.Empty },
            new[] { "χ² de Pearson", ModelHelpers.N(fit.PearsonChi2), JsNumber.ToString(n - fit.Parameters), string.Empty },
            new[] { "Parámetro de escala", ModelHelpers.N(fit.Scale), string.Empty, string.Empty },
            new[] { "Log-verosimilitud", ModelHelpers.N(fit.LogLikelihood), string.Empty, string.Empty },
            new[] { "AIC", ModelHelpers.N(fit.Aic), string.Empty, string.Empty }
        };
        sections.Add(new TableSection(
            "Ajuste del modelo",
            ["Medida", "Valor", "gl", "Valor p"],
            fitRows,
            Note: "El valor p de la devianza solo es una prueba de bondad de ajuste con conteos o binarios agrupados."));

        var points = new List<ScatterPoint>(n);
        for (var row = 0; row < n; row++) points.Add(new ScatterPoint(fit.Fitted[row], fit.PearsonResiduals[row]));

        return Result.Ok(new AnalysisResult(
            "regression-glm",
            $"Modelo lineal generalizado: {response}",
            sections,
            Subtitle: $"familia {familyLabel} · liga {effectiveLink.Replace('_', ' ')} · n = {n}",
            Charts:
            [
                new ScatterChart(
                    "Residuos de Pearson frente a valores ajustados",
                    points,
                    "Valor ajustado",
                    "Residuo de Pearson")
            ],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
            {
                [ModelHelpers.PredictionsKey] = ModelHelpers.Predictions(
                    frame,
                    design,
                    ("ajustado", fit.Fitted),
                    ("residuo_pearson", fit.PearsonResiduals),
                    ("residuo_deviance", fit.DevianceResiduals))
            },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["deviance"] = fit.Deviance,
                ["null_deviance"] = nullDeviance,
                ["aic"] = fit.Aic,
                ["coefficients"] = design.ColumnNames
                    .Select((column, index) => (column, value: fit.Coefficients[index]))
                    .ToDictionary(
                        pair => pair.column == "Intercept" ? "Intercept" : design.Pretty(pair.column),
                        pair => (object?)pair.value,
                        StringComparer.Ordinal)
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
