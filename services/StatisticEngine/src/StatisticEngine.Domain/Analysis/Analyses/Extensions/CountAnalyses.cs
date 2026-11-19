using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Regresión de conteos Poisson/cuasi-Poisson (port de <c>models._count</c>).</summary>
internal static class CountAnalyses
{
    private static readonly AnalysisParamDefinition Family = new(
        "family",
        "Modelo",
        AnalysisParamKinds.Select,
        "poisson",
        [
            new("poisson", "Poisson"),
            new("quasi", "Cuasi-Poisson (corrige la sobredispersión)"),
            new("negbin", "Binomial negativa")
        ],
        false);

    public static readonly AnalysisDefinition Count = new(
        "regression-count",
        "Regresión de conteos (Poisson, cuasi-Poisson, binomial negativa)",
        "Regresión",
        "Razones de tasas con exposición opcional y diagnóstico de sobredispersión.",
        [],
        [],
        [Family, ModelHelpers.Interactions(), ModelHelpers.Alpha()],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Conteos y GLM",
        Roles:
        [
            new ColumnRole("response", "Conteo (enteros ≥ 0)", ColumnKinds.Numeric),
            new ColumnRole("predictors", "Predictores (números y categorías)", ColumnKinds.Any, Multiple: true),
            new ColumnRole("exposure", "Exposición (opcional)", ColumnKinds.Numeric, Optional: true)
        ],
        Outputs: [new AnalysisOutputSpec(ModelHelpers.PredictionsKey, ModelHelpers.PredictionsLabel)]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var response = ExtendedHelpers.FirstRole(input, "response", "Conteo (enteros ≥ 0)");
        var predictors = input.RoleNames("predictors", true);
        var alpha = input.Pnum("alpha", 0.05);
        var kind = input.Pstr("family", "poisson");
        var interactions = input.Pstr("interactions", "none");
        var exposure = ExtendedHelpers.FirstRole(input, "exposure", "Exposición");
        var extra = string.IsNullOrEmpty(exposure)
            ? null
            : new Dictionary<string, string>(StringComparer.Ordinal) { ["exposure"] = exposure };

        var designResult = DesignBuilder.Build(frame, response, predictors, interactions: interactions, extra: extra);
        if (designResult.IsError) return Result.Err<AnalysisResult>(designResult.Error.Code, designResult.Error.Message);
        var design = designResult.Value;
        var y = design.Response;
        if (y.Any(value => value < 0 || Math.Abs(value - Math.Round(value)) > 1e-9))
        {
            return ExtendedHelpers.Invalid($"«{response}» debe contener conteos: enteros no negativos.");
        }

        double[]? offset = null;
        if (extra is not null)
        {
            var token = design.Names.First(pair => pair.Value == exposure).Key;
            var values = design.Data.Numeric(token);
            if (values.Any(value => value <= 0)) return ExtendedHelpers.Invalid("La exposición debe ser positiva.");
            offset = values.Select(value => Math.Log(value)).ToArray();
        }

        if (kind == "negbin")
        {
            return ExtendedHelpers.Error(
                DomainErrorCode.NotSupported,
                "La binomial negativa de conteos aún no está disponible en este motor; use Poisson o cuasi-Poisson.");
        }

        var family = GlmFamily.Poisson;
        var fitResult = GlmModel.Fit(design.Matrix, y, family, GlmLink.Log, offset, pearsonScale: kind == "quasi");
        if (fitResult.IsError) return Result.Err<AnalysisResult>(fitResult.Error.Code, fitResult.Error.Message);
        var fit = fitResult.Value;
        var n = design.RowCount;
        var mu = fit.Fitted;
        var k = fit.Parameters;
        var dfResid = n - k;
        var pearson = 0.0;
        for (var row = 0; row < n; row++) pearson += Math.Pow(y[row] - mu[row], 2) / Math.Max(mu[row], 1e-12);
        var ratio = pearson / dfResid;
        var (ctAlpha, ctP) = CameronTrivedi(y, mu);

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("Media de la respuesta", ModelHelpers.N(y.Average())),
                new TileItem("Pearson χ² / gl", ModelHelpers.N(ratio)),
                new TileItem("Parámetros", JsNumber.ToString(k))
            ])
        };
        ModelHelpers.DroppedNote(sections, design);
        if (kind == "poisson" && ratio > 1.5 && ctP < alpha)
        {
            sections.Add(new AlertSection(
                "Aviso",
                "warning",
                $"Hay sobredispersión (χ² de Pearson / gl = {ratio.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}; "
                + $"prueba de Cameron-Trivedi p = {SigmaText.Fixed4(ctP)}): los errores estándar de Poisson son demasiado pequeños. "
                + "Use cuasi-Poisson o binomial negativa."));
        }
        else if (kind == "poisson")
        {
            sections.Add(new AlertSection(
                "Conclusión",
                "success",
                $"Sin evidencia fuerte de sobredispersión (χ² de Pearson / gl = {ratio.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)})."));
        }

        var confidence = fit.ConfidenceIntervals(alpha);
        var headers = new List<string> { "Término", "Coeficiente", "Error est.", "z", "Valor p" };
        headers.AddRange(ModelHelpers.CiColumns(alpha));
        headers.Add("Razón de tasas");
        headers.Add($"IC {SigmaText.General(100 * (1 - alpha))} % inf. (Razón de tasas)");
        headers.Add($"IC sup. (Razón de tasas)");
        var coefficientRows = new List<IReadOnlyList<string>>();
        for (var index = 0; index < fit.Coefficients.Length; index++)
        {
            var name = design.ColumnNames[index] == "Intercept" ? "Constante" : design.Pretty(design.ColumnNames[index]);
            coefficientRows.Add([
                name,
                ModelHelpers.N(fit.Coefficients[index]),
                ModelHelpers.N(fit.StandardErrors[index]),
                ModelHelpers.N(fit.ZValues[index]),
                ModelHelpers.P(fit.PValues[index]),
                ModelHelpers.N(confidence[index].Low),
                ModelHelpers.N(confidence[index].High),
                ModelHelpers.N(Math.Exp(fit.Coefficients[index])),
                ModelHelpers.N(Math.Exp(confidence[index].Low)),
                ModelHelpers.N(Math.Exp(confidence[index].High))
            ]);
        }
        sections.Add(new TableSection(
            "Coeficientes",
            headers,
            coefficientRows,
            Note: "Razón de tasas = exp(coeficiente): cuánto se multiplica el conteo esperado por cada unidad del predictor."));

        var fitRows = new List<IReadOnlyList<string>>
        {
            new[]
            {
                "Devianza residual",
                ModelHelpers.N(fit.Deviance),
                ModelHelpers.P(ExtendedStatistics.ChiSquareSf(fit.Deviance, dfResid))
            },
            new[] { "Dispersión (χ² de Pearson / gl)", ModelHelpers.N(ratio), string.Empty },
            new[] { "Cameron-Trivedi: α", ModelHelpers.N(ctAlpha), ModelHelpers.P(ctP) }
        };
        if (kind != "quasi")
        {
            fitRows.Add(new[] { "Log-verosimilitud", ModelHelpers.N(fit.LogLikelihood), string.Empty });
            fitRows.Add(new[] { "AIC", ModelHelpers.N(fit.Aic), string.Empty });
            fitRows.Add(new[] { "BIC", ModelHelpers.N(-2 * fit.LogLikelihood + k * Math.Log(n)), string.Empty });
        }
        sections.Add(new TableSection("Ajuste del modelo", ["Medida", "Valor", "Valor p"], fitRows));

        var top = Math.Min(Math.Max((int)Math.Round(y.Max()), 3), 15);
        var categories = Enumerable.Range(0, top + 1).Select(value => JsNumber.ToString(value)).ToArray();
        var observed = Enumerable.Range(0, top + 1).Select(value => y.Count(item => item == value) / (double)n).ToArray();
        var expected = Enumerable.Range(0, top + 1)
            .Select(value => mu.Average(mean => PoissonPmf(value, mean)))
            .ToArray();
        var pearsonResiduals = new double[n];
        for (var row = 0; row < n; row++) pearsonResiduals[row] = (y[row] - mu[row]) / Math.Sqrt(Math.Max(mu[row], 1e-12));

        return Result.Ok(new AnalysisResult(
            "regression-count",
            $"Regresión de conteos (Poisson): {response}",
            sections,
            Subtitle: $"n = {n}" + (extra is not null ? $" · exposición = {exposure}" : string.Empty),
            Charts:
            [
                new GroupedBarChart(
                    "Frecuencias observadas y esperadas por el modelo",
                    categories,
                    [
                        new BarSeries("Observadas", observed),
                        new BarSeries("Esperadas", expected)
                    ])
            ],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
            {
                [ModelHelpers.PredictionsKey] = ModelHelpers.Predictions(
                    frame,
                    design,
                    ("ajustado", mu),
                    ("residuo_pearson", pearsonResiduals))
            },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["dispersion"] = ratio,
                ["cameron_trivedi_p"] = ctP,
                ["coefficients"] = design.ColumnNames
                    .Select((column, index) => (column, value: fit.Coefficients[index]))
                    .ToDictionary(
                        pair => pair.column == "Intercept" ? "Intercept" : design.Pretty(pair.column),
                        pair => (object?)pair.value,
                        StringComparer.Ordinal)
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static (double Alpha, double PValue) CameronTrivedi(double[] y, double[] mu)
    {
        var target = new double[y.Length];
        var design = new double[y.Length][];
        for (var row = 0; row < y.Length; row++)
        {
            target[row] = (Math.Pow(y[row] - mu[row], 2) - y[row]) / Math.Max(mu[row], 1e-12);
            design[row] = [mu[row]];
        }
        var fit = LinearModel.Fit(design, target);
        return fit.IsOk ? (fit.Value.Coefficients[0], fit.Value.PValues[0]) : (double.NaN, double.NaN);
    }

    private static double PoissonPmf(int value, double mean)
    {
        if (mean <= 0) return value == 0 ? 1 : 0;
        return Math.Exp(value * Math.Log(mean) - mean - SpecialFunctions.LogGamma(value + 1));
    }
}
