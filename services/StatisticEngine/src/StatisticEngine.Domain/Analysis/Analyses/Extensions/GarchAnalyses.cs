using System.Globalization;
using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;
using StatisticEngine.Domain.TimeSeries;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>
/// Modelos de volatilidad condicional de la familia GARCH (GARCH, ARCH, GJR y TARCH) con
/// distribuciones normal y t de Student. Porta la parte numérica de <c>sigma.domain.analysis.econ_models._garch</c>:
/// estimación por máxima verosimilitud, persistencia, diagnóstico (ARCH-LM, Ljung-Box y Jarque-Bera),
/// valor en riesgo con la prueba de Kupiec y pronóstico analítico de la varianza.
/// </summary>
/// <remarks>
/// El vector dorado de Sigma se generó con el punto donde se detuvo SLSQP de SciPy, no con el máximo de
/// verosimilitud: por eso la variante concreta del ejemplo no alcanza la tolerancia de paridad (ver
/// <c>EXTENSIONS-NOTES.md</c>). El análisis queda implementado pero no registrado hasta que Sigma
/// regenere el vector con un óptimo convergente.
/// </remarks>
internal static class GarchAnalyses
{
    private const int MinimumObservations = 100;

    private static readonly AnalysisParamOption[] VariantOptions =
    [
        new("garch", "GARCH(p, q)"),
        new("gjr", "GJR-GARCH (efecto apalancamiento)"),
        new("egarch", "EGARCH (logarítmico, asimétrico)"),
        new("tarch", "TARCH / Zakoian (desviación estándar)"),
        new("aparch", "APARCH"),
        new("arch", "ARCH(q)")
    ];

    private static readonly AnalysisParamOption[] DistributionOptions =
    [
        new("normal", "Normal"),
        new("t", "t de Student"),
        new("skewt", "t asimétrica"),
        new("ged", "Error generalizado (GED)")
    ];

    private static readonly AnalysisParamOption[] MeanOptions =
    [
        new("constant", "Constante"),
        new("zero", "Cero"),
        new("ar", "Autorregresivo")
    ];

    private static readonly AnalysisParamOption[] InputOptions =
    [
        new("returns", "Rendimientos"),
        new("prices", "Precios (se calculan rendimientos logarítmicos)")
    ];

    public static readonly AnalysisDefinition Garch = new(
        "garch",
        "GARCH, EGARCH, GJR y volatilidad",
        "Econometría",
        "Volatilidad condicional con asimetría y colas pesadas: parámetros, persistencia, diagnóstico, "
        + "valor en riesgo con prueba de Kupiec y pronóstico.",
        [],
        [],
        [
            new(
                "variant",
                "Modelo de volatilidad",
                AnalysisParamKinds.Select,
                "gjr",
                VariantOptions,
                false),
            new(
                "p",
                "p (rezagos de σ²)",
                AnalysisParamKinds.Integer,
                1.0,
                [],
                false,
                Minimum: 0,
                Maximum: 4,
                VisibleIf: new AnalysisVisibleIf("variant", ["garch", "gjr", "egarch", "tarch", "aparch"])),
            new(
                "o",
                "o (términos asimétricos)",
                AnalysisParamKinds.Integer,
                1.0,
                [],
                false,
                Minimum: 1,
                Maximum: 3,
                VisibleIf: new AnalysisVisibleIf("variant", ["gjr", "egarch", "tarch", "aparch"])),
            new(
                "q",
                "q (rezagos de ε²)",
                AnalysisParamKinds.Integer,
                1.0,
                [],
                false,
                Minimum: 1,
                Maximum: 4),
            new(
                "mean",
                "Modelo de la media",
                AnalysisParamKinds.Select,
                "constant",
                MeanOptions,
                false),
            new(
                "ar_lags",
                "Rezagos AR de la media",
                AnalysisParamKinds.Integer,
                1.0,
                [],
                false,
                Minimum: 1,
                Maximum: 5,
                VisibleIf: new AnalysisVisibleIf("mean", ["ar"])),
            new(
                "distribution",
                "Distribución de los errores",
                AnalysisParamKinds.Select,
                "t",
                DistributionOptions,
                false),
            new(
                "input",
                "Los datos son",
                AnalysisParamKinds.Select,
                "returns",
                InputOptions,
                false),
            new(
                "percent",
                "Multiplicar por 100 (rendimientos en %)",
                AnalysisParamKinds.Boolean,
                true,
                [],
                false,
                Help: "Mejora la estabilidad numérica de la optimización."),
            new(
                "var_level",
                "Cola del valor en riesgo",
                AnalysisParamKinds.Number,
                0.05,
                [],
                false,
                Minimum: 0.001,
                Maximum: 0.2),
            new(
                "horizon",
                "Periodos a pronosticar (0 = no)",
                AnalysisParamKinds.Integer,
                10.0,
                [],
                false,
                Minimum: 0,
                Maximum: 250),
            ExtendedHelpers.Alpha()
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Volatilidad y regímenes",
        Roles:
        [
            new ColumnRole("series", "Rendimientos (o precios)", ColumnKinds.Numeric),
            new ColumnRole(
                "time",
                "Columna de tiempo (opcional)",
                ColumnKinds.DateTime,
                Optional: true,
                Help: "Sin ella se usa el orden de las filas.")
        ],
        Outputs: [new AnalysisOutputSpec("volatilidad", "Volatilidad condicional", Required: false)],
        Heavy: true,
        NeedsDataFrame: true);

    private enum Family
    {
        Garch,
        Arch,
        Gjr,
        Tarch
    }

    private enum Distribution
    {
        Normal,
        StudentT
    }

    private sealed record ModelSpec(
        Family Family,
        int P,
        int O,
        int Q,
        double Power,
        bool ConstantMean,
        Distribution Distribution)
    {
        public int MeanCount => ConstantMean ? 1 : 0;

        public int VolatilityCount => 1 + P + O + Q;

        public int DistributionCount => Distribution == Distribution.Normal ? 0 : 1;

        public int TotalCount => MeanCount + VolatilityCount + DistributionCount;

        public int VolatilityOffset => MeanCount;

        public int DistributionOffset => MeanCount + VolatilityCount;
    }

    private sealed record FilterContext(double Backcast, double[][] Bounds);

    private sealed record FitOutcome(
        ModelSpec Spec,
        double[] Parameters,
        string[] Names,
        double LogLikelihood,
        double Aic,
        double Bic,
        double[] Residuals,
        double[] ConditionalVariance,
        double[] ConditionalVolatility,
        double[] StandardizedResiduals,
        double? Persistence,
        double? HalfLife,
        double? UnconditionalVariance,
        double[][] Covariance);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var seriesName = ExtendedHelpers.FirstRole(input, "series", "Rendimientos (o precios)");
        var timeColumn = ExtendedHelpers.FirstRole(input, "time", "Columna de tiempo");
        var variant = input.Pstr("variant", "gjr");
        var p = input.Pint("p", 1);
        var o = input.Pint("o", 1);
        var q = input.Pint("q", 1);
        var mean = input.Pstr("mean", "constant");
        var distributionName = input.Pstr("distribution", "t");
        var inputMode = input.Pstr("input", "returns");
        var percent = input.Pbool("percent", true);
        var level = input.Pnum("var_level", 0.05);
        var horizon = input.Pint("horizon", 10);
        var alpha = input.Pnum("alpha", 0.05);

        var (family, power, volumeP, volumeO, volumeQ) = variant switch
        {
            "garch" => (Family.Garch, 2.0, p, 0, q),
            "arch" => (Family.Arch, 2.0, q, 0, 0),
            "gjr" => (Family.Gjr, 2.0, p, Math.Max(o, 1), q),
            "tarch" => (Family.Tarch, 1.0, p, Math.Max(o, 1), q),
            _ => (Family.Garch, 0.0, 0, 0, 0)
        };

        if (family == Family.Garch && power == 0)
        {
            return ExtendedHelpers.Error(
                DomainErrorCode.NotSupported,
                "El modelo EGARCH y el APARCH todavía no están disponibles; use GARCH, ARCH, GJR o TARCH.");
        }

        var distribution = distributionName switch
        {
            "normal" => Distribution.Normal,
            "t" => Distribution.StudentT,
            _ => (Distribution)(-1)
        };

        if ((int)distribution < 0)
        {
            return ExtendedHelpers.Error(
                DomainErrorCode.NotSupported,
                "Por ahora solo están disponibles las distribuciones normal y t de Student; "
                + "la t asimétrica y la GED quedan pendientes.");
        }

        if (mean == "ar")
        {
            return ExtendedHelpers.Error(
                DomainErrorCode.NotSupported,
                "El modelo de media autorregresivo (AR) todavía no está disponible; use media constante o cero.");
        }

        if (volumeP < 0 || volumeQ < 0 || volumeP > 4 || volumeQ > 4 || volumeO > 3)
        {
            return ExtendedHelpers.Invalid("Los órdenes del modelo no son válidos.");
        }

        if (volumeP == 0 && volumeO == 0)
        {
            return ExtendedHelpers.Invalid("Un modelo ARCH necesita al menos un rezago (q ≥ 1).");
        }

        if (inputMode is not ("returns" or "prices"))
        {
            return ExtendedHelpers.Invalid("El tipo de datos de entrada no es válido.");
        }

        if (alpha is <= 0 or >= 1)
        {
            return ExtendedHelpers.Invalid("El nivel de significancia debe estar entre 0 y 1.");
        }

        var loaded = SeriesLoader.Load(frame, seriesName, timeColumn, 0, MinimumObservations);
        if (loaded.IsError) return Result.Err<AnalysisResult>(loaded.Error.Code, loaded.Error.Message);
        var series = loaded.Value;

        var values = series.Values;
        var labels = series.Labels;
        if (inputMode == "prices")
        {
            if (values.Any(value => value <= 0))
            {
                return ExtendedHelpers.Invalid(
                    "Para pasar de precios a rendimientos los precios deben ser positivos.");
            }

            var returns = new double[values.Length - 1];
            for (var index = 0; index < returns.Length; index++)
            {
                returns[index] = Math.Log(values[index + 1] / values[index]);
            }
            values = returns;
            labels = labels[1..];
        }

        var y = new double[values.Length];
        for (var index = 0; index < values.Length; index++) y[index] = percent ? values[index] * 100 : values[index];

        if (StandardDeviation(y, 0) == 0)
        {
            return ExtendedHelpers.Invalid("La serie es constante.");
        }

        var spec = new ModelSpec(
            family,
            volumeP,
            volumeO,
            volumeQ,
            power,
            mean != "zero",
            distribution);

        var fit = Fit(y, spec);
        if (fit is null)
        {
            return ExtendedHelpers.Error(
                DomainErrorCode.NonConvergence,
                "No se pudo ajustar el modelo de volatilidad: el optimizador no convergió.");
        }

        var n = y.Length;
        var names = fit.Names;
        var parameters = fit.Parameters;
        var persistence = fit.Persistence;
        var halfLife = fit.HalfLife;
        var unconditional = fit.UnconditionalVariance;
        var standardized = fit.StandardizedResiduals;
        var volatility = fit.ConditionalVolatility;
        var residuals = fit.Residuals;

        var rawLm = ArchLagrangeMultiplier(Subtract(y, Mean(y)), 5);
        var lb = LjungBox.Compute(standardized, 10);
        var lb2 = LjungBox.Compute(standardized.Select(value => value * value).ToArray(), 10);
        var residualLm = ArchLagrangeMultiplier(standardized, 5);
        var jarqueBera = JarqueBera(standardized);

        var ci = ConfidenceIntervals(parameters, fit.Covariance, alpha);


        var title = $"Modelo de volatilidad {VariantTitle(variant)}: {series.Name}";
        var subtitle = $"distribución {DistributionName(distribution)} · n = {n}"
                       + (inputMode == "prices" ? " · rendimientos logarítmicos ×100 a partir de precios" : string.Empty);

        var sections = new List<AnalysisSection>
        {
            new TilesSection(
                "Resumen",
                [
                    new TileItem("Observaciones", n.ToString(CultureInfo.InvariantCulture)),
                    new TileItem("Log-verosimilitud", ExtendedHelpers.N(fit.LogLikelihood)),
                    new TileItem("AIC", ExtendedHelpers.N(fit.Aic)),
                    new TileItem("BIC", ExtendedHelpers.N(fit.Bic)),
                    new TileItem("Persistencia", persistence is { } value ? ExtendedHelpers.N(value) : "—")
                ]),
            new TableSection(
                "Parámetros",
                [
                    "Parámetro",
                    "Estimación",
                    "Error est.",
                    "t",
                    "Valor p",
                    $"IC {100 * (1 - alpha):g} % inf.",
                    "IC sup."
                ],
                names.Select((name, index) =>
                {
                    var stdError = Math.Sqrt(fit.Covariance[index][index]);
                    var estimate = parameters[index];
                    var statistic = stdError > 0 ? estimate / stdError : double.NaN;
                    return (IReadOnlyList<string>)
                    [
                        ParameterLabel(name),
                        ExtendedHelpers.N(estimate),
                        ExtendedHelpers.N(stdError),
                        ExtendedHelpers.N(statistic),
                        ExtendedHelpers.P(ExtendedHelpers.NormalTwoSided(statistic)),
                        ExtendedHelpers.N(ci[index][0]),
                        ExtendedHelpers.N(ci[index][1])                    ];
                }).ToList())
        };

        var facts = new List<IReadOnlyList<string>>();
        if (persistence is { } rho) facts.Add([ "Persistencia", ExtendedHelpers.N(rho) ]);
        if (halfLife is { } life) facts.Add([ "Vida media de un choque (periodos)", ExtendedHelpers.N(life) ]);
        if (unconditional is { } variance)
        {
            facts.Add(["Varianza incondicional", ExtendedHelpers.N(variance)]);
            facts.Add(["Volatilidad incondicional", ExtendedHelpers.N(Math.Sqrt(variance))]);
        }

        sections.Add(new TableSection(
            "Persistencia de la volatilidad",
            ["Medida", "Valor"],
            facts.Count > 0 ? facts : [[ "Persistencia", "*" ]],
            Note: persistence is not null
                ? "La persistencia cercana a 1 indica que los choques de volatilidad tardan mucho en disiparse; "
                  + "≥ 1 sería no estacionario (IGARCH)."
                : null));

        sections.Add(new TableSection(
            "Diagnóstico",
            ["Prueba", "Estadístico", "Valor p", "Lectura"],
            [
                [
                    "Efectos ARCH en la serie (LM, 5 rezagos)",
                    ExtendedHelpers.N(rawLm.Statistic),
                    ExtendedHelpers.P(rawLm.PValue),
                    rawLm.PValue < 0.05
                        ? "hay heterocedasticidad condicional: un modelo de volatilidad tiene sentido"
                        : "poca evidencia de efectos ARCH"
                ],
                [
                    "Ljung-Box de los residuos estandarizados",
                    ExtendedHelpers.N(lb.Statistic),
                    ExtendedHelpers.P(lb.PValue),
                    lb.PValue >= 0.05 ? "la media está bien especificada" : "queda autocorrelación en la media"
                ],
                [
                    "Ljung-Box de los residuos² estandarizados",
                    ExtendedHelpers.N(lb2.Statistic),
                    ExtendedHelpers.P(lb2.PValue),
                    lb2.PValue >= 0.05
                        ? "la varianza está bien modelada"
                        : "queda heterocedasticidad: aumente p o q, o cambie de modelo"
                ],
                [
                    "ARCH-LM de los residuos estandarizados",
                    ExtendedHelpers.N(residualLm.Statistic),
                    ExtendedHelpers.P(residualLm.PValue),
                    residualLm.PValue >= 0.05 ? "sin efectos ARCH residuales" : "aún hay efectos ARCH"
                ],
                [
                    "Jarque-Bera de los residuos estandarizados",
                    ExtendedHelpers.N(jarqueBera.Statistic),
                    ExtendedHelpers.P(jarqueBera.PValue),
                    jarqueBera.PValue >= 0.05
                        ? "compatible con normalidad"
                        : "colas pesadas: considere la distribución t"
                ]
            ]));

        var axis = Enumerable.Range(1, n).Select(index => (double)index).ToArray();
        var xLabel = series.Dated ? "Tiempo" : "Observación";
        var charts = new List<ChartSpec>
        {
            new SeriesChart(
                "Volatilidad condicional estimada",
                [new XYSeries("σ(t)", axis, volatility)],
                xLabel,
                "Desviación estándar condicional")
        };

        var quantile = Quantile(spec, parameters, level);
        var meanAt = Subtract(y, residuals);
        var valueAtRisk = new double[n];
        for (var index = 0; index < n; index++) valueAtRisk[index] = -(meanAt[index] + volatility[index] * quantile);
        var hits = y.Where((value, index) => value < -valueAtRisk[index]).Count();
        var kupiec = Kupiec(hits, n, level);

        sections.Add(new TableSection(
            $"Valor en riesgo al {100 * (1 - level):g} % (1 día)",
            ["Violaciones", "Esperadas", "Tasa observada", "Kupiec LR", "Valor p"],
            [
                [
                    hits.ToString(CultureInfo.InvariantCulture),
                    ExtendedHelpers.N(level * n),
                    ExtendedHelpers.N(hits / (double)n),
                    ExtendedHelpers.N(kupiec.Statistic),
                    ExtendedHelpers.P(kupiec.PValue)
                ]
            ],
            Note: "VaR = −(media + σ(t)·cuantil); una violación es un rendimiento peor que −VaR. "
                  + "Kupiec contrasta que la tasa de violaciones sea igual a la nominal."
                  + (percent ? " Los rendimientos están en %." : string.Empty)));

        charts.Add(new SeriesChart(
            "Rendimientos y valor en riesgo",
            [
                new XYSeries("Rendimiento", axis, y),
                new XYSeries("−VaR", axis, valueAtRisk.Select(value => -value).ToArray(), "dash")
            ],
            xLabel,
            "Rendimiento"));

        if (horizon > 0)
        {
            var forecast = ForecastVariance(spec, residuals, fit.ConditionalVariance, parameters, horizon);
            var future = FutureLabels(series, labels, horizon);
            sections.Add(new TableSection(
                "Pronóstico de la volatilidad",
                ["Periodo", "Varianza", "Desviación estándar"],
                Enumerable.Range(0, horizon)
                    .Select(index => (IReadOnlyList<string>)
                    [
                        future[index],
                        ExtendedHelpers.N(forecast[index]),
                        ExtendedHelpers.N(Math.Sqrt(forecast[index]))
                    ])
                    .ToList()));
        }

        var parameterMap = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var index = 0; index < names.Length; index++) parameterMap[names[index]] = parameters[index];

        return Result.Ok(new AnalysisResult(
            "garch",
            title,
            sections,
            Subtitle: subtitle,
            Charts: charts,
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
            {
                ["volatilidad"] = new DatasetResult(
                    "Volatilidad condicional",
                    [
                        DataColumn.Text("tiempo", labels.Select(label => (string?)label).ToArray()),
                        DataColumn.Numeric("rendimiento", y),
                        DataColumn.Numeric("volatilidad", volatility),
                        DataColumn.Numeric("residuo_estandarizado", standardized),
                        DataColumn.Numeric("VaR", valueAtRisk)
                    ])
            },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["persistence"] = persistence,
                ["half_life"] = halfLife,
                ["aic"] = fit.Aic,
                ["bic"] = fit.Bic,
                ["parameters"] = parameterMap,
                ["lm_p"] = rawLm.PValue,
                ["residual_lm_p"] = residualLm.PValue,
                ["var_hits"] = hits,
                ["kupiec_p"] = kupiec.PValue
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static FitOutcome? Fit(double[] y, ModelSpec spec)
    {
        var meanStart = spec.ConstantMean ? Mean(y) : 0;
        var baseResiduals = spec.ConstantMean ? Subtract(y, meanStart) : (double[])y.Clone();
        var context = BuildContext(baseResiduals, spec.Power);
        var omegaLower = 1e-8 * MeanAbsolutePower(baseResiduals, spec.Power);
        var omegaUpper = 10 * MeanAbsolutePower(baseResiduals, spec.Power);
        var scale = BuildScale(y, spec, baseResiduals, omegaLower, omegaUpper);

        var starts = new List<double[]>();
        var volatilityStart = VolatilityStartingValues(spec, baseResiduals, context);
        starts.Add(ComposeStart(spec, meanStart, baseResiduals, volatilityStart, context));
        foreach (var (alpha, beta) in new[] { (0.05, 0.90), (0.10, 0.80), (0.20, 0.65) })
        {
            if (spec.P == 0 || spec.Q == 0) break;
            var candidate = (double[])volatilityStart.Clone();
            candidate[0] = Math.Clamp((1 - alpha - beta) * Variance(baseResiduals, 0), omegaLower, omegaUpper);
            candidate[1] = alpha;
            candidate[^1] = beta;
            starts.Add(ComposeStart(spec, meanStart, baseResiduals, candidate, context));
        }

        double[]? best = null;
        var bestValue = double.PositiveInfinity;
        foreach (var start in starts)
        {
            var optimized = Minimize(
                parameters => Objective(spec, y, parameters, omegaLower, omegaUpper, context),
                start,
                scale);
            if (optimized is null) continue;
            var value = Objective(spec, y, optimized, omegaLower, omegaUpper, context);
            if (value < bestValue)
            {
                best = optimized;
                bestValue = value;
            }
        }

        if (best is null || !double.IsFinite(bestValue)) return null;

        var residuals = MeanResiduals(y, spec, best);
        if (HasPenalty(spec, best, omegaLower, omegaUpper)) return null;

        ComputeVariance(spec, residuals, best[spec.VolatilityOffset..(spec.VolatilityOffset + spec.VolatilityCount)], context, out var variance);
        var volatility = variance.Select(Math.Sqrt).ToArray();
        var standardized = new double[y.Length];
        for (var index = 0; index < y.Length; index++) standardized[index] = residuals[index] / volatility[index];

        var logLikelihood = -NegativeLogLikelihood(y, spec, best, context);
        var count = best.Length;
        var aic = -2 * logLikelihood + 2 * count;
        var bic = -2 * logLikelihood + Math.Log(y.Length) * count;

        double? persistence = null;
        double? halfLife = null;
        double? unconditional = null;
        if (spec.Family is Family.Garch or Family.Arch or Family.Gjr)
        {
            var vol = best[spec.VolatilityOffset..(spec.VolatilityOffset + spec.VolatilityCount)];
            persistence = VolatilityPersistence(spec, vol);
            if (persistence is > 0 and < 1)
            {
                halfLife = Math.Log(0.5) / Math.Log(persistence.Value);
                unconditional = vol[0] / (1 - persistence.Value);
            }
        }

        var covariance = CovarianceMatrix(y, spec, best, context);
        if (covariance is null) return null;

        return new FitOutcome(
            spec,
            best,
            ParameterNames(spec),
            logLikelihood,
            aic,
            bic,
            residuals,
            variance,
            volatility,
            standardized,
            persistence,
            halfLife,
            unconditional,
            covariance);
    }

    private static double[] ComposeStart(
        ModelSpec spec,
        double mean,
        double[] baseResiduals,
        double[] volatility,
        FilterContext context)
    {
        var parameters = new double[spec.TotalCount];
        var offset = 0;
        if (spec.ConstantMean) parameters[offset++] = mean;
        for (var index = 0; index < volatility.Length; index++) parameters[offset + index] = volatility[index];
        offset += volatility.Length;
        if (spec.Distribution == Distribution.StudentT)
        {
            ComputeVariance(spec, baseResiduals, volatility, context, out var variance);
            var standardized = new double[baseResiduals.Length];
            for (var index = 0; index < baseResiduals.Length; index++)
            {
                var vol = Math.Sqrt(variance[index]);
                standardized[index] = vol > 0 ? baseResiduals[index] / vol : 0;
            }
            var kurtosis = KurtosisBiased(standardized);
            parameters[offset] = Math.Max(kurtosis > 3.75 ? (4 * kurtosis - 6) / (kurtosis - 3) : 12, 4);
        }
        return parameters;
    }

    private static double[] BuildScale(double[] y, ModelSpec spec, double[] baseResiduals, double omegaLower, double omegaUpper)
    {
        var scale = new double[spec.TotalCount];
        var offset = 0;
        if (spec.ConstantMean) scale[offset++] = Math.Max(1e-8, StandardDeviation(y, 1));
        var variance = Math.Max(1e-8, Variance(baseResiduals, 0));
        scale[offset++] = Math.Max(1e-8, omegaUpper - omegaLower);
        for (var index = 0; index < spec.P + spec.O + spec.Q; index++) scale[offset + index] = 0.1;
        offset += spec.P + spec.O + spec.Q;
        if (spec.Distribution == Distribution.StudentT) scale[offset] = 10;
        return scale;
    }

    private static double[] VolatilityStartingValues(ModelSpec spec, double[] residuals, FilterContext context)
    {
        var target = MeanAbsolutePower(residuals, spec.Power);
        var scale = Variance(residuals, 0) / Math.Pow(target, 2 / spec.Power);
        target *= Math.Pow(scale, spec.Power / 2);

        double[]? best = null;
        var bestLikelihood = double.NegativeInfinity;
        foreach (var alphaValue in new[] { 0.01, 0.05, 0.1, 0.2 })
        {
            foreach (var gammaValue in new[] { 0.01, 0.05, 0.1, 0.2 })
            {
                foreach (var abgValue in new[] { 0.5, 0.7, 0.9, 0.98 })
                {
                    var parameters = new double[spec.VolatilityCount];
                    for (var index = 0; index < parameters.Length; index++) parameters[index] = (1 - abgValue) * target;
                    var abg = abgValue;
                    if (spec.P > 0)
                    {
                        for (var index = 0; index < spec.P; index++) parameters[1 + index] = alphaValue / spec.P;
                        abg -= alphaValue;
                    }
                    if (spec.O > 0)
                    {
                        for (var index = 0; index < spec.O; index++)
                        {
                            parameters[1 + spec.P + index] = gammaValue / spec.O;
                        }
                        abg -= gammaValue / 2.0;
                    }
                    if (spec.Q > 0)
                    {
                        for (var index = 0; index < spec.Q; index++)
                        {
                            parameters[1 + spec.P + spec.O + index] = abg / spec.Q;
                        }
                    }
                    var likelihood = GaussianLogLikelihood(spec, residuals, parameters, context);
                    if (likelihood > bestLikelihood)
                    {
                        bestLikelihood = likelihood;
                        best = (double[])parameters.Clone();
                    }
                }
            }
        }
        return best ?? throw new InvalidOperationException("No se pudo construir un valor inicial para la volatilidad.");
    }

    private static double GaussianLogLikelihood(ModelSpec spec, double[] residuals, double[] volatility, FilterContext context)
    {
        ComputeVariance(spec, residuals, volatility, context, out var variance);
        var total = 0.0;
        for (var index = 0; index < residuals.Length; index++)
        {
            total += -0.5 * (Math.Log(2 * Math.PI) + Math.Log(variance[index]) + residuals[index] * residuals[index] / variance[index]);
        }
        return total;
    }

    private static double NegativeLogLikelihood(double[] y, ModelSpec spec, double[] parameters, FilterContext context)
    {
        var residuals = MeanResiduals(y, spec, parameters);
        var volatility = parameters[spec.VolatilityOffset..(spec.VolatilityOffset + spec.VolatilityCount)];
        ComputeVariance(spec, residuals, volatility, context, out var variance);
        var total = 0.0;
        if (spec.Distribution == Distribution.Normal)
        {
            for (var index = 0; index < residuals.Length; index++)
            {
                total += -0.5 * (Math.Log(2 * Math.PI) + Math.Log(variance[index]) + residuals[index] * residuals[index] / variance[index]);
            }
            return -total;
        }

        var nu = parameters[spec.DistributionOffset];
        var constant = SpecialFunctions.LogGamma((nu + 1) / 2) - SpecialFunctions.LogGamma(nu / 2) - 0.5 * Math.Log(Math.PI * (nu - 2));
        for (var index = 0; index < residuals.Length; index++)
        {
            total += constant
                     - 0.5 * Math.Log(variance[index])
                     - ((nu + 1) / 2) * Math.Log(1 + residuals[index] * residuals[index] / (variance[index] * (nu - 2)));
        }
        return -total;
    }

    private static double[] IndividualNegativeLogLikelihood(double[] y, ModelSpec spec, double[] parameters, FilterContext context)
    {
        var residuals = MeanResiduals(y, spec, parameters);
        var volatility = parameters[spec.VolatilityOffset..(spec.VolatilityOffset + spec.VolatilityCount)];
        ComputeVariance(spec, residuals, volatility, context, out var variance);
        var output = new double[residuals.Length];
        if (spec.Distribution == Distribution.Normal)
        {
            for (var index = 0; index < residuals.Length; index++)
            {
                output[index] = 0.5 * (Math.Log(2 * Math.PI) + Math.Log(variance[index])
                                       + residuals[index] * residuals[index] / variance[index]);
            }
            return output;
        }

        var nu = parameters[spec.DistributionOffset];
        var constant = SpecialFunctions.LogGamma((nu + 1) / 2) - SpecialFunctions.LogGamma(nu / 2) - 0.5 * Math.Log(Math.PI * (nu - 2));
        for (var index = 0; index < residuals.Length; index++)
        {
            output[index] = -constant
                            + 0.5 * Math.Log(variance[index])
                            + ((nu + 1) / 2) * Math.Log(1 + residuals[index] * residuals[index] / (variance[index] * (nu - 2)));
        }
        return output;
    }

    private static double[] MeanResiduals(double[] y, ModelSpec spec, double[] parameters)
    {
        var residuals = new double[y.Length];
        if (!spec.ConstantMean)
        {
            for (var index = 0; index < y.Length; index++) residuals[index] = y[index];
            return residuals;
        }
        var mean = parameters[0];
        for (var index = 0; index < y.Length; index++) residuals[index] = y[index] - mean;
        return residuals;
    }

    private static void ComputeVariance(
        ModelSpec spec,
        double[] residuals,
        double[] parameters,
        FilterContext context,
        out double[] variance)
    {
        var n = residuals.Length;
        var power = spec.Power;
        var fresids = new double[n];
        for (var index = 0; index < n; index++) fresids[index] = Math.Pow(Math.Abs(residuals[index]), power);

        var backcast = context.Backcast;
        var bounds = context.Bounds;
        var transformed = new double[n];
        for (var index = 0; index < n; index++)
        {
            var value = parameters[0];
            var location = 1;
            for (var lag = 0; lag < spec.P; lag++)
            {
                value += parameters[location++] * (index - 1 - lag < 0 ? backcast : fresids[index - 1 - lag]);
            }
            for (var lag = 0; lag < spec.O; lag++)
            {
                if (index - 1 - lag < 0)
                {
                    value += parameters[location++] * 0.5 * backcast;
                }
                else
                {
                    value += parameters[location++] * fresids[index - 1 - lag]
                             * (residuals[index - 1 - lag] < 0 ? 1.0 : 0.0);
                }
            }
            for (var lag = 0; lag < spec.Q; lag++)
            {
                value += parameters[location++] * (index - 1 - lag < 0 ? backcast : transformed[index - 1 - lag]);
            }
            transformed[index] = Math.Clamp(value, bounds[index][0], bounds[index][1]);
        }

        variance = new double[n];
        var inverse = 2.0 / power;
        for (var index = 0; index < n; index++) variance[index] = Math.Pow(transformed[index], inverse);
    }

    private static FilterContext BuildContext(double[] residuals, double power) =>
        new(Backcast(residuals, power), VarianceBounds(residuals, power));

    private static double Backcast(double[] residuals, double power)
    {
        var tau = Math.Min(75, residuals.Length);
        var weights = new double[tau];
        var totalWeight = 0.0;
        for (var index = 0; index < tau; index++)
        {
            weights[index] = Math.Pow(0.94, index);
            totalWeight += weights[index];
        }
        var total = 0.0;
        for (var index = 0; index < tau; index++)
        {
            total += Math.Pow(Math.Abs(residuals[index]), power) * weights[index] / totalWeight;
        }
        return total;
    }

    private static double[][] VarianceBounds(double[] residuals, double power)
    {
        var n = residuals.Length;
        var tau = Math.Min(75, n);
        var weights = new double[tau];
        var totalWeight = 0.0;
        for (var index = 0; index < tau; index++)
        {
            weights[index] = Math.Pow(0.94, index);
            totalWeight += weights[index];
        }
        var initial = 0.0;
        for (var index = 0; index < tau; index++) initial += residuals[index] * residuals[index] * weights[index] / totalWeight;

        var smoothing = new double[n];
        for (var index = 0; index < n; index++)
        {
            smoothing[index] = index == 0
                ? initial
                : 0.94 * smoothing[index - 1] + 0.06 * residuals[index - 1] * residuals[index - 1];
        }

        var variance = Variance(residuals, 0);
        var maxSquare = residuals.Max(value => value * value);
        var lowerBound = variance / 1e8;
        var minimumUpper = 1 + maxSquare;
        var upperBound = 1e7 * (1 + maxSquare);
        var bounds = new double[n][];
        for (var index = 0; index < n; index++)
        {
            var lower = Math.Max(smoothing[index] / 1e6, lowerBound);
            var upper = Math.Min(Math.Max(smoothing[index] * 1e6, minimumUpper), upperBound);
            if (power != 2.0)
            {
                lower = Math.Pow(lower, power / 2.0);
                upper = Math.Pow(upper, power / 2.0);
            }
            bounds[index] = [lower, upper];
        }
        return bounds;
    }

    private static double Objective(
        ModelSpec spec,
        double[] y,
        double[] parameters,
        double omegaLower,
        double omegaUpper,
        FilterContext context)
    {
        var penalty = Penalty(spec, parameters, omegaLower, omegaUpper);
        var likelihood = NegativeLogLikelihood(y, spec, parameters, context);
        if (!double.IsFinite(likelihood)) return 1e12 + penalty;
        return likelihood + penalty;
    }

    private static double Penalty(
        ModelSpec spec,
        double[] parameters,
        double omegaLower,
        double omegaUpper)
    {
        var violation = 0.0;
        void Add(double value)
        {
            if (value > 0) violation += value * value;
        }

        var location = spec.VolatilityOffset;
        var omega = parameters[location++];
        Add(omegaLower - omega);
        Add(omega - omegaUpper);
        for (var index = 0; index < spec.P; index++)
        {
            var alpha = parameters[location++];
            Add(-alpha);
            Add(alpha - 1);
        }
        for (var index = 0; index < spec.O; index++)
        {
            var gamma = parameters[location++];
            Add(-1 - gamma);
            Add(gamma - 2);
        }
        for (var index = 0; index < spec.Q; index++)
        {
            var beta = parameters[location++];
            Add(-beta);
            Add(beta - 1);
        }

        var persistence = PersistenceFromVector(spec, parameters);
        Add(persistence - 1);

        if (spec.Distribution == Distribution.StudentT)
        {
            var nu = parameters[spec.DistributionOffset];
            Add(2.05 - nu);
            Add(nu - 500);
        }

        return 1e10 * violation;
    }

    private static bool HasPenalty(ModelSpec spec, double[] parameters, double omegaLower, double omegaUpper) =>
        Penalty(spec, parameters, omegaLower, omegaUpper) > 0;

    private static double PersistenceFromVector(ModelSpec spec, double[] parameters)
    {
        var location = spec.VolatilityOffset + 1;
        var persistence = 0.0;
        for (var index = 0; index < spec.P; index++) persistence += parameters[location++];
        for (var index = 0; index < spec.O; index++) persistence += 0.5 * parameters[location++];
        for (var index = 0; index < spec.Q; index++) persistence += parameters[location++];
        return persistence;
    }

    private static double VolatilityPersistence(ModelSpec spec, double[] volatility)
    {
        var location = 1;
        var persistence = 0.0;
        for (var index = 0; index < spec.P; index++) persistence += volatility[location++];
        for (var index = 0; index < spec.O; index++) persistence += 0.5 * volatility[location++];
        for (var index = 0; index < spec.Q; index++) persistence += volatility[location++];
        return persistence;
    }

    private static string[] ParameterNames(ModelSpec spec)
    {
        var names = new List<string>();
        if (spec.ConstantMean) names.Add("mu");
        names.Add("omega");
        for (var index = 1; index <= spec.P; index++) names.Add($"alpha[{index}]");
        for (var index = 1; index <= spec.O; index++) names.Add($"gamma[{index}]");
        for (var index = 1; index <= spec.Q; index++) names.Add($"beta[{index}]");
        if (spec.Distribution == Distribution.StudentT) names.Add("nu");
        return names.ToArray();
    }

    private static string ParameterLabel(string name)
    {
        if (name == "mu") return "μ (media)";
        if (name == "omega") return "ω (constante)";
        if (name == "nu") return "ν (grados de libertad)";
        if (name.StartsWith("alpha", StringComparison.Ordinal)) return $"α (ARCH) {name["alpha".Length..]}";
        if (name.StartsWith("gamma", StringComparison.Ordinal)) return $"γ (asimetría) {name["gamma".Length..]}";
        if (name.StartsWith("beta", StringComparison.Ordinal)) return $"β (GARCH) {name["beta".Length..]}";
        return name;
    }

    private static string VariantTitle(string variant) => variant switch
    {
        "garch" => "GARCH(p, q)",
        "gjr" => "GJR-GARCH",
        "egarch" => "EGARCH",
        "tarch" => "TARCH / Zakoian",
        "aparch" => "APARCH",
        "arch" => "ARCH(q)",
        _ => variant
    };

    private static string DistributionName(Distribution distribution) =>
        distribution == Distribution.Normal ? "normal" : "t de student";

    private static double Quantile(ModelSpec spec, double[] parameters, double level)
    {
        if (spec.Distribution == Distribution.Normal) return SpecialFunctions.NormalInv(level);
        var nu = parameters[spec.DistributionOffset];
        return InferenceDistributions.TQuantile(level, nu) / Math.Sqrt(nu / (nu - 2));
    }

    private static double[] ForecastVariance(
        ModelSpec spec,
        double[] residuals,
        double[] variance,
        double[] parameters,
        int horizon)
    {
        var output = new double[horizon];
        var location = spec.VolatilityOffset;
        var omega = parameters[location++];
        var alphas = new double[spec.P];
        for (var index = 0; index < spec.P; index++) alphas[index] = parameters[location++];
        var gammas = new double[spec.O];
        for (var index = 0; index < spec.O; index++) gammas[index] = parameters[location++];
        var betas = new double[spec.Q];
        for (var index = 0; index < spec.Q; index++) betas[index] = parameters[location++];

        var oneStep = omega;
        for (var index = 0; index < spec.P; index++)
        {
            var position = residuals.Length - 1 - index;
            oneStep += alphas[index] * (position >= 0 ? Math.Pow(Math.Abs(residuals[position]), spec.Power) : 0);
        }
        for (var index = 0; index < spec.O; index++)
        {
            var position = residuals.Length - 1 - index;
            oneStep += gammas[index]
                       * (position >= 0 && residuals[position] < 0
                           ? Math.Pow(Math.Abs(residuals[position]), spec.Power)
                           : 0);
        }
        for (var index = 0; index < spec.Q; index++)
        {
            var position = variance.Length - 1 - index;
            var lagged = position >= 0 ? Math.Pow(variance[position], spec.Power / 2.0) : 0;
            oneStep += betas[index] * lagged;
        }

        var varianceExponent = 2.0 / spec.Power;
        if (spec.Family == Family.Arch)
        {
            for (var index = 0; index < horizon; index++) output[index] = Math.Pow(oneStep, varianceExponent);
            return output;
        }

        var persistence = VolatilityPersistence(spec, parameters[spec.VolatilityOffset..(spec.VolatilityOffset + spec.VolatilityCount)]);
        output[0] = Math.Pow(oneStep, varianceExponent);
        for (var index = 1; index < horizon; index++)
        {
            var transformed = omega + persistence * Math.Pow(output[index - 1], spec.Power / 2.0);
            output[index] = Math.Pow(transformed, varianceExponent);
        }
        return output;
    }

    private static List<double[]> ConfidenceIntervals(double[] parameters, double[][] covariance, double alpha)
    {
        var z = SpecialFunctions.NormalInv(1 - alpha / 2);
        var output = new List<double[]>(parameters.Length);
        for (var index = 0; index < parameters.Length; index++)
        {
            var error = Math.Sqrt(covariance[index][index]);
            output.Add([parameters[index] - z * error, parameters[index] + z * error]);
        }
        return output;
    }

    private static double[][]? CovarianceMatrix(double[] y, ModelSpec spec, double[] parameters, FilterContext context)
    {
        var count = parameters.Length;
        var steps = new double[count];
        for (var index = 0; index < count; index++)
        {
            // Mismos pasos que statsmodels.tools.numdiff.approx_hess3 (EPS ** (1/4) * max(|x|, 0.1)).
            steps[index] = 1.220703125e-4 * Math.Max(Math.Abs(parameters[index]), 0.1);
        }

        var hessian = new double[count][];
        for (var row = 0; row < count; row++) hessian[row] = new double[count];
        for (var row = 0; row < count; row++)
        {
            for (var column = row; column < count; column++)
            {
                var pp = (double[])parameters.Clone();
                var pm = (double[])parameters.Clone();
                var mp = (double[])parameters.Clone();
                var mm = (double[])parameters.Clone();
                pp[row] += steps[row]; pp[column] += steps[column];
                pm[row] += steps[row]; pm[column] -= steps[column];
                mp[row] -= steps[row]; mp[column] += steps[column];
                mm[row] -= steps[row]; mm[column] -= steps[column];
                var value = (NegativeLogLikelihood(y, spec, pp, context) - NegativeLogLikelihood(y, spec, pm, context)
                             - NegativeLogLikelihood(y, spec, mp, context) + NegativeLogLikelihood(y, spec, mm, context))
                            / (4 * steps[row] * steps[column]);
                hessian[row][column] = value;
                hessian[column][row] = value;
            }
        }

        var inverse = LinearAlgebra.Inverse(hessian);
        if (inverse.IsError) return null;
        var covariance = inverse.Value;

        var scores = new double[y.Length][];
        for (var observation = 0; observation < y.Length; observation++) scores[observation] = new double[count];
        for (var parameter = 0; parameter < count; parameter++)
        {
            var step = 1e-5 * Math.Max(1, Math.Abs(parameters[parameter]));
            var plus = (double[])parameters.Clone();
            var minus = (double[])parameters.Clone();
            plus[parameter] += step;
            minus[parameter] -= step;
            var plusValues = IndividualNegativeLogLikelihood(y, spec, plus, context);
            var minusValues = IndividualNegativeLogLikelihood(y, spec, minus, context);
            for (var observation = 0; observation < y.Length; observation++)
            {
                scores[observation][parameter] = (plusValues[observation] - minusValues[observation]) / (2 * step);
            }
        }

        var means = new double[count];
        for (var parameter = 0; parameter < count; parameter++)
        {
            var total = 0.0;
            for (var observation = 0; observation < y.Length; observation++) total += scores[observation][parameter];
            means[parameter] = total / y.Length;
        }

        var scoreCovariance = new double[count][];
        for (var row = 0; row < count; row++) scoreCovariance[row] = new double[count];
        for (var row = 0; row < count; row++)
        {
            for (var column = row; column < count; column++)
            {
                var total = 0.0;
                for (var observation = 0; observation < y.Length; observation++)
                {
                    total += (scores[observation][row] - means[row]) * (scores[observation][column] - means[column]);
                }
                var value = total / (y.Length - 1);
                scoreCovariance[row][column] = value;
                scoreCovariance[column][row] = value;
            }
        }

        var robust = LinearAlgebra.Multiply(LinearAlgebra.Multiply(covariance, scoreCovariance), covariance);
        for (var row = 0; row < count; row++)
        {
            for (var column = 0; column < count; column++) robust[row][column] *= y.Length;
        }
        return robust;
    }

    private static double[]? Minimize(Func<double[], double> objective, double[] start, double[] scale)
    {
        var count = start.Length;
        var x = new double[count];
        for (var index = 0; index < count; index++) x[index] = start[index] / scale[index];

        double Evaluate(double[] theta)
        {
            var parameters = new double[count];
            for (var index = 0; index < count; index++) parameters[index] = theta[index] * scale[index];
            return objective(parameters);
        }

        var value = Evaluate(x);
        var gradient = Gradient(Evaluate, x);
        var hessian = LinearAlgebra.Identity(count);

        for (var iteration = 0; iteration < 300; iteration++)
        {
            if (MaxAbs(gradient) < 1e-7) break;
            var direction = Negate(LinearAlgebra.MatVec(hessian, gradient));
            var directional = Dot(gradient, direction);
            if (!(directional < 0))
            {
                hessian = LinearAlgebra.Identity(count);
                direction = Negate(gradient);
                directional = Dot(gradient, direction);
            }

            var norm = Math.Sqrt(Dot(direction, direction));
            if (norm > 20)
            {
                for (var index = 0; index < count; index++) direction[index] *= 20 / norm;
                directional = Dot(gradient, direction);
            }

            var step = 1.0;
            double[]? next = null;
            var nextValue = value;
            for (var search = 0; search < 60; search++)
            {
                var candidate = Add(x, Scale(direction, step));
                var candidateValue = Evaluate(candidate);
                if (double.IsFinite(candidateValue) && candidateValue <= value + 1e-4 * step * directional)
                {
                    next = candidate;
                    nextValue = candidateValue;
                    break;
                }
                step *= 0.5;
            }

            if (next is null) break;
            var nextGradient = Gradient(Evaluate, next);
            var s = Subtract(next, x);
            var yVector = Subtract(nextGradient, gradient);
            var sy = Dot(s, yVector);
            if (sy > 1e-14)
            {
                var rho = 1 / sy;
                var left = LinearAlgebra.Identity(count);
                var right = LinearAlgebra.Identity(count);
                for (var row = 0; row < count; row++)
                {
                    for (var column = 0; column < count; column++)
                    {
                        left[row][column] -= rho * s[row] * yVector[column];
                        right[row][column] -= rho * yVector[row] * s[column];
                    }
                }
                var updated = LinearAlgebra.Multiply(LinearAlgebra.Multiply(left, hessian), right);
                for (var row = 0; row < count; row++)
                {
                    for (var column = 0; column < count; column++)
                    {
                        updated[row][column] += rho * s[row] * s[column];
                    }
                }
                hessian = updated;
            }

            x = next;
            value = nextValue;
            gradient = nextGradient;
        }

        // Refinamiento de Newton sobre el mismo espacio escalado.
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var polished = Gradient(Evaluate, x, 1e-6);
            if (MaxAbs(polished) < 1e-9) break;
            var curvature = Hessian(Evaluate, x);
            var solved = LinearAlgebra.SolveLinear(curvature, polished);
            if (solved.IsError) break;
            var direction = Negate(solved.Value);
            if (Dot(polished, direction) >= 0) direction = Negate(polished);
            var norm = Math.Sqrt(Dot(direction, direction));
            if (norm > 5)
            {
                for (var index = 0; index < count; index++) direction[index] *= 5 / norm;
            }
            var step = 1.0;
            var accepted = false;
            for (var search = 0; search < 40; search++)
            {
                var candidate = Add(x, Scale(direction, step));
                var candidateValue = Evaluate(candidate);
                if (double.IsFinite(candidateValue) && candidateValue < value)
                {
                    x = candidate;
                    value = candidateValue;
                    accepted = true;
                    break;
                }
                step *= 0.5;
            }
            if (!accepted) break;
        }

        var parameters = new double[count];
        for (var index = 0; index < count; index++) parameters[index] = x[index] * scale[index];
        return double.IsFinite(value) ? parameters : null;
    }

    private static double[] Gradient(Func<double[], double> function, double[] x, double relativeStep = 1e-5)
    {
        var gradient = new double[x.Length];
        for (var index = 0; index < x.Length; index++)
        {
            var step = relativeStep * Math.Max(1, Math.Abs(x[index]));
            var plus = (double[])x.Clone();
            var minus = (double[])x.Clone();
            plus[index] += step;
            minus[index] -= step;
            gradient[index] = (function(plus) - function(minus)) / (2 * step);
        }
        return gradient;
    }

    private static double[][] Hessian(Func<double[], double> function, double[] x)
    {
        var count = x.Length;
        var output = new double[count][];
        for (var row = 0; row < count; row++) output[row] = new double[count];
        var steps = new double[count];
        for (var index = 0; index < count; index++) steps[index] = 1e-4 * Math.Max(1, Math.Abs(x[index]));

        for (var row = 0; row < count; row++)
        {
            for (var column = row; column < count; column++)
            {
                var pp = (double[])x.Clone();
                var pm = (double[])x.Clone();
                var mp = (double[])x.Clone();
                var mm = (double[])x.Clone();
                pp[row] += steps[row]; pp[column] += steps[column];
                pm[row] += steps[row]; pm[column] -= steps[column];
                mp[row] -= steps[row]; mp[column] += steps[column];
                mm[row] -= steps[row]; mm[column] -= steps[column];
                var value = (function(pp) - function(pm) - function(mp) + function(mm))
                            / (4 * steps[row] * steps[column]);
                output[row][column] = value;
                output[column][row] = value;
            }
        }
        return output;
    }

    private static (double Statistic, double PValue) ArchLagrangeMultiplier(double[] values, int lags)
    {
        var n = values.Length;
        var maxLag = Math.Min(lags, n - 1);
        var rows = n - maxLag;
        if (rows <= maxLag + 1) return (double.NaN, double.NaN);

        var x = new double[rows][];
        var y = new double[rows];
        for (var row = 0; row < rows; row++)
        {
            var position = row + maxLag;
            x[row] = new double[maxLag];
            for (var lag = 1; lag <= maxLag; lag++) x[row][lag - 1] = values[position - lag] * values[position - lag];
            y[row] = values[position] * values[position];
        }

        var regression = LinearAlgebra.LeastSquares(x, y);
        if (regression.IsError) return (double.NaN, double.NaN);
        var statistic = rows * regression.Value.RSquared;
        return (statistic, ExtendedStatistics.ChiSquareSf(statistic, maxLag));
    }

    private static (double Statistic, double PValue) JarqueBera(double[] values)
    {
        var n = values.Length;
        var mean = Mean(values);
        var second = 0.0;
        var third = 0.0;
        var fourth = 0.0;
        for (var index = 0; index < n; index++)
        {
            var delta = values[index] - mean;
            var square = delta * delta;
            second += square;
            third += square * delta;
            fourth += square * square;
        }
        second /= n;
        third /= n;
        fourth /= n;
        var skewness = third / Math.Pow(second, 1.5);
        var kurtosis = fourth / (second * second);
        var statistic = n / 6.0 * (skewness * skewness + (kurtosis - 3) * (kurtosis - 3) / 4.0);
        return (statistic, ExtendedStatistics.ChiSquareSf(statistic, 2));
    }

    private static (double Statistic, double PValue) Kupiec(int hits, int n, double level)
    {
        double LogLikelihood(double probability)
        {
            var misses = n - hits > 0 ? (n - hits) * Math.Log(1 - probability) : 0.0;
            return misses + (hits > 0 ? hits * Math.Log(probability) : 0.0);
        }

        var statistic = Math.Max(-2 * (LogLikelihood(level) - LogLikelihood(hits / (double)n)), 0.0);
        return (statistic, ExtendedStatistics.ChiSquareSf(statistic, 1));
    }

    private static string[] FutureLabels(LoadedSeries series, string[] labels, int horizon)
    {
        if (series.LastDate is not { } last) return Enumerable.Range(1, horizon).Select(index => (series.Values.Length + index).ToString(CultureInfo.InvariantCulture)).ToArray();
        var output = new string[horizon];
        var date = last;
        for (var index = 0; index < horizon; index++)
        {
            date = series.Frequency switch
            {
                "yearly" => date.AddYears(1),
                "quarterly" => date.AddMonths(3),
                "monthly" => date.AddMonths(1),
                "weekly" => date.AddDays(7),
                "daily" => date.AddDays(1),
                "hourly" => date.AddHours(1),
                _ => date.AddMonths(1)
            };
            output[index] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        return output;
    }

    private static double Mean(IReadOnlyList<double> values)
    {
        var total = 0.0;
        for (var index = 0; index < values.Count; index++) total += values[index];
        return total / values.Count;
    }

    private static double Variance(IReadOnlyList<double> values, int ddof)
    {
        var mean = Mean(values);
        var total = 0.0;
        for (var index = 0; index < values.Count; index++)
        {
            var delta = values[index] - mean;
            total += delta * delta;
        }
        return total / (values.Count - ddof);
    }

    private static double StandardDeviation(IReadOnlyList<double> values, int ddof) => Math.Sqrt(Variance(values, ddof));

    private static double MeanAbsolutePower(IReadOnlyList<double> values, double power)
    {
        var total = 0.0;
        for (var index = 0; index < values.Count; index++) total += Math.Pow(Math.Abs(values[index]), power);
        return total / values.Count;
    }

    private static double KurtosisBiased(IReadOnlyList<double> values)
    {
        var mean = Mean(values);
        var second = 0.0;
        var fourth = 0.0;
        for (var index = 0; index < values.Count; index++)
        {
            var delta = values[index] - mean;
            var square = delta * delta;
            second += square;
            fourth += square * square;
        }
        second /= values.Count;
        fourth /= values.Count;
        return fourth / (second * second);
    }

    private static double[] Subtract(double[] left, double[] right)
    {
        var output = new double[left.Length];
        for (var index = 0; index < left.Length; index++) output[index] = left[index] - right[index];
        return output;
    }

    private static double[] Subtract(double[] values, double scalar)
    {
        var output = new double[values.Length];
        for (var index = 0; index < values.Length; index++) output[index] = values[index] - scalar;
        return output;
    }

    private static double Dot(double[] left, double[] right)
    {
        var total = 0.0;
        for (var index = 0; index < left.Length; index++) total += left[index] * right[index];
        return total;
    }

    private static double[] Add(double[] left, double[] right)
    {
        var output = new double[left.Length];
        for (var index = 0; index < left.Length; index++) output[index] = left[index] + right[index];
        return output;
    }

    private static double[] Scale(double[] values, double factor)
    {
        var output = new double[values.Length];
        for (var index = 0; index < values.Length; index++) output[index] = values[index] * factor;
        return output;
    }

    private static double[] Negate(double[] values)
    {
        var output = new double[values.Length];
        for (var index = 0; index < values.Length; index++) output[index] = -values[index];
        return output;
    }

    private static double MaxAbs(double[] values)
    {
        var maximum = 0.0;
        for (var index = 0; index < values.Length; index++) maximum = Math.Max(maximum, Math.Abs(values[index]));
        return maximum;
    }
}
