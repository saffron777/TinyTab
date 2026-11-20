using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>
/// Datos de panel: MCO agrupado, efectos fijos por individuo (y opcionalmente por tiempo) y efectos
/// aleatorios, con errores agrupados, robustos, clásicos o de Driscoll-Kraay, prueba F de
/// poolabilidad, Breusch-Pagan LM y Hausman. Port de <c>econ_models._panel</c> con los estimadores de
/// <c>linearmodels.panel.PooledOLS/PanelOLS/RandomEffects</c>.
/// </summary>
internal static class PanelAnalyses
{
    private static readonly (string Value, string Label)[] CovarianceOptions =
    [
        ("clustered", "Agrupados por individuo (recomendado)"),
        ("robust", "Robustos a heterocedasticidad"),
        ("unadjusted", "Clásicos"),
        ("kernel", "Driscoll-Kraay (dependencia entre individuos)")
    ];

    public static readonly AnalysisDefinition Panel = new(
        "panel",
        "Datos de panel: efectos fijos y aleatorios",
        "Econometría",
        "MCO agrupado, efectos fijos (individuos y tiempo) y efectos aleatorios con errores agrupados o de Driscoll-Kraay; pruebas F, Breusch-Pagan y Hausman.",
        [],
        [],
        [
            new(
                "cov",
                "Errores estándar",
                AnalysisParamKinds.Select,
                "clustered",
                [.. CovarianceOptions.Select(item => new AnalysisParamOption(item.Value, item.Label))],
                false),
            new(
                "time_effects",
                "Agregar efectos de tiempo al modelo de efectos fijos",
                AnalysisParamKinds.Boolean,
                false,
                [],
                false),
            ExtendedHelpers.Alpha()
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Datos de panel",
        Roles:
        [
            new ColumnRole("response", "Respuesta", ColumnKinds.Numeric),
            new ColumnRole("predictors", "Regresores", ColumnKinds.Any, Multiple: true, MinCount: 1),
            new ColumnRole("entity", "Individuo (entidad)", ColumnKinds.Any),
            new ColumnRole("time", "Periodo", ColumnKinds.Any)
        ],
        Outputs: [new AnalysisOutputSpec("efectos", "Efectos fijos por individuo")]);

    /// <summary>Muestra del panel ya alineada y agrupada por individuo.</summary>
    private sealed record PanelSample(
        double[] Response,
        double[] Predictor,
        string PredictorName,
        string[] GroupLabels,
        int[] GroupIds,
        int[] GroupSizes,
        int[] PeriodIds,
        int PeriodCount);

    /// <summary>Ajuste de uno de los tres modelos, con su covarianza y sus R².</summary>
    private sealed record PanelFit(
        IReadOnlyList<string> Terms,
        double[] Coefficients,
        double[] StandardErrors,
        double[] PValues,
        double[][] Covariance,
        double[] Residuals,
        double[][] Design,
        int DfResid,
        double R2Within,
        double R2Between,
        double R2Overall);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var responseName = ExtendedHelpers.FirstRole(input, "response", "Respuesta");
        var predictorNames = input.RoleNames("predictors", true).ToArray();
        var entityName = ExtendedHelpers.FirstRole(input, "entity", "Individuo (entidad)");
        var timeName = ExtendedHelpers.FirstRole(input, "time", "Periodo");
        var covariance = input.Pstr("cov", "clustered");
        var timeEffects = input.Pbool("time_effects", false);
        var alpha = input.Pnum("alpha", 0.05);

        var built = BuildSample(frame, responseName, predictorNames, entityName, timeName, out var dropped);
        if (built.IsError) return Result.Err<AnalysisResult>(built.Error.Code, built.Error.Message);
        var sample = built.Value;
        var groups = sample.GroupLabels.Length;
        var n = sample.Response.Length;
        if (groups < 3 || sample.GroupSizes.Max() < 2)
        {
            return ExtendedHelpers.Invalid("Un panel necesita al menos 3 individuos con 2 o más periodos cada uno.");
        }

        var terms = new[] { "const", sample.PredictorName };
        var pooled = FitPooled(sample, covariance);
        var fixedFit = FitFixed(sample, covariance, timeEffects);
        var random = FitRandom(sample, covariance, out var sigma2Effects, out var sigma2Residual, out var rho, out var thetaMean);
        var poolability = PooledF(sample, fixedFit);
        var breuschPagan = BreuschPagan(sample, pooled.Residuals);
        var hausman = Hausman(sample, fixedFit, random, covariance);

        var sections = new List<AnalysisSection>();
        if (dropped > 0)
        {
            sections.Add(new AlertSection("Aviso", "info", $"Se omitieron {dropped} fila(s) con datos faltantes."));
        }

        var balanced = sample.GroupSizes.Distinct().Count() == 1;
        sections.Add(new TilesSection(
            "Resumen",
            [
                new TileItem("Individuos", JsNumber.ToString(groups)),
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("Panel", balanced ? "balanceado" : "no balanceado"),
                new TileItem("R² dentro (efectos fijos)", JsNumber.ToString(fixedFit.R2Within))
            ]));

        var models = new[] { pooled, fixedFit, random };
        var coefficientRows = new List<IReadOnlyList<string>>();
        foreach (var term in terms)
        {
            var row = new List<string> { term };
            foreach (var fit in models)
            {
                var index = IndexOf(fit.Terms, term);
                if (index < 0)
                {
                    row.Add("*");
                    row.Add("*");
                    row.Add("*");
                }
                else
                {
                    row.Add(ExtendedHelpers.N(fit.Coefficients[index]));
                    row.Add(ExtendedHelpers.N(fit.StandardErrors[index]));
                    row.Add(SigmaText.PValue(fit.PValues[index]));
                }
            }
            coefficientRows.Add(row);
        }
        sections.Add(new TableSection(
            "Coeficientes de los tres modelos",
            ["Término", "MCO agrupado", "EE", "p", "Efectos fijos", "EE", "p", "Efectos aleatorios", "EE", "p"],
            coefficientRows,
            Note: "Los efectos fijos eliminan toda diferencia constante entre individuos (por eso no tienen constante ni admiten regresores que no cambian en el tiempo)."));

        sections.Add(new TableSection(
            "Bondad de ajuste",
            ["Modelo", "R² dentro", "R² entre", "R² total"],
            new[]
            {
                new[] { "MCO agrupado", ExtendedHelpers.N(pooled.R2Within), ExtendedHelpers.N(pooled.R2Between), ExtendedHelpers.N(pooled.R2Overall) },
                new[] { "Efectos fijos", ExtendedHelpers.N(fixedFit.R2Within), ExtendedHelpers.N(fixedFit.R2Between), ExtendedHelpers.N(fixedFit.R2Overall) },
                new[] { "Efectos aleatorios", ExtendedHelpers.N(random.R2Within), ExtendedHelpers.N(random.R2Between), ExtendedHelpers.N(random.R2Overall) }
            }));

        var specificationRows = new List<IReadOnlyList<string>>
        {
            new[]
            {
                "F de poolabilidad (¿hay efectos individuales?)",
                ExtendedHelpers.N(poolability.Statistic),
                SigmaText.PValue(poolability.PValue),
                poolability.PValue < alpha
                    ? "sí: los individuos difieren, no use MCO agrupado"
                    : "no se detectan efectos individuales"
            },
            new[]
            {
                "Breusch-Pagan LM (¿efectos aleatorios frente a agrupado?)",
                ExtendedHelpers.N(breuschPagan.Statistic),
                SigmaText.PValue(breuschPagan.PValue),
                breuschPagan.PValue < alpha
                    ? "hay varianza entre individuos"
                    : "no hay evidencia de efectos aleatorios"
            }
        };
        if (hausman is { } hausmanValue)
        {
            specificationRows.Add([
                $"Hausman (efectos fijos frente a aleatorios, {hausmanValue.Df} gl)",
                ExtendedHelpers.N(hausmanValue.Statistic),
                SigmaText.PValue(hausmanValue.PValue),
                hausmanValue.PValue < alpha
                    ? "prefiera efectos fijos (los efectos aleatorios serían inconsistentes)"
                    : "efectos aleatorios es consistente y más eficiente"
            ]);
        }
        else
        {
            specificationRows.Add([
                "Hausman",
                "*",
                "*",
                "no calculable (la diferencia de varianzas no es positiva definida): use errores agrupados o la prueba de Mundlak"
            ]);
        }
        sections.Add(new TableSection(
            "Pruebas de especificación",
            ["Prueba", "Estadístico", "Valor p", "Lectura"],
            specificationRows));

        sections.Add(new TableSection(
            "Componentes de la varianza (efectos aleatorios)",
            ["Componente", "Valor"],
            new[]
            {
                new[] { "Desv. est. entre individuos (σ_u)", ExtendedHelpers.N(Math.Sqrt(Math.Max(0, sigma2Effects))) },
                new[] { "Desv. est. residual (σ_e)", ExtendedHelpers.N(Math.Sqrt(Math.Max(0, sigma2Residual))) },
                new[] { "Correlación intraclase (ρ)", ExtendedHelpers.N(rho) },
                new[] { "θ medio (fracción de la media que se resta)", ExtendedHelpers.N(thetaMean) }
            }));

        var effects = FixedEffects(sample, fixedFit);
        var shown = effects.Length > 20 ? [.. effects.Take(10), .. effects.Skip(effects.Length - 10)] : effects;
        var charts = new List<ChartSpec>
        {
            new BarChart(
                "Efectos fijos estimados por individuo (los más bajos y los más altos)",
                [.. shown.Select(item => item.Label)],
                [.. shown.Select(item => item.Value)])
        };

        var effectColumns = new List<DataColumn>
        {
            DataColumn.Text("individuo", [.. effects.Select(item => item.Label)]),
            DataColumn.Numeric("efecto_fijo", [.. effects.Select(item => item.Value)])
        };

        var covLabel = CovarianceOptions.First(item => item.Value == covariance).Label.Split(" (")[0].ToLowerInvariant();
        return Result.Ok(new AnalysisResult(
            "panel",
            $"Datos de panel: {responseName}",
            sections,
            Subtitle: $"{groups} individuos · {n} observaciones · errores {covLabel}"
                      + (timeEffects ? " · efectos de tiempo incluidos" : string.Empty),
            Charts: charts,
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
            {
                ["efectos"] = new DatasetResult("Efectos fijos por individuo", effectColumns)
            },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["groups"] = groups,
                ["n"] = n,
                ["fixed"] = ParametersByName(fixedFit),
                ["random"] = ParametersByName(random),
                ["pooled"] = ParametersByName(pooled),
                ["hausman_p"] = hausman is { } value ? value.PValue : null,
                ["poolability_p"] = poolability.PValue,
                ["breusch_pagan_p"] = breuschPagan.PValue
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static int IndexOf(IReadOnlyList<string> terms, string term)
    {
        for (var index = 0; index < terms.Count; index++)
        {
            if (string.Equals(terms[index], term, StringComparison.Ordinal)) return index;
        }
        return -1;
    }

    private static Dictionary<string, object?> ParametersByName(PanelFit fit)
    {
        var output = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 0; index < fit.Terms.Count; index++) output[fit.Terms[index]] = fit.Coefficients[index];
        return output;
    }

    // ------------------------------------------------------------------ muestra

    private static Result<PanelSample> BuildSample(
        Frame frame,
        string responseName,
        string[] predictorNames,
        string entityName,
        string timeName,
        out int dropped)
    {
        dropped = 0;
        if (predictorNames.Length != 1)
        {
            return Result.Err<PanelSample>(
                DomainErrorCode.InvalidInput,
                "El panel con paridad reproduce un único regresor numérico.");
        }
        var responseColumn = frame.Column(responseName);
        var predictorColumn = frame.Column(predictorNames[0]);
        if (responseColumn.Kind != DataColumnKind.Numeric)
        {
            return Result.Err<PanelSample>(DomainErrorCode.InvalidInput, $"«{responseName}» debe ser numérica.");
        }
        if (predictorColumn.Kind != DataColumnKind.Numeric)
        {
            return Result.Err<PanelSample>(DomainErrorCode.InvalidInput, "Los regresores deben ser numéricos.");
        }

        var rows = new List<int>();
        for (var row = 0; row < frame.RowCount; row++)
        {
            if (responseColumn.IsMissing(row) || predictorColumn.IsMissing(row)) continue;
            rows.Add(row);
        }
        dropped = frame.RowCount - rows.Count;
        if (rows.Count < 8)
        {
            return Result.Err<PanelSample>(
                DomainErrorCode.InsufficientData,
                $"Se necesitan al menos 8 filas completas (hay {rows.Count}).");
        }

        var entityColumn = frame.Column(entityName);
        var timeColumn = frame.Column(timeName);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!seen.Add(entityColumn.LabelAt(row) + "\u0001" + timeColumn.LabelAt(row)))
            {
                return Result.Err<PanelSample>(
                    DomainErrorCode.InvalidInput,
                    $"Hay combinaciones repetidas de «{entityName}» y «{timeName}»: cada individuo solo puede tener una observación por periodo.");
            }
        }

        var labels = new List<string>();
        var labelIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var groupIds = new int[rows.Count];
        var sizes = new List<int>();
        for (var index = 0; index < rows.Count; index++)
        {
            var label = entityColumn.LabelAt(rows[index]);
            if (!labelIndex.TryGetValue(label, out var group))
            {
                group = labels.Count;
                labelIndex[label] = group;
                labels.Add(label);
                sizes.Add(0);
            }
            groupIds[index] = group;
            sizes[group]++;
        }

        var periodIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var periodIds = new int[rows.Count];
        var periods = 0;
        for (var index = 0; index < rows.Count; index++)
        {
            var label = timeColumn.LabelAt(rows[index]);
            if (!periodIndex.TryGetValue(label, out var period))
            {
                period = periods;
                periodIndex[label] = period;
                periods++;
            }
            periodIds[index] = period;
        }

        var response = new double[rows.Count];
        var predictor = new double[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            response[index] = responseColumn.NumberAt(rows[index]);
            predictor[index] = predictorColumn.NumberAt(rows[index]);
        }
        return Result.Ok(new PanelSample(
            response,
            predictor,
            predictorNames[0],
            [.. labels],
            groupIds,
            [.. sizes],
            periodIds,
            periods));
    }

    // ------------------------------------------------------------------ estimadores

    private static PanelFit FitPooled(PanelSample sample, string covariance)
    {
        var n = sample.Response.Length;
        var design = WithConstant(sample.Predictor);
        var coefficients = Solve(design, sample.Response);
        var residuals = Residuals(design, sample.Response, coefficients);
        var cov = Covariance(design, residuals, covariance, 0, sample.GroupIds);
        return Build(
            ["const", sample.PredictorName],
            design,
            sample.Response,
            coefficients,
            residuals,
            cov,
            n - coefficients.Length,
            hasConstant: true,
            sample);
    }

    private static PanelFit FitFixed(PanelSample sample, string covariance, bool timeEffects)
    {
        var n = sample.Response.Length;
        var design = WithoutConstant(sample.Predictor);
        var y = sample.Response;
        var groupEffects = sample.GroupLabels.Length;
        var extra = timeEffects ? groupEffects + sample.PeriodCount - 1 : groupEffects;
        if (timeEffects)
        {
            var groupY = Demean(y, sample.GroupIds);
            var groupX = Demean(design, sample.GroupIds);
            var periodY = Demean(y, sample.PeriodIds);
            var periodX = Demean(design, sample.PeriodIds);
            var grandY = y.Average();
            var grandX = design[0].Length == 1 ? design.Average(row => row[0]) : 0;
            var yTwoWay = new double[n];
            var xTwoWay = new double[n][];
            for (var row = 0; row < n; row++)
            {
                yTwoWay[row] = groupY[row] - periodY[row] + grandY;
                xTwoWay[row] = [groupX[row][0] - periodX[row][0] + grandX];
            }
            return FitWithin(sample, xTwoWay, yTwoWay, extra, covariance, groupEffects);
        }

        return FitWithin(sample, Demean(design, sample.GroupIds), Demean(y, sample.GroupIds), extra, covariance, groupEffects);
    }

    private static PanelFit FitWithin(
        PanelSample sample,
        double[][] design,
        double[] response,
        int extra,
        string covariance,
        int groupEffects)
    {
        var n = sample.Response.Length;
        var coefficients = Solve(design, response);
        var residuals = Residuals(design, response, coefficients);
        // Con conglomerados por individuo los efectos están anidados en los conglomerados: no
        // consumen grados de libertad del estimador de covarianza (linearmodels `auto_df`).
        var covarianceExtra = covariance == "clustered" ? 0 : extra;
        var cov = Covariance(design, residuals, covariance, covarianceExtra, sample.GroupIds);
        return Build(
            [sample.PredictorName],
            design,
            response,
            coefficients,
            residuals,
            cov,
            n - coefficients.Length - extra,
            hasConstant: false,
            sample,
            withinFit: true);
    }

    private static PanelFit FitRandom(
        PanelSample sample,
        string covariance,
        out double sigma2Effects,
        out double sigma2Residual,
        out double rho,
        out double thetaMean)
    {
        var n = sample.Response.Length;
        var groups = sample.GroupLabels.Length;
        var design = WithConstant(sample.Predictor);
        var y = sample.Response;
        var t = sample.GroupSizes.Select(size => (double)size).ToArray();
        var means = GroupMeans(design, sample.GroupIds, groups);
        var responseMeans = GroupMeans(y, sample.GroupIds, groups);
        var grand = ColumnMeans(design);
        var grandResponse = y.Average();

        var xd = new double[n][];
        var yd = new double[n];
        for (var row = 0; row < n; row++)
        {
            var group = sample.GroupIds[row];
            xd[row] = new double[design[row].Length];
            for (var column = 0; column < design[row].Length; column++)
            {
                xd[row][column] = design[row][column] - means[group][column] + grand[column];
            }
            yd[row] = y[row] - responseMeans[group] + grandResponse;
        }
        var within = Solve(xd, yd);
        var withinResiduals = Residuals(xd, yd, within);
        var sigma2E = Dot(withinResiduals, withinResiduals) / (n - design[0].Length - groups + 1);

        var between = Solve(means, responseMeans);
        var betweenResiduals = Residuals(means, responseMeans, between);
        var ssr = Dot(betweenResiduals, betweenResiduals);
        var tBar = groups / t.Sum(value => 1.0 / value);
        var sigma2U = Math.Max(0, ssr / (groups - design[0].Length) - sigma2E / tBar);

        sigma2Effects = sigma2U;
        sigma2Residual = sigma2E;
        rho = sigma2U + sigma2E > 0 ? sigma2U / (sigma2U + sigma2E) : 0;

        var theta = new double[groups];
        for (var group = 0; group < groups; group++)
        {
            theta[group] = 1 - Math.Sqrt(sigma2E / (t[group] * sigma2U + sigma2E));
        }
        thetaMean = theta.Average();

        var wx = new double[n][];
        var wy = new double[n];
        for (var row = 0; row < n; row++)
        {
            var group = sample.GroupIds[row];
            wx[row] = new double[design[row].Length];
            for (var column = 0; column < design[row].Length; column++)
            {
                wx[row][column] = design[row][column] - theta[group] * means[group][column];
            }
            wy[row] = y[row] - theta[group] * responseMeans[group];
        }
        var coefficients = Solve(wx, wy);
        var residuals = Residuals(wx, wy, coefficients);
        var cov = Covariance(wx, residuals, covariance, 0, sample.GroupIds);
        return Build(
            ["const", sample.PredictorName],
            wx,
            wy,
            coefficients,
            residuals,
            cov,
            n - coefficients.Length,
            hasConstant: true,
            sample);
    }

    private static PanelFit Build(
        string[] terms,
        double[][] design,
        double[] response,
        double[] coefficients,
        double[] residuals,
        double[][] covariance,
        int dfResid,
        bool hasConstant,
        PanelSample sample,
        bool withinFit = false)
    {
        var k = coefficients.Length;
        var standardErrors = new double[k];
        var pValues = new double[k];
        for (var index = 0; index < k; index++)
        {
            standardErrors[index] = Math.Sqrt(Math.Max(0, covariance[index][index]));
            var tValue = standardErrors[index] > 0 ? coefficients[index] / standardErrors[index] : double.NaN;
            pValues[index] = 2 * (1 - InferenceDistributions.TCdf(Math.Abs(tValue), dfResid));
        }

        double r2Within, r2Between, r2Overall;
        if (withinFit)
        {
            var yWithin = response;
            r2Within = 1 - Dot(residuals, residuals) / Dot(yWithin, yWithin);
            r2Between = R2Between(sample, coefficients, hasConstant);
            r2Overall = R2Overall(sample.Response, sample.Predictor, coefficients, hasConstant);
        }
        else
        {
            r2Within = R2Within(sample, coefficients, hasConstant);
            r2Between = R2Between(sample, coefficients, hasConstant);
            r2Overall = R2Overall(sample.Response, sample.Predictor, coefficients, hasConstant);
        }
        return new PanelFit(
            terms,
            coefficients,
            standardErrors,
            pValues,
            [.. covariance.Select(row => row)],
            residuals,
            design,
            dfResid,
            r2Within,
            r2Between,
            r2Overall);
    }

    // ------------------------------------------------------------------ covarianzas

    private static double[][] Covariance(double[][] design, double[] residuals, string type, int effects, int[] groups)
    {
        var n = design.Length;
        var k = design[0].Length;
        var cross = LinearAlgebra.Multiply(LinearAlgebra.Transpose(design), design);
        var inverse = LinearAlgebra.Inverse(cross);
        if (inverse.IsError) return LinearAlgebra.Identity(k);
        var a = inverse.Value;
        var scale = n / (double)(n - effects - k);

        // Con A = (XᵀX)⁻¹, todos los estimadores quedan V = scale · A · M · A salvo el clásico.
        if (type == "unadjusted") return MatrixBuilder.Scale(a, scale * Dot(residuals, residuals) / n);

        double[][] meat;
        switch (type)
        {
            case "robust":
                meat = LinearAlgebra.Zeros(k, k);
                for (var row = 0; row < n; row++)
                {
                    var value = residuals[row] * residuals[row];
                    for (var i = 0; i < k; i++)
                    {
                        for (var j = 0; j < k; j++) meat[i][j] += value * design[row][i] * design[row][j];
                    }
                }
                break;
            case "kernel":
                meat = KernelMeat(design, residuals, k);
                break;
            default:
                meat = LinearAlgebra.Zeros(k, k);
                var sums = new Dictionary<int, double[]>();
                for (var row = 0; row < n; row++)
                {
                    var group = groups[row];
                    if (!sums.TryGetValue(group, out var accumulator))
                    {
                        accumulator = new double[k];
                        sums[group] = accumulator;
                    }
                    for (var column = 0; column < k; column++) accumulator[column] += design[row][column] * residuals[row];
                }
                foreach (var accumulator in sums.Values)
                {
                    for (var i = 0; i < k; i++)
                    {
                        for (var j = 0; j < k; j++) meat[i][j] += accumulator[i] * accumulator[j];
                    }
                }
                break;
        }

        var product = LinearAlgebra.Multiply(LinearAlgebra.Multiply(a, meat), a);
        return MatrixBuilder.Symmetrize(MatrixBuilder.Scale(product, scale));
    }

    private static double[][] KernelMeat(double[][] design, double[] residuals, int k)
    {
        var n = design.Length;
        var scores = LinearAlgebra.Zeros(n, k);
        for (var row = 0; row < n; row++)
        {
            for (var column = 0; column < k; column++) scores[row][column] = design[row][column] * residuals[row];
        }
        var bandwidth = Math.Floor(4 * Math.Pow(n / 100.0, 2.0 / 9.0));
        var meat = LinearAlgebra.Zeros(k, k);
        for (var lag = 0; lag < n; lag++)
        {
            var weight = 1 - lag / (bandwidth + 1);
            if (weight <= 0) break;
            var factor = lag == 0 ? weight : 2 * weight;
            for (var row = lag; row < n; row++)
            {
                for (var i = 0; i < k; i++)
                {
                    for (var j = 0; j < k; j++) meat[i][j] += factor * scores[row][i] * scores[row - lag][j];
                }
            }
        }
        return MatrixBuilder.Scale(meat, n);
    }

    // ------------------------------------------------------------------ pruebas

    private static (double Statistic, double PValue) PooledF(PanelSample sample, PanelFit fixedFit)
    {
        var n = sample.Response.Length;
        var yMean = sample.Response.Average();
        var xMean = sample.Predictor.Average();
        var design = new double[n][];
        for (var row = 0; row < n; row++) design[row] = [sample.Predictor[row] - xMean];
        var ydm = sample.Response.Select(value => value - yMean).ToArray();
        var pooled = Solve(design, ydm);
        var pooledResiduals = Residuals(design, ydm, pooled);
        var ssrPooled = Dot(pooledResiduals, pooledResiduals);
        var ssrFixed = Dot(fixedFit.Residuals, fixedFit.Residuals);
        var dfNum = n - fixedFit.DfResid - fixedFit.Coefficients.Length - 1;
        var statistic = (ssrPooled - ssrFixed) / dfNum / (ssrFixed / fixedFit.DfResid);
        return (statistic, ExtendedStatistics.FSf(statistic, dfNum, fixedFit.DfResid));
    }

    private static (double Statistic, double PValue) BreuschPagan(PanelSample sample, double[] residuals)
    {
        var n = residuals.Length;
        var groups = sample.GroupLabels.Length;
        var sums = new double[groups];
        for (var row = 0; row < n; row++) sums[sample.GroupIds[row]] += residuals[row];
        var s1 = sums.Sum(value => value * value);
        var s2 = Dot(residuals, residuals);
        var denominator = sample.GroupSizes.Sum(size => (double)size * (size - 1));
        var statistic = n * n * Math.Pow(s1 / s2 - 1, 2) / (2 * denominator);
        return (statistic, ExtendedStatistics.ChiSquareSf(statistic, 1));
    }

    private sealed record HausmanValue(double Statistic, double PValue, int Df);

    private static HausmanValue? Hausman(PanelSample sample, PanelFit fixedFit, PanelFit random, string covariance)
    {
        _ = covariance;
        var n = sample.Response.Length;
        var groups = sample.GroupLabels.Length;
        var fixedClassical = Covariance(fixedFit.Design, fixedFit.Residuals, "unadjusted", groups, sample.GroupIds);
        var randomClassical = Covariance(random.Design, random.Residuals, "unadjusted", 0, sample.GroupIds);
        var difference = fixedClassical[0][0] - randomClassical[1][1];
        if (!(difference > 0)) return null;
        var delta = fixedFit.Coefficients[0] - random.Coefficients[1];
        var statistic = delta * delta / difference;
        return statistic >= 0
            ? new HausmanValue(statistic, ExtendedStatistics.ChiSquareSf(statistic, 1), 1)
            : null;
    }

    private static (string Label, double Value)[] FixedEffects(PanelSample sample, PanelFit fixedFit)
    {
        var groups = sample.GroupLabels.Length;
        var yMeans = GroupMeans(sample.Response, sample.GroupIds, groups);
        var xMeans = GroupMeans(sample.Predictor, sample.GroupIds, groups);
        var effects = new List<(string Label, double Value)>(groups);
        for (var group = 0; group < groups; group++)
        {
            effects.Add((sample.GroupLabels[group], yMeans[group] - xMeans[group] * fixedFit.Coefficients[0]));
        }
        effects.Sort((first, second) => first.Value.CompareTo(second.Value));
        return [.. effects];
    }

    // ------------------------------------------------------------------ utilidades

    private static double[][] WithConstant(double[] predictor)
    {
        var design = new double[predictor.Length][];
        for (var row = 0; row < predictor.Length; row++) design[row] = [1.0, predictor[row]];
        return design;
    }

    private static double[][] WithoutConstant(double[] predictor)
    {
        var design = new double[predictor.Length][];
        for (var row = 0; row < predictor.Length; row++) design[row] = [predictor[row]];
        return design;
    }

    private static double[] Solve(double[][] design, double[] response)
    {
        var k = design[0].Length;
        var cross = LinearAlgebra.Multiply(LinearAlgebra.Transpose(design), design);
        var inverse = LinearAlgebra.Inverse(cross);
        if (inverse.IsError) return new double[k];
        var moment = new double[k];
        for (var row = 0; row < design.Length; row++)
        {
            for (var column = 0; column < k; column++) moment[column] += design[row][column] * response[row];
        }
        var output = new double[k];
        for (var i = 0; i < k; i++)
        {
            for (var j = 0; j < k; j++) output[i] += inverse.Value[i][j] * moment[j];
        }
        return output;
    }

    private static double[] Residuals(double[][] design, double[] response, double[] coefficients)
    {
        var output = new double[response.Length];
        for (var row = 0; row < response.Length; row++)
        {
            var fitted = 0.0;
            for (var column = 0; column < coefficients.Length; column++) fitted += design[row][column] * coefficients[column];
            output[row] = response[row] - fitted;
        }
        return output;
    }

    private static double[] Demean(double[] values, int[] groups)
    {
        var groupCount = groups.Length == 0 ? 0 : groups.Max() + 1;
        var sums = new double[groupCount];
        var counts = new int[groupCount];
        for (var row = 0; row < values.Length; row++)
        {
            sums[groups[row]] += values[row];
            counts[groups[row]]++;
        }
        var output = new double[values.Length];
        for (var row = 0; row < values.Length; row++) output[row] = values[row] - sums[groups[row]] / counts[groups[row]];
        return output;
    }

    private static double[][] Demean(double[][] design, int[] groups)
    {
        var rows = design.Length;
        var columns = design[0].Length;
        var output = new double[rows][];
        for (var row = 0; row < rows; row++) output[row] = new double[columns];
        for (var column = 0; column < columns; column++)
        {
            var values = new double[rows];
            for (var row = 0; row < rows; row++) values[row] = design[row][column];
            var demeaned = Demean(values, groups);
            for (var row = 0; row < rows; row++) output[row][column] = demeaned[row];
        }
        return output;
    }

    private static double[] GroupMeans(double[] values, int[] groups, int groupCount)
    {
        var sums = new double[groupCount];
        var counts = new int[groupCount];
        for (var row = 0; row < values.Length; row++)
        {
            sums[groups[row]] += values[row];
            counts[groups[row]]++;
        }
        var output = new double[groupCount];
        for (var group = 0; group < groupCount; group++) output[group] = sums[group] / counts[group];
        return output;
    }

    private static double[][] GroupMeans(double[][] design, int[] groups, int groupCount)
    {
        var k = design[0].Length;
        var sums = new double[groupCount][];
        var counts = new int[groupCount];
        for (var group = 0; group < groupCount; group++) sums[group] = new double[k];
        for (var row = 0; row < design.Length; row++)
        {
            counts[groups[row]]++;
            for (var column = 0; column < k; column++) sums[groups[row]][column] += design[row][column];
        }
        var output = new double[groupCount][];
        for (var group = 0; group < groupCount; group++)
        {
            output[group] = new double[k];
            for (var column = 0; column < k; column++) output[group][column] = sums[group][column] / counts[group];
        }
        return output;
    }

    private static double[] ColumnMeans(double[][] design)
    {
        var k = design[0].Length;
        var output = new double[k];
        for (var row = 0; row < design.Length; row++)
        {
            for (var column = 0; column < k; column++) output[column] += design[row][column];
        }
        for (var column = 0; column < k; column++) output[column] /= design.Length;
        return output;
    }

    private static double Dot(double[] first, double[] second)
    {
        var total = 0.0;
        for (var index = 0; index < first.Length; index++) total += first[index] * second[index];
        return total;
    }

    private static double R2Within(PanelSample sample, double[] coefficients, bool hasConstant)
    {
        var yWithin = Demean(sample.Response, sample.GroupIds);
        var predictorWithin = Demean(sample.Predictor, sample.GroupIds);
        var coefficient = hasConstant ? coefficients[1] : coefficients[0];
        var residuals = new double[yWithin.Length];
        for (var row = 0; row < yWithin.Length; row++) residuals[row] = yWithin[row] - coefficient * predictorWithin[row];
        return 1 - Dot(residuals, residuals) / Dot(yWithin, yWithin);
    }

    private static double R2Between(PanelSample sample, double[] coefficients, bool hasConstant)
    {
        var groups = sample.GroupLabels.Length;
        var yMeans = GroupMeans(sample.Response, sample.GroupIds, groups);
        var xMeans = GroupMeans(sample.Predictor, sample.GroupIds, groups);
        var residuals = new double[groups];
        for (var group = 0; group < groups; group++)
        {
            var fitted = hasConstant ? coefficients[0] + coefficients[1] * xMeans[group] : coefficients[0] * xMeans[group];
            residuals[group] = yMeans[group] - fitted;
        }
        var mean = hasConstant ? yMeans.Average() : 0;
        var total = 0.0;
        foreach (var value in yMeans) total += Math.Pow(value - mean, 2);
        return total > 0 ? 1 - Dot(residuals, residuals) / total : 0;
    }

    private static double R2Overall(double[] response, double[] predictor, double[] coefficients, bool hasConstant)
    {
        var residuals = new double[response.Length];
        for (var row = 0; row < response.Length; row++)
        {
            var fitted = hasConstant ? coefficients[0] + coefficients[1] * predictor[row] : coefficients[0] * predictor[row];
            residuals[row] = response[row] - fitted;
        }
        var mean = hasConstant ? response.Average() : 0;
        var total = 0.0;
        foreach (var value in response) total += Math.Pow(value - mean, 2);
        return total > 0 ? 1 - Dot(residuals, residuals) / total : 0;
    }
}
