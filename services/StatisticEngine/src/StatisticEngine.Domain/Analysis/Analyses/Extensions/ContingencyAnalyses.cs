using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Tasas, proporciones y tablas de contingencia portadas de Sigma.</summary>
internal static class ContingencyAnalyses
{
    private const string Category = "Pruebas adicionales";

    private static readonly AnalysisParamDefinition Alpha = ExtendedHelpers.Alpha();

    private static readonly AnalysisDefinition McNemar = new(
        "mcnemar",
        "McNemar (dos mediciones binarias)",
        Category,
        "Compara dos mediciones binarias del mismo sujeto (antes/después, dos pruebas diagnósticas).",
        [],
        [],
        [
            new(
                "method",
                "Método",
                AnalysisParamKinds.Select,
                "auto",
                [
                    new("auto", "Automático (exacto con pocos discordantes)"),
                    new("exact", "Exacto (binomial)"),
                    new("chi2", "χ² con corrección de continuidad")
                ],
                false),
            Alpha
        ],
        RunMcNemar,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "No paramétricas y datos pareados",
        Roles:
        [
            new ColumnRole("a", "Medición A (2 valores)", ColumnKinds.Any),
            new ColumnRole("b", "Medición B (2 valores)", ColumnKinds.Any)
        ]);

    private static readonly AnalysisDefinition Cochran = new(
        "cochran-q",
        "Q de Cochran (k mediciones binarias)",
        Category,
        "Generaliza McNemar a 3 o más mediciones binarias (0/1) de los mismos sujetos.",
        [],
        [],
        [Alpha],
        RunCochran,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "No paramétricas y datos pareados",
        Roles:
        [
            new ColumnRole("vars", "Mediciones 0/1", ColumnKinds.Numeric, Multiple: true, MinCount: 3)
        ]);

    private static readonly AnalysisDefinition Poisson = new(
        "poisson-rate",
        "Tasas de Poisson (1 y 2 muestras)",
        Category,
        "Contrasta una tasa de eventos con un valor, o compara dos tasas, con intervalos exactos. Se escriben los conteos: no necesita dataset.",
        [],
        [],
        [
            new("design", "Diseño", AnalysisParamKinds.Select, "one", [new("one", "Una tasa"), new("two", "Dos tasas")], false),
            new("count1", "Eventos observados (muestra 1)", AnalysisParamKinds.Integer, 10.0, [], false, Minimum: 0),
            new("exposure1", "Exposición (muestra 1): horas, personas-año, unidades…", AnalysisParamKinds.Number, 10.0, [], false, Minimum: 1e-9),
            new("rate0", "Tasa de referencia", AnalysisParamKinds.Number, 1.0, [], false, Minimum: 1e-9, VisibleIf: new("design", ["one"])),
            new("method1", "Método", AnalysisParamKinds.Select, "exact-c",
                [new("exact-c", "Exacto"), new("score", "Score"), new("wald", "Wald")], false,
                VisibleIf: new("design", ["one"])),
            new("count2", "Eventos observados (muestra 2)", AnalysisParamKinds.Integer, 15.0, [], false, Minimum: 0,
                VisibleIf: new("design", ["two"])),
            new("exposure2", "Exposición (muestra 2)", AnalysisParamKinds.Number, 12.0, [], false, Minimum: 1e-9,
                VisibleIf: new("design", ["two"])),
            new("ratio0", "Razón de tasas bajo H₀", AnalysisParamKinds.Number, 1.0, [], false, Minimum: 1e-9,
                VisibleIf: new("design", ["two"])),
            new("method2", "Prueba", AnalysisParamKinds.Select, "exact-cond",
                [
                    new("exact-cond", "Exacta condicional"),
                    new("cond-midp", "Exacta con valor p medio"),
                    new("score", "Score"),
                    new("wald-log", "Wald (logarítmica)")
                ],
                false,
                VisibleIf: new("design", ["two"])),
            ExtendedHelpers.Alternative(),
            Alpha
        ],
        RunPoisson,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Tasas, proporciones y tablas",
        Roles: []);

    private static readonly AnalysisDefinition OddsRatio = new(
        "odds-ratio",
        "Odds ratio y riesgo relativo (2×2)",
        Category,
        "Mide la asociación entre una exposición y un evento binarios, con intervalos y pruebas exacta de Fisher y χ².",
        [],
        [],
        [Alpha],
        RunOddsRatio,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Tasas, proporciones y tablas",
        Roles:
        [
            new ColumnRole("exposure", "Exposición (2 valores; el mayor es «expuesto»)", ColumnKinds.Any),
            new ColumnRole("outcome", "Evento (2 valores; el mayor es «evento»)", ColumnKinds.Any)
        ]);

    private static readonly AnalysisDefinition Cmh = new(
        "mantel-haenszel",
        "Cochran-Mantel-Haenszel (2×2 estratificadas)",
        Category,
        "Odds ratio común y prueba de asociación controlando por una variable de estrato, con prueba de homogeneidad de Breslow-Day.",
        [],
        [],
        [Alpha],
        RunCmh,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Tasas, proporciones y tablas",
        Roles:
        [
            new ColumnRole("exposure", "Exposición (2 valores)", ColumnKinds.Any),
            new ColumnRole("outcome", "Evento (2 valores)", ColumnKinds.Any),
            new ColumnRole("stratum", "Estrato", ColumnKinds.Categorical)
        ]);

    private static readonly Lazy<IReadOnlyList<AnalysisDefinition>> LazyAll =
        new(() => [McNemar, Cochran, Poisson, OddsRatio, Cmh], isThreadSafe: true);

    public static IReadOnlyList<AnalysisDefinition> All => LazyAll.Value;

    private static Result<AnalysisResult> RunMcNemar(AnalysisInput input)
    {
        var frame = ExtendedHelpers.FrameOf(input);
        var a = ExtendedHelpers.FirstRole(input, "a", "Medición A");
        var b = ExtendedHelpers.FirstRole(input, "b", "Medición B");
        var aLevels = frame.Levels(a);
        var bLevels = frame.Levels(b);
        if (aLevels.Length != 2) return ExtendedHelpers.Invalid($"«{a}» debe tener exactamente 2 valores distintos (tiene {aLevels.Length}).");
        if (bLevels.Length != 2) return ExtendedHelpers.Invalid($"«{b}» debe tener exactamente 2 valores distintos (tiene {bLevels.Length}).");
        var aPositive = aLevels[1];
        var bPositive = bLevels[1];

        var rows = new List<(string? A, string? B)>(frame.RowCount);
        var complete = frame.CompleteCases([a, b]);
        for (var row = 0; row < complete.RowCount; row++)
        {
            rows.Add((complete.Texts(a)[row], complete.Texts(b)[row]));
        }

        var table = new int[2, 2];
        var firstPositive = 0;
        var secondPositive = 0;
        foreach (var (first, second) in rows)
        {
            var one = first == aPositive;
            var two = second == bPositive;
            table[one ? 0 : 1, two ? 0 : 1]++;
            if (one) firstPositive++;
            if (two) secondPositive++;
        }
        var discordant = table[0, 1] + table[1, 0];
        if (discordant == 0)
        {
            return ExtendedHelpers.Invalid("No hay pares discordantes: las dos mediciones coinciden en todos los casos.");
        }

        var method = input.Pstr("method", "auto");
        var exact = method == "exact" || (method == "auto" && discordant < 25);
        double pValue;
        if (exact)
        {
            pValue = ExtendedHelpers.BinomialMinlike(table[0, 1], discordant, 0.5);
        }
        else
        {
            var statistic = Math.Pow(Math.Abs(table[0, 1] - table[1, 0]) - 1, 2) / discordant;
            pValue = ExtendedStatistics.ChiSquareSf(statistic, 1);
        }
        var alpha = input.Pnum("alpha", 0.05);
        var total = table[0, 0] + table[0, 1] + table[1, 0] + table[1, 1];

        return Result.Ok(new AnalysisResult(
            "mcnemar",
            "Prueba de McNemar",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Pares", JsNumber.ToString(total)),
                    new TileItem("Discordantes", JsNumber.ToString(discordant)),
                    new TileItem("Valor p", ExtendedHelpers.P(pValue)),
                    new TileItem("Método", exact ? "exacto (binomial)" : "χ² con corrección")
                ]),
                SigmaText.Decision(pValue, alpha, "las dos mediciones tienen la misma proporción de positivos"),
                new TableSection(
                    "Tabla de pares",
                    ["", $"{b} positivo", $"{b} negativo"],
                    [
                        new[] { $"{a} positivo", JsNumber.ToString(table[0, 0]), JsNumber.ToString(table[0, 1]) },
                        new[] { $"{a} negativo", JsNumber.ToString(table[1, 0]), JsNumber.ToString(table[1, 1]) }
                    ]),
                new TableSection(
                    "Proporciones de positivos",
                    ["Medición", "Proporción"],
                    [
                        new[] { a, ExtendedHelpers.N(firstPositive / (double)rows.Count) },
                        new[] { b, ExtendedHelpers.N(secondPositive / (double)rows.Count) }
                    ])
            ],
            Subtitle: $"{a} frente a {b} (positivo = {aPositive} / {bPositive})",
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["p"] = pValue,
                ["discordant"] = discordant
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunCochran(AnalysisInput input)
    {
        var frame = ExtendedHelpers.FrameOf(input);
        var names = input.RoleNames("vars", true);
        if (names.Count < 3) return ExtendedHelpers.Invalid("Se necesitan al menos 3 mediciones.");
        var complete = frame.CompleteCases(names);
        if (complete.RowCount < 5) return ExtendedHelpers.Invalid("Se necesitan al menos 5 filas completas.");
        var data = new double[complete.RowCount][];
        for (var row = 0; row < complete.RowCount; row++)
        {
            data[row] = new double[names.Count];
            for (var column = 0; column < names.Count; column++)
            {
                var value = complete.Numeric(names[column])[row];
                if (value != 0 && value != 1)
                {
                    return ExtendedHelpers.Invalid("Las columnas deben contener solo 0 (fracaso) y 1 (éxito).");
                }
                data[row][column] = value;
            }
        }

        var k = names.Count;
        var n = complete.RowCount;
        var columnSums = new double[k];
        var rowSums = new double[n];
        var total = 0.0;
        for (var row = 0; row < n; row++)
        {
            for (var column = 0; column < k; column++)
            {
                columnSums[column] += data[row][column];
                rowSums[row] += data[row][column];
                total += data[row][column];
            }
        }
        var numerator = (k - 1.0) * (k * columnSums.Sum(value => value * value) - total * total);
        var denominator = k * total - rowSums.Sum(value => value * value);
        var statistic = numerator / denominator;
        var pValue = ExtendedStatistics.ChiSquareSf(statistic, k - 1.0);
        var alpha = input.Pnum("alpha", 0.05);

        var rows = new List<IReadOnlyList<string>>();
        for (var column = 0; column < k; column++)
        {
            rows.Add([
                names[column],
                JsNumber.ToString(columnSums[column]),
                ExtendedHelpers.N(columnSums[column] / n)
            ]);
        }

        return Result.Ok(new AnalysisResult(
            "cochran-q",
            "Q de Cochran",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Sujetos", JsNumber.ToString(n)),
                    new TileItem("Q", ExtendedHelpers.N(statistic)),
                    new TileItem("gl", JsNumber.ToString(k - 1)),
                    new TileItem("Valor p", ExtendedHelpers.P(pValue))
                ]),
                SigmaText.Decision(pValue, alpha, "la proporción de éxitos es la misma en todas las mediciones"),
                new TableSection("Éxitos por medición", ["Medición", "Éxitos", "Proporción"], rows)
            ],
            Subtitle: $"{k} mediciones binarias en {n} sujetos",
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["statistic"] = statistic,
                ["p"] = pValue
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunPoisson(AnalysisInput input)
    {
        var alpha = input.Pnum("alpha", 0.05);
        var alternative = input.Pstr("alternative", "two-sided");
        if (input.Pstr("design", "one") == "one")
        {
            var count = (int)input.Pnum("count1", 10);
            var exposure = input.Pnum("exposure1", 10);
            var value = input.Pnum("rate0", 1);
            if (value <= 0) return ExtendedHelpers.Invalid("La tasa de referencia debe ser mayor que 0.");
            var method = input.Pstr("method1", "exact-c");
            var rate = count / exposure;
            double pValue;
            if (method.StartsWith("exact", StringComparison.Ordinal) || method.StartsWith("midp", StringComparison.Ordinal))
            {
                var expected = exposure * value;
                var pv1 = SpecialFunctions.Gammq(count + 1, expected);
                var pv2 = 1 - SpecialFunctions.Gammq(count, expected);
                pValue = alternative switch
                {
                    "larger" => pv2,
                    "smaller" => pv1,
                    _ => 2 * Math.Min(pv1, pv2)
                };
            }
            else
            {
                var standardError = Math.Sqrt((method == "score" ? value : rate) / exposure);
                var z = (rate - value) / standardError;
                pValue = alternative switch
                {
                    "larger" => ExtendedHelpers.NormalOneSided(z),
                    "smaller" => SpecialFunctions.NormalCdf(z),
                    _ => ExtendedHelpers.NormalTwoSided(z)
                };
            }
            pValue = Math.Clamp(pValue, 0, 1);

            var low = count > 0
                ? ExtendedStatistics.ChiSquareInv(alpha / 2, 2 * count) / (2 * exposure)
                : 0;
            var high = ExtendedStatistics.ChiSquareInv(1 - alpha / 2, 2 * (count + 1)) / (2 * exposure);

            return Result.Ok(new AnalysisResult(
                "poisson-rate",
                "Tasa de Poisson (una muestra)",
                [
                    new TilesSection("Resumen",
                    [
                        new TileItem("Tasa observada", ExtendedHelpers.N(rate)),
                        new TileItem("Valor p", ExtendedHelpers.P(pValue))
                    ]),
                    SigmaText.Decision(pValue, alpha, $"la tasa es {ExtendedHelpers.G(value)}"),
                    new TableSection(
                        "Resultado",
                        ["Eventos", "Exposición", "Tasa", $"IC {SigmaText.General(100 * (1 - alpha))} % inf.", "IC sup.", "Tasa H₀", "Valor p"],
                        [
                            new[]
                            {
                                JsNumber.ToString(count),
                                ExtendedHelpers.N(exposure),
                                ExtendedHelpers.N(rate),
                                ExtendedHelpers.N(low),
                                ExtendedHelpers.N(high),
                                ExtendedHelpers.N(value),
                                ExtendedHelpers.P(pValue)
                            }
                        ],
                        Note: $"Método: {method}.")
                ],
                Subtitle: $"{count} eventos en {ExtendedHelpers.G(exposure)} unidades de exposición",
                KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["rate"] = rate,
                    ["p"] = pValue
                },
                Origin: AnalysisCategories.SigmaOrigin));
        }

        var count1 = (int)input.Pnum("count1", 10);
        var exposure1 = input.Pnum("exposure1", 10);
        var count2 = (int)input.Pnum("count2", 15);
        var exposure2 = input.Pnum("exposure2", 12);
        var ratio0 = input.Pnum("ratio0", 1);
        var method2 = input.Pstr("method2", "exact-cond");
        var ratio = count1 / exposure1 / (count2 / exposure2);
        var relative = ratio0 * exposure1 / exposure2;

        double testP;
        if (method2 is "exact-cond" or "cond-midp")
        {
            var probability = relative / (1 + relative);
            var total = count1 + count2;
            testP = ExtendedHelpers.BinomialOneSided(count1, total, probability, alternative);
            if (method2 == "cond-midp")
            {
                testP -= 0.5 * ExtendedStatistics.BinomialPmf(count1, total, probability);
            }
        }
        else if (method2 == "score")
        {
            var statistic = (count1 - count2 * relative) / Math.Sqrt((count1 + count2) * relative);
            testP = alternative switch
            {
                "larger" => ExtendedHelpers.NormalOneSided(statistic),
                "smaller" => SpecialFunctions.NormalCdf(statistic),
                _ => ExtendedHelpers.NormalTwoSided(statistic)
            };
        }
        else
        {
            var statistic = (Math.Log(count1 / (double)count2) - Math.Log(relative)) / Math.Sqrt(1.0 / count1 + 1.0 / count2);
            testP = alternative switch
            {
                "larger" => ExtendedHelpers.NormalOneSided(statistic),
                "smaller" => SpecialFunctions.NormalCdf(statistic),
                _ => ExtendedHelpers.NormalTwoSided(statistic)
            };
        }
        testP = Math.Clamp(testP, 0, 1);

        double ScoreP(double candidate)
        {
            var rd = candidate * exposure1 / exposure2;
            var statistic = (count1 - count2 * rd) / Math.Sqrt((count1 + count2) * rd);
            return ExtendedHelpers.NormalTwoSided(statistic);
        }
        var lower = InvertScore(ratio, true);
        var upper = InvertScore(ratio, false);

        return Result.Ok(new AnalysisResult(
            "poisson-rate",
            "Comparación de dos tasas de Poisson",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Tasa 1", ExtendedHelpers.N(count1 / exposure1)),
                    new TileItem("Tasa 2", ExtendedHelpers.N(count2 / exposure2)),
                    new TileItem("Razón de tasas", ExtendedHelpers.N(ratio)),
                    new TileItem("Valor p", ExtendedHelpers.P(testP))
                ]),
                SigmaText.Decision(testP, alpha, $"la razón de tasas es {ExtendedHelpers.G(ratio0)}"),
                new TableSection(
                    "Resultado",
                    ["Muestra", "Eventos", "Exposición", "Tasa"],
                    [
                        new[] { "1", JsNumber.ToString(count1), ExtendedHelpers.N(exposure1), ExtendedHelpers.N(count1 / exposure1) },
                        new[] { "2", JsNumber.ToString(count2), ExtendedHelpers.N(exposure2), ExtendedHelpers.N(count2 / exposure2) }
                    ]),
                new TableSection(
                    "Razón de tasas (1 / 2)",
                    ["Razón", $"IC {SigmaText.General(100 * (1 - alpha))} % (score) inf.", "sup.", "Razón H₀", "Valor p"],
                    [[
                        ExtendedHelpers.N(ratio),
                        ExtendedHelpers.N(lower),
                        ExtendedHelpers.N(upper),
                        ExtendedHelpers.N(ratio0),
                        ExtendedHelpers.P(testP)
                    ]],
                    Note: $"Prueba: {method2}.")
            ],
            Subtitle: $"{count1}/{ExtendedHelpers.G(exposure1)} frente a {count2}/{ExtendedHelpers.G(exposure2)}",
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["ratio"] = ratio,
                ["p"] = testP
            },
            Origin: AnalysisCategories.SigmaOrigin));

        double InvertScore(double center, bool below)
        {
            var low = below ? 1e-9 : center;
            var high = below ? center : center * 1e6 + 1;
            for (var iteration = 0; iteration < 200; iteration++)
            {
                var middle = (low + high) / 2;
                var p = ScoreP(middle);
                if (below)
                {
                    if (p < alpha) low = middle;
                    else high = middle;
                }
                else
                {
                    if (p > alpha) low = middle;
                    else high = middle;
                }
                if (high - low < 1e-12 * Math.Max(1, high)) break;
            }
            return (low + high) / 2;
        }
    }

    private static Result<AnalysisResult> RunOddsRatio(AnalysisInput input)
    {
        var frame = ExtendedHelpers.FrameOf(input);
        var exposure = ExtendedHelpers.FirstRole(input, "exposure", "Exposición");
        var outcome = ExtendedHelpers.FirstRole(input, "outcome", "Evento");
        var exposureLevels = frame.Levels(exposure);
        var outcomeLevels = frame.Levels(outcome);
        if (exposureLevels.Length != 2) return ExtendedHelpers.Invalid($"«{exposure}» debe tener exactamente 2 valores distintos.");
        if (outcomeLevels.Length != 2) return ExtendedHelpers.Invalid($"«{outcome}» debe tener exactamente 2 valores distintos.");
        var exposed = exposureLevels[1];
        var events = outcomeLevels[1];

        var table = new double[2, 2];
        var data = frame.CompleteCases([exposure, outcome]);
        var exposureTexts = data.Texts(exposure);
        var outcomeTexts = data.Texts(outcome);
        for (var row = 0; row < data.RowCount; row++)
        {
            var isExposed = exposureTexts[row] == exposed;
            var isEvent = outcomeTexts[row] == events;
            table[isExposed ? 0 : 1, isEvent ? 0 : 1]++;
        }
        if (table[0, 0] + table[0, 1] == 0 || table[1, 0] + table[1, 1] == 0
            || table[0, 0] + table[1, 0] == 0 || table[0, 1] + table[1, 1] == 0)
        {
            return ExtendedHelpers.Invalid("La tabla tiene una fila o columna vacía.");
        }

        var shifted = (double[,])table.Clone();
        if (shifted.Cast<double>().Any(value => value == 0))
        {
            for (var i = 0; i < 2; i++)
            {
                for (var j = 0; j < 2; j++) shifted[i, j] += 0.5;
            }
        }
        var a = shifted[0, 0];
        var b = shifted[0, 1];
        var c = shifted[1, 0];
        var d = shifted[1, 1];
        var oddsRatio = a * d / (b * c);
        var riskRatio = (a / (a + b)) / (c / (c + d));
        var riskExposed = table[0, 0] / (table[0, 0] + table[0, 1]);
        var riskUnexposed = table[1, 0] / (table[1, 0] + table[1, 1]);
        var alpha = input.Pnum("alpha", 0.05);
        var z = SpecialFunctions.NormalInv(1 - alpha / 2);

        var logOdds = Math.Log(oddsRatio);
        var oddsSe = Math.Sqrt(1 / a + 1 / b + 1 / c + 1 / d);
        var oddsLow = Math.Exp(logOdds - z * oddsSe);
        var oddsHigh = Math.Exp(logOdds + z * oddsSe);
        var logRisk = Math.Log(riskRatio);
        var riskSe = Math.Sqrt(1 / a - 1 / (a + b) + 1 / c - 1 / (c + d));
        var riskLow = Math.Exp(logRisk - z * riskSe);
        var riskHigh = Math.Exp(logRisk + z * riskSe);

        var fisherP = ExtendedHelpers.FisherExact((int)table[0, 0], (int)table[0, 1], (int)table[1, 0], (int)table[1, 1]);
        var chi = ExtendedHelpers.ContingencyChi2(
            [
                [table[0, 0], table[0, 1]],
                [table[1, 0], table[1, 1]]
            ],
            out var chiP);
        var ci = $"IC {SigmaText.General(100 * (1 - alpha))} %";

        return Result.Ok(new AnalysisResult(
            "odds-ratio",
            $"Odds ratio y riesgo relativo: {outcome} según {exposure}",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Odds ratio", ExtendedHelpers.N(oddsRatio)),
                    new TileItem("Riesgo relativo", ExtendedHelpers.N(riskRatio)),
                    new TileItem("Diferencia de riesgos", ExtendedHelpers.N(riskExposed - riskUnexposed)),
                    new TileItem("p (Fisher)", ExtendedHelpers.P(fisherP))
                ]),
                SigmaText.Decision(fisherP, alpha, "no hay asociación entre la exposición y el evento"),
                new TableSection(
                    "Tabla 2×2",
                    ["", $"{outcome} = {events}", "Sin el evento", "Total", "Riesgo"],
                    [
                        new[]
                        {
                            $"{exposure} = {exposed}",
                            JsNumber.ToString(table[0, 0]),
                            JsNumber.ToString(table[0, 1]),
                            JsNumber.ToString(table[0, 0] + table[0, 1]),
                            ExtendedHelpers.N(riskExposed)
                        },
                        new[]
                        {
                            "No expuestos",
                            JsNumber.ToString(table[1, 0]),
                            JsNumber.ToString(table[1, 1]),
                            JsNumber.ToString(table[1, 0] + table[1, 1]),
                            ExtendedHelpers.N(riskUnexposed)
                        }
                    ]),
                new TableSection(
                    "Medidas de asociación",
                    ["Medida", "Estimación", $"{ci} inf.", $"{ci} sup."],
                    [
                        new[] { "Odds ratio", ExtendedHelpers.N(oddsRatio), ExtendedHelpers.N(oddsLow), ExtendedHelpers.N(oddsHigh) },
                        new[] { "Riesgo relativo", ExtendedHelpers.N(riskRatio), ExtendedHelpers.N(riskLow), ExtendedHelpers.N(riskHigh) }
                    ],
                    Note: "Si hay celdas en cero se suma 0.5 a todas (corrección de Haldane)."),
                new TableSection(
                    "Pruebas de asociación",
                    ["Prueba", "Estadístico", "Valor p"],
                    [
                        new[] { "Exacta de Fisher (razón de odds)", ExtendedHelpers.N(oddsRatio), ExtendedHelpers.P(fisherP) },
                        new[] { "χ² de Pearson", ExtendedHelpers.N(chi), ExtendedHelpers.P(chiP) }
                    ])
            ],
            Subtitle: $"expuesto = {exposed} · evento = {events}",
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["odds_ratio"] = oddsRatio,
                ["risk_ratio"] = riskRatio,
                ["fisher_p"] = fisherP
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunCmh(AnalysisInput input)
    {
        var frame = ExtendedHelpers.FrameOf(input);
        var exposure = ExtendedHelpers.FirstRole(input, "exposure", "Exposición");
        var outcome = ExtendedHelpers.FirstRole(input, "outcome", "Evento");
        var stratum = ExtendedHelpers.FirstRole(input, "stratum", "Estrato");
        var exposureLevels = frame.Levels(exposure);
        var outcomeLevels = frame.Levels(outcome);
        if (exposureLevels.Length != 2) return ExtendedHelpers.Invalid($"«{exposure}» debe tener exactamente 2 valores distintos.");
        if (outcomeLevels.Length != 2) return ExtendedHelpers.Invalid($"«{outcome}» debe tener exactamente 2 valores distintos.");
        var exposed = exposureLevels[1];
        var events = outcomeLevels[1];

        var data = frame.CompleteCases([exposure, outcome, stratum]);
        var exposureTexts = data.Texts(exposure);
        var outcomeTexts = data.Texts(outcome);
        var stratumTexts = data.Texts(stratum);
        var levels = data.Levels(stratum);
        var tables = new List<double[,]>();
        var labels = new List<string>();
        var rows = new List<IReadOnlyList<string>>();
        foreach (var level in levels)
        {
            var table = new double[2, 2];
            for (var row = 0; row < data.RowCount; row++)
            {
                if (stratumTexts[row] != level) continue;
                var isExposed = exposureTexts[row] == exposed;
                var isEvent = outcomeTexts[row] == events;
                table[isExposed ? 0 : 1, isEvent ? 0 : 1]++;
            }
            if (table.Cast<double>().Sum() == 0) continue;
            tables.Add(table);
            labels.Add(level);
            var shifted = (double[,])table.Clone();
            if (shifted.Cast<double>().Any(value => value == 0))
            {
                for (var i = 0; i < 2; i++)
                {
                    for (var j = 0; j < 2; j++) shifted[i, j] += 0.5;
                }
            }
            rows.Add([
                level,
                JsNumber.ToString(table.Cast<double>().Sum()),
                ExtendedHelpers.N(shifted[0, 0] * shifted[1, 1] / (shifted[0, 1] * shifted[1, 0]))
            ]);
        }
        if (tables.Count < 2) return ExtendedHelpers.Invalid("Se necesitan al menos 2 estratos con datos.");

        double sumAd = 0;
        double sumBc = 0;
        double sumA = 0;
        double sumE = 0;
        double sumV = 0;
        var alpha = input.Pnum("alpha", 0.05);
        foreach (var table in tables)
        {
            var a = table[0, 0];
            var b = table[0, 1];
            var c = table[1, 0];
            var d = table[1, 1];
            var n = a + b + c + d;
            sumAd += a * d / n;
            sumBc += b * c / n;
            sumA += a;
            sumE += (a + b) * (a + c) / n;
            sumV += (a + b) * (c + d) * (a + c) * (b + d) / (n * n * (n - 1));
        }
        var pooled = sumAd / sumBc;
        var cmh = Math.Pow(sumA - sumE, 2) / sumV;
        var cmhP = ExtendedStatistics.ChiSquareSf(cmh, 1);

        var breslowP = BreslowDay(tables, pooled);
        var z = SpecialFunctions.NormalInv(1 - alpha / 2);
        var adns = 0.0;
        var bcns = 0.0;
        var termA = 0.0;
        var termB = 0.0;
        var termC = 0.0;
        foreach (var table in tables)
        {
            var a = table[0, 0];
            var bb = table[0, 1];
            var c = table[1, 0];
            var d = table[1, 1];
            var n = a + bb + c + d;
            var ad = a * d;
            var bc = bb * c;
            var apd = a + d;
            adns += ad / n;
            bcns += bc / n;
            termA += apd * ad / (n * n);
            termB += apd * bc / (n * n) + (1 - apd / n) * ad / n;
            termC += (1 - apd / n) * bc / n;
        }
        var variance = (termA / (adns * adns) + termB / (adns * bcns) + termC / (bcns * bcns)) / 2;
        var pooledLow = Math.Exp(Math.Log(pooled) - z * Math.Sqrt(Math.Max(0, variance)));
        var pooledHigh = Math.Exp(Math.Log(pooled) + z * Math.Sqrt(Math.Max(0, variance)));

        var heterogeneous = breslowP < alpha;
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Odds ratio común", ExtendedHelpers.N(pooled)),
                new TileItem("χ² de CMH", ExtendedHelpers.N(cmh)),
                new TileItem("p (CMH)", ExtendedHelpers.P(cmhP)),
                new TileItem("p (Breslow-Day)", ExtendedHelpers.P(breslowP))
            ]),
            SigmaText.Decision(cmhP, alpha, "la exposición y el evento son independientes en todos los estratos"),
            new TableSection("Odds ratio por estrato", [stratum, "n", "Odds ratio"], rows),
            new TableSection(
                "Resultado combinado",
                ["Medida", "Estimación", $"IC {SigmaText.General(100 * (1 - alpha))} % inf.", $"IC {SigmaText.General(100 * (1 - alpha))} % sup.", "Valor p"],
                [[
                    "Odds ratio común (Mantel-Haenszel)",
                    ExtendedHelpers.N(pooled),
                    ExtendedHelpers.N(pooledLow),
                    ExtendedHelpers.N(pooledHigh),
                    ExtendedHelpers.P(cmhP)
                ]]),
            new AlertSection(
                "Conclusión",
                heterogeneous ? "warning" : "info",
                $"Homogeneidad de los odds ratio (Breslow-Day): p = {SigmaText.PValue(breslowP)}. "
                + (heterogeneous
                    ? "Los odds ratio difieren entre estratos: el valor combinado puede no ser representativo."
                    : "No hay evidencia de que difieran entre estratos."))
        };

        return Result.Ok(new AnalysisResult(
            "mantel-haenszel",
            $"Cochran-Mantel-Haenszel: {outcome} según {exposure}",
            sections,
            Subtitle: $"estratificado por {stratum} ({tables.Count} estratos)",
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["pooled_or"] = pooled,
                ["cmh_p"] = cmhP,
                ["breslow_day_p"] = breslowP
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double BreslowDay(IReadOnlyList<double[,]> tables, double pooledOdds)
    {
        var a = 1 - pooledOdds;
        var statistic = 0.0;
        foreach (var table in tables)
        {
            var row1 = table[0, 0] + table[0, 1];
            var col1 = table[0, 0] + table[1, 0];
            var dma = table[1, 1] - table[0, 0];
            var b = pooledOdds * (row1 + col1) + dma;
            var c = -pooledOdds * row1 * col1;
            var discriminant = Math.Sqrt(b * b - 4 * a * c);
            var expected = (-b + discriminant) / (2 * a);
            var variance = 1 / expected
                           + 1 / (col1 - expected)
                           + 1 / (row1 - expected)
                           + 1 / (dma + expected);
            variance = 1 / variance;
            statistic += Math.Pow(table[0, 0] - expected, 2) / variance;
        }
        return ExtendedStatistics.ChiSquareSf(statistic, tables.Count - 1.0);
    }
}
