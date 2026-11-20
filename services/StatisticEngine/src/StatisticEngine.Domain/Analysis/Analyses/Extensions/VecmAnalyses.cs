using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>
/// Modelo de corrección de errores (VECM) de Johansen. Port de <c>econ_multi._vecm</c> con la
/// estimación de <c>statsmodels.tsa.vector_ar.vecm.VECM(...).fit()</c>: rango restringido, las siete
/// variantes deterministas (<c>co</c>, <c>ci</c>, <c>n</c>, <c>lo</c>, <c>li</c>, <c>cili</c>,
/// <c>colo</c>), α y β por máxima verosimilitud, errores estándar de α y Γ, causalidad de Granger de
/// corto plazo (<c>test_granger_causality</c>), Portmanteau (<c>test_whiteness</c>) y
/// <c>predict</c> sobre la representación VAR del VECM.
/// </summary>
internal static class VecmAnalyses
{
    private static readonly (string Value, string Label)[] DeterministicTerms =
    [
        ("co", "Constante fuera de la relación (en las diferencias)"),
        ("ci", "Constante dentro de la relación de largo plazo"),
        ("n", "Ninguno"),
        ("lo", "Tendencia fuera"),
        ("li", "Tendencia dentro"),
        ("cili", "Constante y tendencia dentro"),
        ("colo", "Constante y tendencia fuera")
    ];

    private static readonly (double Level, string Label)[] Levels =
    [
        (0.10, "10 %"),
        (0.05, "5 %"),
        (0.01, "1 %")
    ];

    public static readonly AnalysisDefinition Vecm = new(
        "vecm",
        "VECM: modelo de corrección de errores",
        "Econometría",
        "Relación de largo plazo (β), velocidad de ajuste (α), dinámica de corto plazo y pronósticos para series cointegradas.",
        [],
        [],
        [
            new(
                "rank",
                "Rango de cointegración (vacío = según Johansen)",
                AnalysisParamKinds.Integer,
                null,
                [],
                true,
                Minimum: 1,
                Maximum: 5),
            new(
                "lags",
                "Rezagos en diferencias",
                AnalysisParamKinds.Integer,
                2.0,
                [],
                false,
                Minimum: 0,
                Maximum: 12),
            new(
                "deterministic",
                "Términos deterministas",
                AnalysisParamKinds.Select,
                "ci",
                [.. DeterministicTerms.Select(item => new AnalysisParamOption(item.Value, item.Label))],
                false),
            new(
                "forecast_steps",
                "Periodos a pronosticar (0 = no)",
                AnalysisParamKinds.Integer,
                8.0,
                [],
                false,
                Minimum: 0,
                Maximum: 100),
            ExtendedHelpers.Alpha("Nivel de significancia")
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Cointegración",
        Roles:
        [
            new ColumnRole("vars", "Series (2 a 6, en niveles)", ColumnKinds.Numeric, Multiple: true, MinCount: 2, MaxCount: 6),
            new ColumnRole(
                "time",
                "Columna de tiempo (opcional)",
                ColumnKinds.DateTime,
                Optional: true,
                Help: "Sin ella se usa el orden de las filas.")
        ]);

    // ------------------------------------------------------------------ estimación

    /// <summary>
    /// Resultado de la estimación ML. Las matrices siguen la orientación de statsmodels: las series
    /// son filas y el tiempo columnas. <see cref="DetInside"/> son las filas deterministas de la
    /// relación de largo plazo y <see cref="DetOutside"/> las de la dinámica de corto plazo.
    /// </summary>
    private sealed record VecmEstimate(
        int K,
        int Lags,
        int KAr,
        int Rank,
        string Deterministic,
        int DetInside,
        int DetOutside,
        int Nobs,
        double[][] Alpha,
        double[][] Beta,
        double[][] BetaAugmented,
        double[][] DetCoefCoint,
        double[][] DetCoef,
        double[][] Gamma,
        double[][] DeltaX,
        double[][] YLag1,
        double[][] Resid,
        double[][] SigmaU,
        double[] Eigenvalues,
        double Llf)
    {
        public int ShortRunColumns => DeltaX.Length;

        public bool HasCointegratedConstant => Deterministic.Contains("ci", StringComparison.Ordinal);

        public bool HasCointegratedTrend => Deterministic.Contains("li", StringComparison.Ordinal);

        public bool HasConstant => Deterministic.Contains("co", StringComparison.Ordinal);

        public bool HasTrend => Deterministic.Contains("lo", StringComparison.Ordinal);
    }

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var names = input.RoleNames("vars", true).ToArray();
        var timeColumn = ExtendedHelpers.FirstRole(input, "time", "Columna de tiempo");
        var lags = Math.Max(0, input.Pint("lags", 2));
        var deterministic = input.Pstr("deterministic", "ci");
        var steps = Math.Clamp(input.Pint("forecast_steps", 8), 0, 100);
        var alpha = input.Pnum("alpha", 0.05);
        if (names.Length > 6) return ExtendedHelpers.Invalid("El modelo VECM admite hasta 6 series.");

        var minimum = Math.Max(30, (lags + 3) * names.Length + 10);
        var loaded = MultiSeriesLoader.Load(frame, names, timeColumn, minimum);
        if (loaded.IsError) return Result.Err<AnalysisResult>(loaded.Error.Code, loaded.Error.Message);
        var mts = loaded.Value;
        var k = mts.K;
        var n = mts.N;
        var data = MatrixBuilder.Rows(mts);

        // Rango sugerido por la prueba de la traza de Johansen, con el mismo orden determinista que
        // usa el VECM (igual que `select_coint_rank(endog, det_order, k_ar_diff)`).
        var level = Levels.MinBy(item => Math.Abs(item.Level - alpha)).Level;
        var levelIndex = Array.FindIndex(Levels, item => item.Level == level);
        var johansen = CointegrationAnalyses.Johansen(data, DeterministicOrder(deterministic), lags);
        if (johansen is null)
        {
            return ExtendedHelpers.Invalid("La prueba de Johansen no pudo estimarse con estos datos.");
        }
        var traceCritical = Enumerable.Range(0, k).Select(index => johansen.TraceCritical[index][levelIndex]).ToArray();
        var suggested = 0;
        while (suggested < k && johansen.Trace[suggested] > traceCritical[suggested]) suggested++;
        var maximumRank = Math.Max(1, k - 1);

        var rankProvided = input.Params.TryGetValue("rank", out var rawRank)
                           && rawRank is not null
                           && !(rawRank is string text && text.Length == 0);
        int rank;
        if (rankProvided)
        {
            rank = input.Pint("rank", 1);
            if (rank < 1 || rank > maximumRank)
            {
                return ExtendedHelpers.Invalid($"El rango de cointegración debe estar entre 1 y {maximumRank}.");
            }
        }
        else
        {
            if (suggested < 1)
            {
                return ExtendedHelpers.Invalid(
                    "La prueba de Johansen sugiere rango 0: no hay relación de cointegración; modele las diferencias con un VAR.");
            }
            rank = Math.Min(suggested, maximumRank);
        }

        var estimated = Estimate(mts.Values, lags, deterministic, rank);
        if (estimated.IsError) return Result.Err<AnalysisResult>(estimated.Error.Code, estimated.Error.Message);
        var estimate = estimated.Value;

        var standardErrors = StandardErrors(estimate);
        var portmanteauLags = Math.Min(10, Math.Max(lags + 3, 4));
        var (portmanteau, _, portmanteauP) = Whiteness(estimate, portmanteauLags);

        var sections = new List<AnalysisSection>
        {
            new TilesSection(
                "Resumen",
                [
                    new TileItem("Rango de cointegración", JsNumber.ToString(rank)),
                    new TileItem("Observaciones", JsNumber.ToString(n)),
                    new TileItem("Log-verosimilitud", ExtendedHelpers.N(estimate.Llf, 6)),
                    new TileItem("Rezagos en diferencias", JsNumber.ToString(lags))
                ]),
            new TableSection(
                "Elección del rango (Johansen, traza)",
                ["Rango r", "Estadístico", "Valor crítico", "¿Se rechaza r?"],
                Enumerable.Range(0, k).Select(r => (IReadOnlyList<string>)new[]
                {
                    JsNumber.ToString(r),
                    ExtendedHelpers.N(johansen.Trace[r], 6),
                    ExtendedHelpers.N(traceCritical[r], 6),
                    johansen.Trace[r] > traceCritical[r] ? "sí" : "no"
                }).ToArray(),
                Note: rankProvided
                    ? $"Rango sugerido por la prueba: {suggested}; usted indicó {rank}."
                    : $"Rango sugerido por la prueba: {suggested}.")
        };

        sections.Add(new TableSection(
            "Relación de largo plazo (β, normalizada con la primera serie)",
            ["Serie", .. Enumerable.Range(0, rank).Select(index => $"β{index + 1}")],
            Enumerable.Range(0, k).Select(row => (IReadOnlyList<string>)[
                names[row],
                .. Enumerable.Range(0, rank).Select(relation => ExtendedHelpers.N(estimate.Beta[row][relation], 6))
            ]).ToArray(),
            Note: "La relación de equilibrio es β′y = 0 (más un término determinista si lo incluyó)."));

        sections.Add(new TableSection(
            "Velocidad de ajuste (α)",
            ["Ecuación", .. Enumerable.Range(0, rank).SelectMany(index => new[]
            {
                $"α{index + 1}",
                $"Valor p α{index + 1}"
            })],
            Enumerable.Range(0, k).Select(equation => (IReadOnlyList<string>)[
                names[equation],
                .. Enumerable.Range(0, rank).SelectMany(relation => new[]
                {
                    ExtendedHelpers.N(estimate.Alpha[equation][relation], 6),
                    SigmaText.PValue(AlphaPValue(estimate, standardErrors, equation, relation))
                })
            ]).ToArray(),
            Note: "α negativo en la ecuación de una serie: esa serie corrige las desviaciones del equilibrio (vuelve hacia él)."));

        var shortRunRows = new List<IReadOnlyList<string>>();
        for (var equation = 0; equation < k; equation++)
        {
            for (var lag = 1; lag <= lags; lag++)
            {
                for (var variable = 0; variable < k; variable++)
                {
                    var column = (lag - 1) * k + variable;
                    shortRunRows.Add(new[]
                    {
                        names[equation],
                        $"Δ{names[variable]} (rezago {lag})",
                        ExtendedHelpers.N(estimate.Gamma[equation][column], 6),
                        ExtendedHelpers.N(standardErrors.Gamma[equation][column], 6),
                        SigmaText.PValue(GammaPValue(estimate, standardErrors, equation, column))
                    });
                }
            }
            for (var index = 0; index < estimate.DetOutside; index++)
            {
                var column = lags * k + index;
                shortRunRows.Add(new[]
                {
                    names[equation],
                    index == 0 && estimate.HasConstant ? "Constante" : "Tendencia",
                    ExtendedHelpers.N(estimate.Gamma[equation][column], 6),
                    ExtendedHelpers.N(standardErrors.Gamma[equation][column], 6),
                    SigmaText.PValue(GammaPValue(estimate, standardErrors, equation, column))
                });
            }
        }
        sections.Add(new TableSection(
            "Dinámica de corto plazo (Γ)",
            ["Ecuación (Δ de)", "Regresor", "Coeficiente", "Error est.", "Valor p"],
            shortRunRows));

        var grangerRows = new List<IReadOnlyList<string>>();
        for (var caused = 0; caused < k; caused++)
        {
            var row = new List<string> { names[caused] };
            for (var causing = 0; causing < k; causing++)
            {
                if (caused == causing)
                {
                    row.Add("*");
                    continue;
                }
                row.Add(SigmaText.PValue(GrangerPValue(mts.Values, lags, deterministic, caused, causing)));
            }
            grangerRows.Add(row);
        }
        sections.Add(new TableSection(
            "Causalidad de Granger de corto plazo (valor p)",
            ["Variable que se predice", .. names],
            grangerRows));

        sections.Add(new TableSection(
            "Residuos",
            ["Prueba", "Estadístico", "Valor p"],
            [
                new[]
                {
                    "Portmanteau",
                    ExtendedHelpers.N(portmanteau, 6),
                    SigmaText.PValue(portmanteauP)
                }
            ]));

        var charts = new List<ChartSpec>();
        if (steps > 0)
        {
            var forecast = Forecast(estimate, mts.Values, steps);
            var future = mts.FutureLabels(steps);
            sections.Add(new TableSection(
                "Pronósticos",
                ["Periodo", .. names],
                Enumerable.Range(0, steps).Select(step => (IReadOnlyList<string>)[
                    future[step],
                    .. Enumerable.Range(0, k).Select(variable => ExtendedHelpers.N(forecast[step][variable], 6))
                ]).ToArray()));

            var panels = new List<XYSeries>();
            var keep = Math.Min(n, 4 * steps + 12);
            for (var variable = 0; variable < k; variable++)
            {
                var historyX = new double[keep];
                var history = new double[keep];
                for (var index = 0; index < keep; index++)
                {
                    historyX[index] = index;
                    history[index] = mts.Values[variable][n - keep + index];
                }
                panels.Add(new XYSeries($"Datos: {names[variable]}", historyX, history));

                var forecastX = new double[steps];
                var forecastY = new double[steps];
                for (var step = 0; step < steps; step++)
                {
                    forecastX[step] = keep + step;
                    forecastY[step] = forecast[step][variable];
                }
                panels.Add(new XYSeries($"Pronóstico: {names[variable]}", forecastX, forecastY, "dash"));
            }
            charts.Add(new SeriesChart("Pronósticos del VECM", panels, "Observación", "Valor"));
        }

        return Result.Ok(new AnalysisResult(
            "vecm",
            "Modelo de corrección de errores (VECM)",
            sections,
            Subtitle: $"{k} series · rango {rank} · {lags} rezago(s) en diferencias · n = {n}",
            Charts: charts,
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["rank"] = rank,
                ["llf"] = estimate.Llf,
                ["alpha"] = estimate.Alpha,
                ["beta"] = estimate.Beta
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    /// <summary>Orden de tendencia que usa <c>coint_johansen</c> para cada variante del VECM.</summary>
    private static int DeterministicOrder(string deterministic) => deterministic switch
    {
        "n" => -1,
        "ci" or "co" => 0,
        _ => 1
    };

    private static Result<VecmEstimate> Estimate(double[][] y, int lags, string deterministic, int rank)
    {
        var k = y.Length;
        var n = y[0].Length;
        var kAr = lags + 1;
        var observations = n - kAr;
        if (observations <= 0)
        {
            return Result.Err<VecmEstimate>(
                DomainErrorCode.InsufficientData,
                "El modelo necesita más observaciones que rezagos.");
        }

        var detInside = (deterministic.Contains("ci", StringComparison.Ordinal) ? 1 : 0)
                        + (deterministic.Contains("li", StringComparison.Ordinal) ? 1 : 0);
        var detOutside = (deterministic.Contains("co", StringComparison.Ordinal) ? 1 : 0)
                         + (deterministic.Contains("lo", StringComparison.Ordinal) ? 1 : 0);

        var deltaY = new double[k][];
        for (var variable = 0; variable < k; variable++)
        {
            deltaY[variable] = new double[observations];
            for (var row = 0; row < observations; row++)
            {
                deltaY[variable][row] = y[variable][kAr + row] - y[variable][kAr + row - 1];
            }
        }

        var yLag1 = new double[k + detInside][];
        for (var variable = 0; variable < k; variable++)
        {
            yLag1[variable] = new double[observations];
            for (var row = 0; row < observations; row++) yLag1[variable][row] = y[variable][kAr - 1 + row];
        }
        var detRow = k;
        if (deterministic.Contains("ci", StringComparison.Ordinal))
        {
            yLag1[detRow] = Ones(observations);
            detRow++;
        }
        if (deterministic.Contains("li", StringComparison.Ordinal))
        {
            yLag1[detRow] = LinearTrend(observations, kAr, coint: true);
            detRow++;
        }

        var shortRun = k * lags + detOutside;
        var deltaX = new double[shortRun][];
        for (var lag = 1; lag <= lags; lag++)
        {
            for (var variable = 0; variable < k; variable++)
            {
                var row = (lag - 1) * k + variable;
                deltaX[row] = new double[observations];
                for (var column = 0; column < observations; column++)
                {
                    var index = kAr - 1 + column - lag;
                    deltaX[row][column] = y[variable][index + 1] - y[variable][index];
                }
            }
        }
        var outsideRow = k * lags;
        if (deterministic.Contains("co", StringComparison.Ordinal))
        {
            deltaX[outsideRow] = Ones(observations);
            outsideRow++;
        }
        if (deterministic.Contains("lo", StringComparison.Ordinal))
        {
            deltaX[outsideRow] = LinearTrend(observations, kAr, coint: false);
        }

        var r0 = Residualize(deltaY, deltaX);
        var r1 = Residualize(yLag1, deltaX);

        var s00 = Scale(Multiply(r0, Transpose(r0)), 1.0 / observations);
        var s01 = Scale(Multiply(r0, Transpose(r1)), 1.0 / observations);
        var s11 = Scale(Multiply(r1, Transpose(r1)), 1.0 / observations);

        var inverseS00 = LinearAlgebra.Inverse(s00);
        if (inverseS00.IsError) return Result.Err<VecmEstimate>(inverseS00.Error.Code, inverseS00.Error.Message);
        var inverseSqrt = InverseSqrtSymmetric(s11);

        var factor = Multiply(s01, inverseSqrt);
        var middle = Multiply(Multiply(Transpose(factor), inverseS00.Value), factor);
        var eigen = LinearAlgebra.EigenSymmetric(MatrixBuilder.Symmetrize(middle));
        var order = Enumerable.Range(0, k + detInside)
            .OrderByDescending(index => eigen.Values[index])
            .ToArray();
        var eigenvalues = order.Select(index => eigen.Values[index]).ToArray();

        var betaTilde = new double[k + detInside][];
        for (var row = 0; row < k + detInside; row++)
        {
            betaTilde[row] = new double[rank];
            for (var relation = 0; relation < rank; relation++)
            {
                var total = 0.0;
                for (var index = 0; index < k + detInside; index++)
                {
                    total += inverseSqrt[row][index] * eigen.Vectors[index][order[relation]];
                }
                betaTilde[row][relation] = total;
            }
        }

        // Normalización: las primeras `rank` filas de β son la identidad.
        var top = new double[rank][];
        for (var row = 0; row < rank; row++)
        {
            top[row] = new double[rank];
            for (var column = 0; column < rank; column++) top[row][column] = betaTilde[row][column];
        }
        var inverseTop = LinearAlgebra.Inverse(top);
        if (inverseTop.IsError)
        {
            return Result.Err<VecmEstimate>(
                DomainErrorCode.SingularMatrix,
                "No se pudo normalizar el vector de cointegración (la primera serie no identifica la relación).");
        }
        var normalised = Multiply(betaTilde, inverseTop.Value);

        var longRun = new double[k][];
        for (var row = 0; row < k; row++) longRun[row] = normalised[row];
        var detCoefCoint = new double[detInside][];
        for (var row = 0; row < detInside; row++) detCoefCoint[row] = normalised[k + row];

        var betaProduct = Multiply(Multiply(Transpose(normalised), s11), normalised);
        var inverseBetaProduct = LinearAlgebra.Inverse(betaProduct);
        if (inverseBetaProduct.IsError)
        {
            return Result.Err<VecmEstimate>(inverseBetaProduct.Error.Code, inverseBetaProduct.Error.Message);
        }
        var loadings = Multiply(Multiply(s01, normalised), inverseBetaProduct.Value);

        var longRunPart = Multiply(loadings, Multiply(Transpose(normalised), yLag1));
        var shortRunResidual = new double[k][];
        for (var row = 0; row < k; row++)
        {
            shortRunResidual[row] = new double[observations];
            for (var column = 0; column < observations; column++)
            {
                shortRunResidual[row][column] = deltaY[row][column] - longRunPart[row][column];
            }
        }

        double[][] gamma;
        if (shortRun > 0)
        {
            var xx = Multiply(deltaX, Transpose(deltaX));
            var inverseXx = LinearAlgebra.Inverse(xx);
            if (inverseXx.IsError) return Result.Err<VecmEstimate>(inverseXx.Error.Code, inverseXx.Error.Message);
            gamma = Multiply(Multiply(shortRunResidual, Transpose(deltaX)), inverseXx.Value);
        }
        else
        {
            gamma = Enumerable.Range(0, k).Select(_ => Array.Empty<double>()).ToArray();
        }

        var residual = new double[k][];
        for (var row = 0; row < k; row++)
        {
            residual[row] = new double[observations];
            for (var column = 0; column < observations; column++)
            {
                var fitted = 0.0;
                for (var term = 0; term < shortRun; term++) fitted += gamma[row][term] * deltaX[term][column];
                residual[row][column] = shortRunResidual[row][column] - fitted;
            }
        }

        var sigmaU = Scale(Multiply(residual, Transpose(residual)), 1.0 / observations);
        var logDeterminant = CholeskyDecomposition.LogDeterminant(s00);
        if (!double.IsFinite(logDeterminant))
        {
            return Result.Err<VecmEstimate>(
                DomainErrorCode.SingularMatrix,
                "La matriz de covarianzas de los residuos de largo plazo no es definida positiva.");
        }
        var sum = 0.0;
        for (var index = 0; index < rank; index++) sum += Math.Log(1 - eigenvalues[index]);
        var logLikelihood = -k * observations * Math.Log(2 * Math.PI) / 2
                            - observations * (logDeterminant + sum) / 2
                            - k * observations / 2.0;

        var detCoef = new double[k][];
        for (var row = 0; row < k; row++)
        {
            detCoef[row] = new double[detOutside];
            for (var column = 0; column < detOutside; column++) detCoef[row][column] = gamma[row][k * lags + column];
        }

        return Result.Ok(new VecmEstimate(
            k,
            lags,
            kAr,
            rank,
            deterministic,
            detInside,
            detOutside,
            observations,
            loadings,
            longRun,
            normalised,
            detCoefCoint,
            detCoef,
            gamma,
            deltaX,
            yLag1,
            residual,
            sigmaU,
            eigenvalues,
            logLikelihood));
    }

    // ------------------------------------------------------------------ errores estándar

    private sealed record StandardErrorResult(double[][] Alpha, double[][] Gamma);

    /// <summary>
    /// Errores estándar de α y Γ con <c>cov_params_default</c> (Lütkepohl p. 296, 7.2.21):
    /// <c>kron(b_id·Ω⁻¹·b_idᵀ, Σ_u)</c>; sólo se publican las entradas diagonales que usan las tablas.
    /// </summary>
    private static StandardErrorResult StandardErrors(VecmEstimate estimate)
    {
        var k = estimate.K;
        var rank = estimate.Rank;
        var shortRun = estimate.ShortRunColumns;
        var size = rank + shortRun;

        var betaY = Multiply(Transpose(estimate.BetaAugmented), estimate.YLag1);
        var omega = LinearAlgebra.Zeros(size, size);
        var betaBeta = Multiply(betaY, Transpose(betaY));
        for (var row = 0; row < rank; row++)
        {
            for (var column = 0; column < rank; column++) omega[row][column] = betaBeta[row][column];
        }
        var betaX = Multiply(betaY, Transpose(estimate.DeltaX));
        for (var row = 0; row < rank; row++)
        {
            for (var column = 0; column < shortRun; column++)
            {
                omega[row][rank + column] = betaX[row][column];
                omega[rank + column][row] = betaX[row][column];
            }
        }
        var xx = Multiply(estimate.DeltaX, Transpose(estimate.DeltaX));
        for (var row = 0; row < shortRun; row++)
        {
            for (var column = 0; column < shortRun; column++) omega[rank + row][rank + column] = xx[row][column];
        }

        var alpha = new double[k][];
        var gamma = new double[k][];
        for (var variable = 0; variable < k; variable++)
        {
            alpha[variable] = new double[rank];
            gamma[variable] = new double[shortRun];
            for (var column = 0; column < rank; column++) alpha[variable][column] = double.NaN;
            for (var column = 0; column < shortRun; column++) gamma[variable][column] = double.NaN;
        }

        var inverse = LinearAlgebra.Inverse(omega);
        if (inverse.IsError) return new StandardErrorResult(alpha, gamma);

        var longRunRows = k + estimate.DetInside;
        var rows = longRunRows + shortRun;
        var identity = LinearAlgebra.Zeros(rows, size);
        for (var row = 0; row < longRunRows; row++)
        {
            for (var column = 0; column < rank; column++)
            {
                identity[row][column] = estimate.BetaAugmented[row][column];
            }
        }
        for (var row = 0; row < shortRun; row++) identity[longRunRows + row][rank + row] = 1;
        var matrix = Multiply(Multiply(identity, inverse.Value), Transpose(identity));

        for (var equation = 0; equation < k; equation++)
        {
            for (var relation = 0; relation < rank; relation++)
            {
                var variance = matrix[relation][relation] * estimate.SigmaU[equation][equation];
                alpha[equation][relation] = variance > 0 ? Math.Sqrt(variance) : double.NaN;
            }
            for (var column = 0; column < shortRun; column++)
            {
                var variance = matrix[longRunRows + column][longRunRows + column] * estimate.SigmaU[equation][equation];
                gamma[equation][column] = variance > 0 ? Math.Sqrt(variance) : double.NaN;
            }
        }

        return new StandardErrorResult(alpha, gamma);
    }

    private static double AlphaPValue(
        VecmEstimate estimate,
        StandardErrorResult errors,
        int equation,
        int relation)
    {
        var error = errors.Alpha[equation][relation];
        if (!(error > 0)) return double.NaN;
        return TwoSidedNormal(estimate.Alpha[equation][relation] / error);
    }

    private static double GammaPValue(
        VecmEstimate estimate,
        StandardErrorResult errors,
        int equation,
        int column)
    {
        var error = errors.Gamma[equation][column];
        if (!(error > 0)) return double.NaN;
        return TwoSidedNormal(estimate.Gamma[equation][column] / error);
    }

    /// <summary><c>2·(1 − Φ(|z|))</c> sin cancelación catastrófica: <c>erfc(|z|/√2)</c>.</summary>
    private static double TwoSidedNormal(double z) => SpecialFunctions.Erfc(Math.Abs(z) / Math.Sqrt(2));

    // ------------------------------------------------------------------ Portmanteau

    /// <summary>
    /// <c>test_whiteness(nlags)</c> de statsmodels: autocovarianzas de los residuos centrados sobre
    /// <c>T</c> y traza con la inversa de <c>sigma_u</c> (sin centrar), <c>gl = k²(nlags−k_ar+1) − k·r</c>.
    /// </summary>
    private static (double Statistic, int Df, double PValue) Whiteness(VecmEstimate estimate, int nlags)
    {
        var k = estimate.K;
        var observations = estimate.Nobs;
        if (nlags < estimate.KAr)
        {
            return (double.NaN, 0, double.NaN);
        }
        var means = new double[k];
        for (var variable = 0; variable < k; variable++)
        {
            for (var row = 0; row < observations; row++) means[variable] += estimate.Resid[variable][row];
            means[variable] /= observations;
        }
        var centered = new double[k][];
        for (var variable = 0; variable < k; variable++)
        {
            centered[variable] = new double[observations];
            for (var row = 0; row < observations; row++)
            {
                centered[variable][row] = estimate.Resid[variable][row] - means[variable];
            }
        }

        var inverseSigma = LinearAlgebra.Inverse(estimate.SigmaU);
        if (inverseSigma.IsError) return (double.NaN, 0, double.NaN);

        var statistic = 0.0;
        for (var lag = 1; lag <= nlags; lag++)
        {
            var acov = new double[k][];
            for (var row = 0; row < k; row++)
            {
                acov[row] = new double[k];
                for (var column = 0; column < k; column++)
                {
                    var total = 0.0;
                    for (var time = lag; time < observations; time++)
                    {
                        total += centered[row][time] * centered[column][time - lag];
                    }
                    acov[row][column] = total / observations;
                }
            }

            var middle = Multiply(Multiply(Transpose(acov), inverseSigma.Value), acov);
            var product = Multiply(middle, inverseSigma.Value);
            for (var index = 0; index < k; index++) statistic += product[index][index];
        }
        statistic *= observations;

        var df = k * k * (nlags - estimate.KAr + 1) - k * estimate.Rank;
        return (statistic, df, ExtendedStatistics.ChiSquareSf(statistic, df));
    }

    // ------------------------------------------------------------------ causalidad de Granger

    /// <summary>
    /// <c>test_granger_causality(caused, causing)</c>: VAR(k_ar+1) en niveles con los deterministas
    /// como <c>exog</c>, reparametrizado como en statsmodels (<c>x_min_p</c>) y restricción de Wald
    /// sobre los primeros <c>k_ar</c> rezagos. Se devuelve el p-valor de la F.
    /// </summary>
    private static double GrangerPValue(
        double[][] y,
        int lags,
        string deterministic,
        int caused,
        int causing)
    {
        var k = y.Length;
        var n = y[0].Length;
        var kAr = lags + 1;
        var p = kAr + 1;
        var t = n - kAr - 1;
        if (t <= k * p + 2) return double.NaN;
        var deterministicColumns = DeterministicColumns(deterministic, n);
        var numDet = deterministicColumns.Count;

        var columns = numDet + k * p;
        var design = new double[t][];
        var response = new double[t][];
        for (var row = 0; row < t; row++)
        {
            var time = p + row;
            var values = new double[columns];
            for (var det = 0; det < numDet; det++) values[det] = deterministicColumns[det][time];
            for (var lag = 1; lag <= p; lag++)
            {
                for (var variable = 0; variable < k; variable++)
                {
                    values[numDet + (lag - 1) * k + variable] = y[variable][time - lag];
                }
            }
            design[row] = values;
            var target = new double[k];
            for (var variable = 0; variable < k; variable++) target[variable] = y[variable][time];
            response[row] = target;
        }

        var xtx = new double[columns][];
        for (var row = 0; row < columns; row++)
        {
            xtx[row] = new double[columns];
            for (var column = 0; column < columns; column++)
            {
                var total = 0.0;
                for (var observation = 0; observation < t; observation++)
                {
                    total += design[observation][row] * design[observation][column];
                }
                xtx[row][column] = total;
            }
        }
        var inverse = LinearAlgebra.Inverse(xtx);
        if (inverse.IsError) return double.NaN;
        var xty = new double[columns][];
        for (var row = 0; row < columns; row++)
        {
            xty[row] = new double[k];
            for (var variable = 0; variable < k; variable++)
            {
                var total = 0.0;
                for (var observation = 0; observation < t; observation++)
                {
                    total += design[observation][row] * response[observation][variable];
                }
                xty[row][variable] = total;
            }
        }
        var parameters = LinearAlgebra.Multiply(inverse.Value, xty);

        var dfResid = t - (k * p + numDet);
        if (dfResid <= 0) return double.NaN;
        var sigma = LinearAlgebra.Zeros(k, k);
        for (var observation = 0; observation < t; observation++)
        {
            var residuals = new double[k];
            for (var variable = 0; variable < k; variable++)
            {
                var fitted = 0.0;
                for (var row = 0; row < columns; row++)
                {
                    fitted += design[observation][row] * parameters[row][variable];
                }
                residuals[variable] = response[observation][variable] - fitted;
            }
            for (var i = 0; i < k; i++)
            {
                for (var j = 0; j < k; j++) sigma[i][j] += residuals[i] * residuals[j];
            }
        }
        for (var i = 0; i < k; i++)
        {
            for (var j = 0; j < k; j++) sigma[i][j] /= dfResid;
        }

        // Restricción: coeficientes de `causing` en los rezagos 1..p−1 de la ecuación `caused`.
        var restrictions = p - 1;
        var ca = new double[restrictions];
        for (var lag = 0; lag < restrictions; lag++)
        {
            ca[lag] = parameters[numDet + lag * k + causing][caused];
        }

        var xRows = numDet + k * p;
        var xMinP = new double[xRows][];
        for (var det = 0; det < numDet; det++)
        {
            xMinP[det] = new double[t];
            for (var column = 0; column < t; column++) xMinP[det][column] = deterministicColumns[det][p + column];
        }
        for (var index = 0; index < p - 1; index++)
        {
            for (var variable = 0; variable < k; variable++)
            {
                var row = new double[t];
                for (var column = 0; column < t; column++)
                {
                    row[column] = y[variable][kAr - index + column] - y[variable][column];
                }
                xMinP[numDet + index * k + variable] = row;
            }
        }
        for (var variable = 0; variable < k; variable++)
        {
            var row = new double[t];
            for (var column = 0; column < t; column++) row[column] = y[variable][column];
            xMinP[numDet + (p - 1) * k + variable] = row;
        }

        var xx = Multiply(xMinP, Transpose(xMinP));
        var inverseXx = LinearAlgebra.Inverse(xx);
        if (inverseXx.IsError) return double.NaN;
        var mleScale = (double)dfResid / t;

        var selected = new double[restrictions][];
        for (var row = 0; row < restrictions; row++)
        {
            selected[row] = new double[restrictions];
            for (var column = 0; column < restrictions; column++)
            {
                var first = numDet + causing + k * row;
                var second = numDet + causing + k * column;
                selected[row][column] = t * inverseXx.Value[first][second] * sigma[caused][caused] * mleScale;
            }
        }

        var middle = LinearAlgebra.Inverse(selected);
        if (middle.IsError) return double.NaN;
        var wald = 0.0;
        for (var row = 0; row < restrictions; row++)
        {
            for (var column = 0; column < restrictions; column++)
            {
                wald += ca[row] * middle.Value[row][column] * ca[column];
            }
        }
        wald *= t;
        var statistic = wald / restrictions;
        return ExtendedStatistics.FSf(statistic, restrictions, (double)k * dfResid);
    }

    /// <summary>Columnas deterministas de <c>_deterministic_to_exog</c> (sin estacionalidad ni exog).</summary>
    private static List<double[]> DeterministicColumns(string deterministic, int observations)
    {
        var columns = new List<double[]>(2);
        if (deterministic.Contains("co", StringComparison.Ordinal)
            || deterministic.Contains("ci", StringComparison.Ordinal))
        {
            columns.Add(Ones(observations));
        }
        if (deterministic.Contains("lo", StringComparison.Ordinal)
            || deterministic.Contains("li", StringComparison.Ordinal))
        {
            var trend = new double[observations];
            for (var row = 0; row < observations; row++) trend[row] = row;
            columns.Add(trend);
        }
        return columns;
    }

    // ------------------------------------------------------------------ pronóstico

    /// <summary>
    /// <c>predict(steps)</c>: representación VAR del VECM (<c>A₀ = αβ′ + I + Γ₁</c>,
    /// <c>Aᵢ = Γᵢ₊₁ − Γᵢ</c>, <c>A_p = −Γ_p</c>) más el arranque con los últimos <c>k_ar</c> niveles,
    /// igual que <c>var_model.forecast</c>.
    /// </summary>
    private static double[][] Forecast(VecmEstimate estimate, double[][] y, int steps)
    {
        var k = estimate.K;
        var kAr = estimate.KAr;
        var n = y[0].Length;
        var alphaBeta = Multiply(estimate.Alpha, Transpose(estimate.Beta));

        var coefficients = new double[kAr][][];
        for (var lag = 0; lag < kAr; lag++) coefficients[lag] = LinearAlgebra.Zeros(k, k);
        for (var row = 0; row < k; row++)
        {
            for (var column = 0; column < k; column++)
            {
                coefficients[0][row][column] = alphaBeta[row][column] + (row == column ? 1 : 0);
                if (estimate.Lags > 0) coefficients[0][row][column] += GammaAt(estimate, row, column);
            }
        }
        if (kAr >= 2)
        {
            for (var lag = 1; lag <= kAr - 2; lag++)
            {
                for (var row = 0; row < k; row++)
                {
                    for (var column = 0; column < k; column++)
                    {
                        coefficients[lag][row][column] = GammaAt(estimate, row, k * lag + column)
                                                        - GammaAt(estimate, row, k * (lag - 1) + column);
                    }
                }
            }
            for (var row = 0; row < k; row++)
            {
                for (var column = 0; column < k; column++)
                {
                    coefficients[kAr - 1][row][column] = -GammaAt(estimate, row, k * (kAr - 2) + column);
                }
            }
        }

        var baseline = new double[steps][];
        for (var step = 0; step < steps; step++)
        {
            var row = new double[k];
            if (estimate.HasCointegratedConstant && estimate.DetCoefCoint.Length > 0)
            {
                for (var equation = 0; equation < k; equation++)
                {
                    var total = 0.0;
                    for (var relation = 0; relation < estimate.Rank; relation++)
                    {
                        total += estimate.Alpha[equation][relation] * estimate.DetCoefCoint[0][relation];
                    }
                    row[equation] += total;
                }
            }
            else if (estimate.HasConstant && estimate.DetCoef.Length > 0 && estimate.DetCoef[0].Length > 0)
            {
                for (var equation = 0; equation < k; equation++) row[equation] += estimate.DetCoef[equation][0];
            }
            if (estimate.HasCointegratedTrend)
            {
                var offset = estimate.HasCointegratedConstant ? 1 : 0;
                if (offset < estimate.DetCoefCoint.Length)
                {
                    var value = estimate.Nobs + kAr + step;
                    for (var equation = 0; equation < k; equation++)
                    {
                        var total = 0.0;
                        for (var relation = 0; relation < estimate.Rank; relation++)
                        {
                            total += estimate.Alpha[equation][relation] * estimate.DetCoefCoint[offset][relation];
                        }
                        row[equation] += value * total;
                    }
                }
            }
            else if (estimate.HasTrend && estimate.DetCoef.Length > 0 && estimate.DetCoef[0].Length > 0)
            {
                var value = estimate.Nobs + kAr + step + 1;
                for (var equation = 0; equation < k; equation++)
                {
                    row[equation] += value * estimate.DetCoef[equation][^1];
                }
            }
            baseline[step] = row;
        }

        var initial = new double[kAr][];
        for (var lag = 0; lag < kAr; lag++)
        {
            initial[lag] = new double[k];
            for (var variable = 0; variable < k; variable++) initial[lag][variable] = y[variable][n - kAr + lag];
        }

        var forecast = new double[steps][];
        for (var step = 1; step <= steps; step++)
        {
            var value = (double[])baseline[step - 1].Clone();
            for (var lag = 1; lag <= kAr; lag++)
            {
                var previous = step > lag ? forecast[step - lag - 1] : initial[kAr - lag + step - 1];
                for (var equation = 0; equation < k; equation++)
                {
                    var total = 0.0;
                    for (var variable = 0; variable < k; variable++)
                    {
                        total += coefficients[lag - 1][equation][variable] * previous[variable];
                    }
                    value[equation] += total;
                }
            }
            forecast[step - 1] = value;
        }
        return forecast;
    }

    private static double GammaAt(VecmEstimate estimate, int row, int column) =>
        column < estimate.Gamma[row].Length ? estimate.Gamma[row][column] : 0.0;

    // ------------------------------------------------------------------ utilidades

    private static double[][] Residualize(double[][] matrix, double[][] regressors)
    {
        if (regressors.Length == 0 || matrix.Length == 0) return matrix;
        var xx = Multiply(regressors, Transpose(regressors));
        var inverse = LinearAlgebra.Inverse(xx);
        if (inverse.IsError) return matrix;
        var product = Multiply(Multiply(matrix, Transpose(regressors)), inverse.Value);
        var fitted = Multiply(product, regressors);
        var output = new double[matrix.Length][];
        for (var row = 0; row < matrix.Length; row++)
        {
            output[row] = new double[matrix[row].Length];
            for (var column = 0; column < matrix[row].Length; column++)
            {
                output[row][column] = matrix[row][column] - fitted[row][column];
            }
        }
        return output;
    }

    /// <summary>Inversa de la raíz cuadrada simétrica de una matriz definida positiva.</summary>
    private static double[][] InverseSqrtSymmetric(double[][] matrix)
    {
        var size = matrix.Length;
        var eigen = LinearAlgebra.EigenSymmetric(MatrixBuilder.Symmetrize(matrix));
        var output = LinearAlgebra.Zeros(size, size);
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                var total = 0.0;
                for (var index = 0; index < size; index++)
                {
                    var value = eigen.Values[index];
                    if (!(value > 0)) continue;
                    total += eigen.Vectors[row][index] * eigen.Vectors[column][index] / Math.Sqrt(value);
                }
                output[row][column] = total;
            }
        }
        return output;
    }

    private static double[][] Scale(double[][] matrix, double factor)
    {
        var output = new double[matrix.Length][];
        for (var row = 0; row < matrix.Length; row++)
        {
            output[row] = new double[matrix[row].Length];
            for (var column = 0; column < matrix[row].Length; column++) output[row][column] = matrix[row][column] * factor;
        }
        return output;
    }

    private static double[] Ones(int count)
    {
        var values = new double[count];
        for (var index = 0; index < count; index++) values[index] = 1;
        return values;
    }

    private static double[] LinearTrend(int count, int order, bool coint)
    {
        var values = new double[count];
        for (var index = 0; index < count; index++) values[index] = index + order + (coint ? 0 : 1);
        return values;
    }

    private static double[][] Transpose(double[][] matrix) => LinearAlgebra.Transpose(matrix);

    private static double[][] Multiply(double[][] a, double[][] b) => LinearAlgebra.Multiply(a, b);
}
