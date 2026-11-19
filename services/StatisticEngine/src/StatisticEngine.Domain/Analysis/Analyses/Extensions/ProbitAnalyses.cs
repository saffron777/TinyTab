using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Regresión binaria (probit/logit/cloglog) con clasificación y ROC (port de <c>models._binary</c>).</summary>
internal static class ProbitAnalyses
{
    private static readonly AnalysisParamDefinition Link = new(
        "link",
        "Liga",
        AnalysisParamKinds.Select,
        "logit",
        [
            new("logit", "Logit (regresión logística)"),
            new("probit", "Probit"),
            new("cloglog", "Complemento log-log")
        ],
        false);

    public static readonly AnalysisDefinition Probit = new(
        "regression-probit",
        "Regresión binaria (probit)",
        "Regresión",
        "Respuesta binaria con liga probit (o logit/cloglog): odds ratios, ajuste, clasificación y curva ROC.",
        [],
        [],
        [
            Link,
            new("event", "Valor que cuenta como evento (vacío = el mayor)", AnalysisParamKinds.Text, null, [], true),
            new("cutoff", "Corte de clasificación", AnalysisParamKinds.Number, 0.5, [], false, Minimum: 0.01, Maximum: 0.99),
            ModelHelpers.Interactions(),
            ModelHelpers.Alpha()
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Respuesta binaria",
        Roles:
        [
            new ColumnRole("response", "Respuesta binaria (2 valores)", ColumnKinds.Any),
            new ColumnRole("predictors", "Predictores (números y categorías)", ColumnKinds.Any, Multiple: true)
        ],
        Outputs: [new AnalysisOutputSpec("predicciones", "Predicciones y residuos")]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var response = ExtendedHelpers.FirstRole(input, "response", "Respuesta binaria");
        var predictors = input.RoleNames("predictors", true);
        var alpha = input.Pnum("alpha", 0.05);
        var linkName = input.Pstr("link", "logit");
        var cutoff = input.Pnum("cutoff", 0.5);
        var interactions = input.Pstr("interactions", "none");
        var levels = frame.Levels(response);
        if (levels.Length != 2)
        {
            return ExtendedHelpers.Invalid($"«{response}» debe tener exactamente 2 valores distintos (tiene {levels.Length}).");
        }
        var requested = input.Pstr("event", string.Empty).Trim();
        var eventLabel = requested.Length > 0 ? requested : levels[1];
        if (eventLabel != levels[0] && eventLabel != levels[1])
        {
            return ExtendedHelpers.Invalid($"El valor de evento «{eventLabel}» no está entre los valores de «{response}».");
        }
        var otherLabel = eventLabel == levels[0] ? levels[1] : levels[0];

        var labels = frame.Texts(response);
        var encoded = labels.Select(value => value == eventLabel ? 1.0 : 0.0).ToArray();
        var working = frame.With(DataColumn.Numeric(response, encoded));
        var designResult = DesignBuilder.Build(working, response, predictors, interactions: interactions);
        if (designResult.IsError) return Result.Err<AnalysisResult>(designResult.Error.Code, designResult.Error.Message);
        var design = designResult.Value;
        var y = design.Response;
        var events = (int)y.Sum();
        if (events < 1 || events == design.RowCount)
        {
            return ExtendedHelpers.Invalid("La respuesta tiene un solo valor entre las filas completas: no hay nada que modelar.");
        }
        var link = linkName switch
        {
            "probit" => GlmLink.Probit,
            "cloglog" => GlmLink.CLogLog,
            _ => GlmLink.Logit
        };
        var fitResult = GlmModel.Fit(design.Matrix, y, GlmFamily.Binomial, link);
        if (fitResult.IsError) return Result.Err<AnalysisResult>(fitResult.Error.Code, fitResult.Error.Message);
        var fit = fitResult.Value;
        var enough = ModelHelpers.NeedEnough(design, fit.Parameters + 1);
        if (enough.IsError) return Result.Err<AnalysisResult>(enough.Error.Code, enough.Error.Message);
        var n = design.RowCount;
        var k = fit.Parameters - 1;
        var eventRate = y.Average();
        var nullLogLikelihood = n * (eventRate * Math.Log(eventRate) + (1 - eventRate) * Math.Log(1 - eventRate));
        var logLikelihood = fit.LogLikelihood;
        var lr = 2 * (logLikelihood - nullLogLikelihood);
        var cox = 1 - Math.Exp(2 * (nullLogLikelihood - logLikelihood) / n);
        var nagelkerke = cox / (1 - Math.Exp(2 * nullLogLikelihood / n));

        var probability = fit.Fitted;
        var (hl, hlP, groups) = HosmerLemeshow(y, probability);
        var pLr = ExtendedStatistics.ChiSquareSf(lr, k);
        var predicted = probability.Select(value => value >= cutoff ? 1 : 0).ToArray();
        var tp = predicted.Select((value, index) => value == 1 && y[index] == 1 ? 1 : 0).Sum();
        var tn = predicted.Select((value, index) => value == 0 && y[index] == 0 ? 1 : 0).Sum();
        var fp = predicted.Select((value, index) => value == 1 && y[index] == 0 ? 1 : 0).Sum();
        var fn = predicted.Select((value, index) => value == 0 && y[index] == 1 ? 1 : 0).Sum();
        var accuracy = (tp + tn) / (double)n;
        var sensitivity = tp / (double)Math.Max(1, tp + fn);
        var specificity = tn / (double)Math.Max(1, tn + fp);
        var ppv = tp / (double)Math.Max(1, tp + fp);
        var npv = tn / (double)Math.Max(1, tn + fn);
        var auc = AreaUnderRoc(y, probability);

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("Eventos", JsNumber.ToString(events)),
                new TileItem("χ² de razón de verosimilitud", ModelHelpers.N(lr)),
                new TileItem("McFadden R²", ModelHelpers.N(1 - logLikelihood / nullLogLikelihood))
            ])
        };
        ModelHelpers.DroppedNote(sections, design);
        if (events / Math.Max(1, k) < 10)
        {
            sections.Add(new AlertSection(
                "Aviso",
                "warning",
                $"Solo hay {events} eventos para {k} parámetro(s) ({(events / (double)Math.Max(1, k)).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} por parámetro): se recomiendan al menos 10 para que los coeficientes sean estables."));
        }

        var confidence = fit.ConfidenceIntervals(alpha);
        var exponentiate = linkName == "logit";
        var headers = new List<string> { "Término", "Coeficiente", "Error est.", "z", "Valor p" };
        headers.AddRange(ModelHelpers.CiColumns(alpha));
        if (exponentiate)
        {
            headers.Add("Odds ratio");
            headers.Add($"IC {SigmaText.General(100 * (1 - alpha))} % inf. (Odds ratio)");
            headers.Add("IC sup. (Odds ratio)");
        }
        var coefficientRows = new List<IReadOnlyList<string>>();
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
            if (exponentiate)
            {
                row.Add(ModelHelpers.N(Math.Exp(fit.Coefficients[index])));
                row.Add(ModelHelpers.N(Math.Exp(confidence[index].Low)));
                row.Add(ModelHelpers.N(Math.Exp(confidence[index].High)));
            }
            coefficientRows.Add(row);
        }
        sections.Add(new TableSection(
            "Coeficientes",
            headers,
            coefficientRows,
            Note: exponentiate
                ? "Odds ratio = exp(coeficiente): cuánto se multiplican las ventajas del evento por cada unidad (o frente a la categoría de referencia)."
                : null));

        sections.Add(new TableSection(
            "Ajuste del modelo",
            ["Medida", "Valor", "gl", "Valor p"],
            [
                new[] { "Razón de verosimilitud (frente al modelo sin predictores)", ModelHelpers.N(lr), JsNumber.ToString(k), ModelHelpers.P(pLr) },
                new[] { "Hosmer-Lemeshow", ModelHelpers.N(hl), JsNumber.ToString(groups - 2), ModelHelpers.P(hlP) },
                new[] { "R² de McFadden", ModelHelpers.N(1 - logLikelihood / nullLogLikelihood), string.Empty, string.Empty },
                new[] { "R² de Cox-Snell", ModelHelpers.N(cox), string.Empty, string.Empty },
                new[] { "R² de Nagelkerke", ModelHelpers.N(nagelkerke), string.Empty, string.Empty },
                new[] { "Log-verosimilitud", ModelHelpers.N(logLikelihood), string.Empty, string.Empty },
                new[] { "AIC", ModelHelpers.N(-2 * logLikelihood + 2 * fit.Parameters), string.Empty, string.Empty },
                new[] { "BIC", ModelHelpers.N(-2 * logLikelihood + fit.Parameters * Math.Log(n)), string.Empty, string.Empty }
            ],
            Note: "Hosmer-Lemeshow: un valor p pequeño indica mal ajuste (calibración)."));

        sections.Add(new TableSection(
            $"Clasificación con corte {SigmaText.General(cutoff)}",
            ["", $"Predicho = {eventLabel}", $"Predicho = {otherLabel}", "Total"],
            [
                new[] { $"Real = {eventLabel}", JsNumber.ToString(tp), JsNumber.ToString(fn), JsNumber.ToString(tp + fn) },
                new[] { $"Real = {otherLabel}", JsNumber.ToString(fp), JsNumber.ToString(tn), JsNumber.ToString(fp + tn) }
            ]));

        sections.Add(new TableSection(
            "Medidas de clasificación",
            ["Medida", "Valor"],
            [
                new[] { "Exactitud", ModelHelpers.N(accuracy) },
                new[] { "Sensibilidad", ModelHelpers.N(sensitivity) },
                new[] { "Especificidad", ModelHelpers.N(specificity) },
                new[] { "Valor predictivo positivo", ModelHelpers.N(ppv) },
                new[] { "Valor predictivo negativo", ModelHelpers.N(npv) },
                new[] { "Área bajo la curva ROC (AUC)", ModelHelpers.N(auc) }
            ]));

        var curve = RocCurve(y, probability);
        var points = curve.Select(pair => new ScatterPoint(pair.FalsePositive, pair.TruePositive)).ToList();

        var label = linkName switch
        {
            "probit" => "probit",
            "cloglog" => "complemento log-log",
            _ => "logit"
        };

        var predictions = ModelHelpers.Predictions(
            frame,
            design,
            ("probabilidad", probability),
            ("clase_predicha", predicted.Select(value => (double)value).ToArray()),
            ("residuo_deviance", fit.DevianceResiduals));

        return Result.Ok(new AnalysisResult(
            "regression-probit",
            $"Regresión {label}: {response}",
            sections,
            Subtitle: $"evento = {eventLabel} · referencia = {otherLabel} · n = {n}",
            Charts:
            [
                new ScatterChart("Curva ROC", points, "1 − especificidad", "Sensibilidad")
            ],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal) { ["predicciones"] = predictions },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["events"] = events,
                ["auc"] = auc,
                ["accuracy"] = accuracy,
                ["mcfadden"] = 1 - logLikelihood / nullLogLikelihood,
                ["lr_p"] = pLr,
                ["hl_p"] = hlP,
                ["coefficients"] = design.ColumnNames
                    .Select((column, index) => (column, value: fit.Coefficients[index]))
                    .ToDictionary(
                        pair => pair.column == "Intercept" ? "Intercept" : design.Pretty(pair.column),
                        pair => (object?)pair.value,
                        StringComparer.Ordinal)
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static (double Statistic, double PValue, int Groups) HosmerLemeshow(double[] y, double[] probability)
    {
        var order = Enumerable.Range(0, y.Length).OrderBy(index => probability[index]).ToArray();
        var groups = Math.Min(10, y.Length);
        var sizes = new int[groups];
        for (var index = 0; index < groups; index++) sizes[index] = y.Length / groups + (index < y.Length % groups ? 1 : 0);
        var statistic = 0.0;
        var position = 0;
        foreach (var size in sizes)
        {
            var observed = 0.0;
            var expected = 0.0;
            for (var index = 0; index < size; index++)
            {
                var row = order[position + index];
                observed += y[row];
                expected += probability[row];
            }
            var mean = expected / size;
            if (mean > 0 && mean < 1) statistic += Math.Pow(observed - expected, 2) / (size * mean * (1 - mean));
            position += size;
        }
        var df = Math.Max(groups - 2, 1);
        return (statistic, ExtendedStatistics.ChiSquareSf(statistic, df), groups);
    }

    private static double AreaUnderRoc(double[] y, double[] probability)
    {
        var curve = RocCurve(y, probability);
        var area = 0.0;
        for (var index = 1; index < curve.Count; index++)
        {
            area += (curve[index].FalsePositive - curve[index - 1].FalsePositive)
                    * (curve[index].TruePositive + curve[index - 1].TruePositive) / 2;
        }
        return area;
    }

    private static List<(double FalsePositive, double TruePositive)> RocCurve(double[] y, double[] probability)
    {
        var positives = y.Sum();
        var negatives = y.Length - positives;
        var order = Enumerable.Range(0, y.Length).OrderByDescending(index => probability[index]).ToArray();
        var curve = new List<(double, double)> { (0.0, 0.0) };
        var tp = 0.0;
        var fp = 0.0;
        for (var index = 0; index < order.Length; index++)
        {
            if (y[order[index]] == 1) tp++;
            else fp++;
            if (index + 1 < order.Length && probability[order[index + 1]] == probability[order[index]]) continue;
            curve.Add((fp / Math.Max(1, negatives), tp / Math.Max(1, positives)));
        }
        return curve;
    }
}
