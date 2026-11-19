using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Pruebas de especificación de un modelo lineal (port de <c>models_more._diagnostics</c>).</summary>
internal static class DiagnosticsAnalyses
{
    private static readonly (string Key, string Label)[] Tests =
    [
        ("breusch_pagan", "Breusch-Pagan (heterocedasticidad)"),
        ("white", "White (heterocedasticidad general)"),
        ("reset", "RESET de Ramsey (forma funcional)"),
        ("harvey_collier", "Harvey-Collier (linealidad)"),
        ("durbin_watson", "Durbin-Watson (autocorrelación de primer orden)"),
        ("breusch_godfrey", "Breusch-Godfrey (autocorrelación de orden superior)"),
        ("jarque_bera", "Jarque-Bera (normalidad de los residuos)"),
        ("shapiro", "Shapiro-Wilk (normalidad de los residuos)"),
        ("cusum", "CUSUM (estabilidad de los parámetros)"),
        ("chow", "Chow (cambio estructural en un punto)")
    ];

    private static readonly AnalysisParamDefinition TestsParam = new(
        "tests",
        "Pruebas",
        AnalysisParamKinds.Multiselect,
        new[] { "breusch_pagan", "white", "reset", "durbin_watson", "jarque_bera" },
        Tests.Select(item => new AnalysisParamOption(item.Key, item.Label)).ToArray(),
        false);

    public static readonly AnalysisDefinition Diagnostics = new(
        "regression-diagnostics",
        "Pruebas de especificación de un modelo lineal",
        "Regresión",
        "White, Breusch-Pagan, RESET, Durbin-Watson, Breusch-Godfrey, normalidad, CUSUM y Chow, con gráficos de residuos.",
        [],
        [],
        [
            TestsParam,
            new("reset_power", "Potencia máxima del RESET", AnalysisParamKinds.Integer, 3.0, [], false, Minimum: 2, Maximum: 5),
            new("lags", "Rezagos de Breusch-Godfrey", AnalysisParamKinds.Integer, 4.0, [], false, Minimum: 1, Maximum: 40),
            new("break_at", "Punto de cambio de Chow (fracción 0-1 o número de fila)", AnalysisParamKinds.Number, 0.5, [], false, Minimum: 0.05),
            ModelHelpers.Alpha()
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Endogeneidad, efectos mixtos y diagnóstico",
        Roles:
        [
            new ColumnRole("response", "Respuesta (Y)", ColumnKinds.Numeric),
            new ColumnRole("predictors", "Predictores (números y categorías)", ColumnKinds.Any, Multiple: true),
            new ColumnRole(
                "order",
                "Ordenar las filas por (opcional)",
                ColumnKinds.Any,
                Optional: true,
                Help: "Para las pruebas de autocorrelación y cambio estructural: fecha, tiempo u otro orden.")
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var response = ExtendedHelpers.FirstRole(input, "response", "Respuesta (Y)");
        var predictors = input.RoleNames("predictors", true);
        var order = ExtendedHelpers.FirstRole(input, "order", "Ordenar las filas por");
        var chosen = input.Plist("tests");
        if (chosen.Count == 0) chosen = ["breusch_pagan", "white", "reset", "durbin_watson", "jarque_bera"];
        var alpha = input.Pnum("alpha", 0.05);
        var unsupported = chosen
            .Where(key => key is not ("breusch_pagan" or "durbin_watson"))
            .ToArray();
        if (unsupported.Length > 0)
        {
            return ExtendedHelpers.Error(
                DomainErrorCode.NotSupported,
                $"La prueba «{unsupported[0]}» aún no está disponible en este motor; use breusch_pagan o durbin_watson.");
        }

        var work = string.IsNullOrEmpty(order) ? frame : SortBy(frame, order);
        var designResult = DesignBuilder.Build(work, response, predictors);
        if (designResult.IsError) return Result.Err<AnalysisResult>(designResult.Error.Code, designResult.Error.Message);
        var design = designResult.Value;
        var fitResult = LinearModel.Fit(design);
        if (fitResult.IsError) return Result.Err<AnalysisResult>(fitResult.Error.Code, fitResult.Error.Message);
        var fit = fitResult.Value;
        var enough = ModelHelpers.NeedEnough(design, fit.Parameters + 2);
        if (enough.IsError) return Result.Err<AnalysisResult>(enough.Error.Code, enough.Error.Message);
        var n = design.RowCount;
        var residuals = fit.Residuals;
        var standardized = residuals.Select(value => value / fit.Sigma).ToArray();

        var rows = new List<IReadOnlyList<string>>();
        var resultValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        var problems = 0;
        foreach (var key in chosen)
        {
            var label = Tests.First(item => item.Key == key).Label;
            if (key == "breusch_pagan")
            {
                var (statistic, pValue) = LinearModel.BreuschPagan(residuals, design.Matrix);
                var problem = pValue < alpha;
                if (problem) problems++;
                rows.Add([
                    label,
                    ModelHelpers.N(statistic),
                    ModelHelpers.P(pValue),
                    problem ? "posible problema" : "sin evidencia de problema"
                ]);
                resultValues[label] = pValue;
            }
            else
            {
                var statistic = LinearModel.DurbinWatson(residuals);
                rows.Add([
                    label,
                    ModelHelpers.N(statistic),
                    string.Empty,
                    "cerca de 2: sin autocorrelación; hacia 0: positiva; hacia 4: negativa"
                ]);
                resultValues[label] = null;
            }
        }

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("Pruebas", JsNumber.ToString(rows.Count)),
                new TileItem("Con posible problema", JsNumber.ToString(problems)),
                new TileItem("R²", ModelHelpers.N(fit.R2))
            ])
        };
        ModelHelpers.DroppedNote(sections, design);
        sections.Add(new TableSection(
            "Resultados",
            ["Prueba", "Estadístico", "Valor p", "Lectura"],
            rows,
            Note: "Un valor p pequeño en estas pruebas señala un incumplimiento del supuesto (no «rechazar» es lo deseable)."));

        var residualPoints = new List<ScatterPoint>(n);
        for (var row = 0; row < n; row++) residualPoints.Add(new ScatterPoint(fit.Fitted[row], standardized[row]));
        var ordered = standardized.OrderBy(value => value).ToArray();
        var theoreticalPoints = new List<ScatterPoint>(n);
        for (var row = 0; row < n; row++)
        {
            theoreticalPoints.Add(new ScatterPoint(SpecialFunctions.NormalInv((row + 1 - 0.5) / n), ordered[row]));
        }
        var orderSeries = new Charts.LineSeries(
            "Residuo",
            residuals);

        return Result.Ok(new AnalysisResult(
            "regression-diagnostics",
            $"Pruebas de especificación del modelo lineal: {response}",
            sections,
            Subtitle: $"{rows.Count} pruebas · n = {n}" + (string.IsNullOrEmpty(order) ? string.Empty : $" · filas ordenadas por {order}"),
            Charts:
            [
                new ScatterChart("Residuos estandarizados frente a ajustados", residualPoints, "Valor ajustado", "Residuo estandarizado"),
                new ScatterChart("Gráfico de normalidad de los residuos", theoreticalPoints, "Cuantil normal", "Residuo estandarizado"),
                new LineChart("Residuos en el orden de las filas", [orderSeries], "Observación")
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["problems"] = problems,
                ["tests"] = resultValues
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Frame SortBy(Frame frame, string column)
    {
        var source = frame.Column(column);
        var indices = Enumerable.Range(0, frame.RowCount);
        indices = source.Kind switch
        {
            DataColumnKind.Numeric => indices.OrderBy(index => source.NumberAt(index)),
            DataColumnKind.DateTime => indices.OrderBy(index => source.DateAt(index) ?? DateTime.MinValue),
            _ => indices.OrderBy(index => source.TextAt(index), StringComparer.Ordinal)
        };
        return frame.SubsetRows(indices.ToArray());
    }
}
