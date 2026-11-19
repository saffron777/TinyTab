using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Potencia y tamaño de muestra de los diseños que no cubre el baseline (Sigma power/power_designs).</summary>
internal static class PowerDesignAnalyses
{
    private const string Category = "Potencia y tamaño de muestra";

    private static readonly AnalysisParamDefinition Alpha = new(
        "alpha",
        "Nivel de significancia α",
        AnalysisParamKinds.Number,
        0.05,
        [],
        false,
        Minimum: 0.0001,
        Maximum: 0.5);

    private static readonly AnalysisParamDefinition SolveForN = new(
        "solve_for",
        "Calcular",
        AnalysisParamKinds.Select,
        "n",
        [
            new("n", "Tamaño de muestra"),
            new("power", "Potencia"),
            new("effect", "Efecto detectable")
        ],
        false);

    private static readonly AnalysisParamDefinition Power = new(
        "power",
        "Potencia deseada",
        AnalysisParamKinds.Number,
        0.8,
        [],
        false,
        Minimum: 0.01,
        Maximum: 0.999,
        VisibleIf: new("solve_for", ["n", "effect"]));

    private static readonly AnalysisParamDefinition N = new(
        "n",
        "Tamaño de muestra",
        AnalysisParamKinds.Number,
        30.0,
        [],
        false,
        Minimum: 2,
        VisibleIf: new("solve_for", ["power", "effect"]));

    private static readonly AnalysisParamDefinition AlternativeT = new(
        "alternative",
        "Hipótesis alternativa",
        AnalysisParamKinds.Select,
        "two-sided",
        [
            new("two-sided", "Bilateral (≠)"),
            new("larger", "Cola derecha (>)"),
            new("smaller", "Cola izquierda (<)")
        ],
        false);

    private static readonly AnalysisParamDefinition AlternativeCorr = new(
        "alternative",
        "Hipótesis alternativa",
        AnalysisParamKinds.Select,
        "two-sided",
        [new("two-sided", "Bilateral (≠ 0)"), new("larger", "Unilateral")],
        false);

    private static readonly AnalysisDefinition Correlation = new(
        "power-correlation",
        "Correlación: potencia y tamaño de muestra",
        Category,
        "Cuántos pares de observaciones hacen falta para detectar una correlación r (transformación z de Fisher).",
        [],
        [],
        [
            SolveForN,
            new(
                "r",
                "Correlación r a detectar",
                AnalysisParamKinds.Number,
                0.3,
                [],
                false,
                Minimum: -0.999,
                Maximum: 0.999,
                VisibleIf: new("solve_for", ["n", "power"])),
            new(
                "n",
                "Tamaño de muestra (pares)",
                AnalysisParamKinds.Number,
                50.0,
                [],
                false,
                Minimum: 2,
                VisibleIf: new("solve_for", ["power", "effect"])),
            Power,
            Alpha,
            AlternativeCorr
        ],
        RunCorrelation,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Más diseños",
        Roles: []);

    private static readonly AnalysisDefinition Tost = new(
        "power-tost",
        "Equivalencia (TOST) de dos medias: potencia y tamaño de muestra",
        Category,
        "Tamaño de muestra o potencia para demostrar que dos medias son equivalentes dentro de un margen.",
        [],
        [],
        [
            new(
                "solve_for",
                "Calcular",
                AnalysisParamKinds.Select,
                "n",
                [new("n", "Tamaño de muestra"), new("power", "Potencia")],
                false),
            new("margin", "Límite de equivalencia (±)", AnalysisParamKinds.Number, 2.0, [], false, Minimum: 1e-9),
            new("difference", "Diferencia verdadera esperada entre medias", AnalysisParamKinds.Number, 0.0, [], false),
            new("sd", "Desviación estándar σ", AnalysisParamKinds.Number, 3.0, [], false, Minimum: 1e-9),
            new(
                "n",
                "Tamaño de muestra por grupo",
                AnalysisParamKinds.Number,
                30.0,
                [],
                false,
                Minimum: 2,
                VisibleIf: new("solve_for", ["power", "effect"])),
            Power,
            Alpha
        ],
        RunTost,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Más diseños",
        Roles: []);

    private static readonly AnalysisDefinition PairedT = new(
        "power-paired-t",
        "Potencia y tamaño de muestra (t pareada)",
        Category,
        "Despeje el tamaño, la potencia o el efecto detectable de una prueba t de muestras pareadas.",
        [],
        [],
        [
            new(
                "solve_for",
                "Calcular",
                AnalysisParamKinds.Select,
                "n",
                [
                    new("n", "Tamaño de muestra"),
                    new("power", "Potencia"),
                    new("effect", "Efecto detectable (d de Cohen)")
                ],
                false),
            new(
                "effect_size",
                "Efecto estandarizado d",
                AnalysisParamKinds.Number,
                0.5,
                [],
                false,
                Help: "0.2 pequeño · 0.5 mediano · 0.8 grande.",
                VisibleIf: new("solve_for", ["n", "power"])),
            new(
                "n",
                "Tamaño de muestra",
                AnalysisParamKinds.Number,
                30.0,
                [],
                false,
                Minimum: 2,
                VisibleIf: new("solve_for", ["power", "effect"])),
            Power,
            Alpha,
            AlternativeT
        ],
        RunPairedT,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Pruebas t",
        Roles: [],
        Heavy: true);

    private static readonly AnalysisDefinition Proportions = new(
        "power-prop2",
        "Proporciones (dos muestras): potencia y tamaño de muestra",
        Category,
        "Tamaño de muestra, potencia o proporción detectable para comparar dos proporciones independientes.",
        [],
        [],
        [
            new(
                "solve_for",
                "Calcular",
                AnalysisParamKinds.Select,
                "n",
                [
                    new("n", "Tamaño de muestra"),
                    new("power", "Potencia"),
                    new("effect", "Proporción detectable")
                ],
                false),
            new("p0", "Proporción de referencia (grupo 2)", AnalysisParamKinds.Number, 0.5, [], false, Minimum: 0.0001, Maximum: 0.9999),
            new(
                "p1",
                "Proporción alternativa a detectar",
                AnalysisParamKinds.Number,
                0.6,
                [],
                false,
                Minimum: 0.0001,
                Maximum: 0.9999,
                VisibleIf: new("solve_for", ["n", "power"])),
            N,
            Power,
            Alpha,
            AlternativeT,
            new(
                "ratio",
                "Razón n₂ / n₁",
                AnalysisParamKinds.Number,
                1.0,
                [],
                false,
                Minimum: 0.1,
                Maximum: 20)
        ],
        RunProportions,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Más diseños",
        Roles: []);

    private static readonly Lazy<IReadOnlyList<AnalysisDefinition>> LazyAll =
        new(() => [Correlation, Tost, PairedT, Proportions], isThreadSafe: true);

    public static IReadOnlyList<AnalysisDefinition> All => LazyAll.Value;

    private static Result<AnalysisResult> RunCorrelation(AnalysisInput input)
    {
        var solve = input.Pstr("solve_for", "n");
        var alpha = input.Pnum("alpha", 0.05);
        var alternative = input.Pstr("alternative", "two-sided");
        var za = SpecialFunctions.NormalInv(1 - alpha / (alternative == "two-sided" ? 2 : 1));
        var r = input.Pnum("r", 0.3);
        var n = input.Pnum("n", 50);
        var power = input.Pnum("power", 0.8);
        if (solve != "effect" && (Math.Abs(r) >= 1 || r == 0))
        {
            return ExtendedHelpers.Invalid("La correlación r debe estar entre −1 y 1 y ser distinta de 0.");
        }

        double PowerAt(double rho, double size)
        {
            var shift = Math.Abs(Math.Atanh(rho)) * Math.Sqrt(size - 3);
            var beta = SpecialFunctions.NormalCdf(shift - za);
            return beta + (alternative == "two-sided" ? SpecialFunctions.NormalCdf(-shift - za) : 0);
        }

        if (solve == "n")
        {
            n = Math.Pow((za + SpecialFunctions.NormalInv(power)) / Math.Abs(Math.Atanh(r)), 2) + 3;
        }
        else if (solve == "power")
        {
            if (n <= 3) return ExtendedHelpers.Invalid("El tamaño de muestra debe ser mayor que 3.");
            power = PowerAt(r, n);
        }
        else
        {
            if (n <= 3) return ExtendedHelpers.Invalid("El tamaño de muestra debe ser mayor que 3.");
            var z = Bisect(1e-6, 6.0, candidate => PowerAt(Math.Tanh(candidate), n), power);
            r = Math.Tanh(z);
        }
        if (!double.IsFinite(n) || !double.IsFinite(power) || !double.IsFinite(r))
        {
            return ExtendedHelpers.Error(DomainErrorCode.NonConvergence, "No se pudo despejar el valor pedido: revise el efecto, α y la potencia.");
        }
        var shownN = solve == "n" ? Math.Ceiling(n) : n;
        var actual = solve == "n" ? PowerAt(r, shownN) : power;

        return Report(
            "power-correlation",
            "Potencia y tamaño de muestra: correlación",
            $"α = {SigmaText.General(alpha)} · {AlternativeLabel(alternative, correlation: true)}",
            "Tamaño de muestra",
            "Correlación r",
            "pares de observaciones",
            solve, shownN, actual, Math.Abs(r), alpha,
            [["Transformación z de Fisher", Math.Atanh(Math.Abs(r))]],
            "Usa la transformación z de Fisher: n = ((z_α + z_β) / atanh r)² + 3.",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = shownN,
                ["power"] = actual,
                ["r"] = Math.Abs(r)
            },
            chartCurves: 3);
    }

    private static Result<AnalysisResult> RunTost(AnalysisInput input)
    {
        var solve = input.Pstr("solve_for", "n");
        var alpha = input.Pnum("alpha", 0.05);
        var margin = input.Pnum("margin", 2);
        var sd = input.Pnum("sd", 3);
        var delta = input.Pnum("difference", 0);
        var n = input.Pnum("n", 30);
        var power = input.Pnum("power", 0.8);
        if (Math.Abs(delta) >= margin)
        {
            return ExtendedHelpers.Invalid("La diferencia verdadera debe estar dentro de los límites de equivalencia (|diferencia| < margen).");
        }

        if (solve == "n")
        {
            n = Bracket(m => TostPower(delta, margin, sd, m, alpha), power, 3, 100000);
            if (!double.IsFinite(n) || n <= 0)
            {
                return ExtendedHelpers.Error(DomainErrorCode.NonConvergence,
                    "Con esos datos no se alcanza la potencia pedida: aumente el tamaño de muestra o el efecto.");
            }
        }
        else
        {
            if (n < 3) return ExtendedHelpers.Invalid("Se necesitan al menos 3 observaciones por grupo.");
            power = TostPower(delta, margin, sd, n, alpha);
        }
        var shownN = solve == "n" ? Math.Ceiling(n) : n;
        var actual = solve == "n" ? TostPower(delta, margin, sd, shownN, alpha) : power;

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Tamaño de muestra por grupo", JsNumber.ToString(shownN)),
                new TileItem("Potencia", ExtendedHelpers.N(actual)),
                new TileItem("Diferencia verdadera", ExtendedHelpers.N(delta)),
                new TileItem("Margen", ExtendedHelpers.N(margin))
            ])
        };
        if (solve == "n")
        {
            sections.Add(new AlertSection(
                "Aviso",
                "info",
                $"Se necesitan {SigmaText.General(shownN)} observaciones por grupo para demostrar equivalencia con potencia {SigmaText.Fixed4(actual)}."));
        }
        sections.Add(new TableSection(
            "Parámetros",
            ["Parámetro", "Valor"],
            [
                new[] { "Tamaño por grupo", JsNumber.ToString(shownN) },
                new[] { "Potencia", ExtendedHelpers.N(actual) },
                new[] { "Diferencia verdadera entre medias", ExtendedHelpers.N(delta) },
                new[] { "Límite de equivalencia (±)", ExtendedHelpers.N(margin) },
                new[] { "Desviación estándar σ", ExtendedHelpers.N(sd) },
                new[] { "α", ExtendedHelpers.N(alpha) }
            ],
            Note: "Potencia exacta de las dos pruebas t unilaterales con varianzas iguales."));

        return Result.Ok(new AnalysisResult(
            "power-tost",
            "Potencia y tamaño de muestra: equivalencia de dos medias (TOST)",
            sections,
            Subtitle: $"α = {SigmaText.General(alpha)} · límites ±{SigmaText.General(margin)} · σ = {SigmaText.General(sd)}",
            Charts: [new Charts.LineChart("Curvas de potencia", [new Charts.LineSeries("Potencia", [actual])], "Tamaño de muestra por grupo")],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = shownN,
                ["power"] = actual
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunPairedT(AnalysisInput input)
    {
        var solve = input.Pstr("solve_for", "n");
        var alpha = input.Pnum("alpha", 0.05);
        var alternative = input.Pstr("alternative", "two-sided");
        var effect = input.Pnum("effect_size", 0.5);
        var n = input.Pnum("n", 30);
        var power = input.Pnum("power", 0.8);
        if (solve != "effect" && (effect == 0 || !double.IsFinite(effect)))
        {
            return ExtendedHelpers.Invalid("El efecto d debe ser distinto de 0.");
        }

        double PowerAt(double d, double size)
        {
            var df = size - 1;
            var ncp = d * Math.Sqrt(size);
            var critical = InferenceDistributions.TQuantile(1 - (alternative == "two-sided" ? alpha / 2 : alpha), df);
            return alternative switch
            {
                "larger" => 1 - NoncentralTCdf(critical, df, ncp),
                "smaller" => NoncentralTCdf(-critical, df, ncp),
                _ => 1 - NoncentralTCdf(critical, df, ncp) + NoncentralTCdf(-critical, df, ncp)
            };
        }

        if (solve == "n")
        {
            n = Bracket(size => PowerAt(effect, size), power, 2, 100000);
        }
        else if (solve == "power")
        {
            power = PowerAt(effect, n);
        }
        else
        {
            effect = Bracket(d => PowerAt(d, n), power, 1e-6, 10);
        }
        if (!double.IsFinite(n) || !double.IsFinite(power) || !double.IsFinite(effect) || n < 2)
        {
            return ExtendedHelpers.Error(DomainErrorCode.NonConvergence,
                "No se pudo despejar el valor pedido: revise el efecto, α y la potencia.");
        }
        var shownN = solve == "n" ? Math.Ceiling(n) : n;
        var actual = solve == "n" ? PowerAt(effect, shownN) : power;

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Tamaño de muestra", JsNumber.ToString(solve == "n" ? shownN : n)),
                new TileItem("Potencia", ExtendedHelpers.N(actual)),
                new TileItem("Efecto (d)", ExtendedHelpers.N(effect)),
                new TileItem("α", ExtendedHelpers.N(alpha))
            ])
        };
        if (solve == "n")
        {
            sections.Add(new AlertSection(
                "Aviso",
                "info",
                $"Se necesitan {SigmaText.General(shownN)} observaciones para detectar d = {SigmaText.General(effect)} "
                + $"con potencia ≥ {SigmaText.General(power)} (potencia real: {SigmaText.Fixed4(actual)})."));
        }
        sections.Add(new TableSection(
            "Parámetros",
            ["Parámetro", "Valor"],
            [
                ["Diseño", "Muestras pareadas"],
                ["Efecto estandarizado d", ExtendedHelpers.N(effect)],
                ["Tamaño de muestra", JsNumber.ToString(solve == "n" ? shownN : n)],
                ["Potencia", ExtendedHelpers.N(actual)],
                ["α", ExtendedHelpers.N(alpha)]
            ]));

        return Result.Ok(new AnalysisResult(
            "power-paired-t",
            "Potencia y tamaño de muestra: prueba t (muestras pareadas)",
            sections,
            Subtitle: $"α = {SigmaText.General(alpha)} · {AlternativeLabel(alternative, correlation: false)}",
            Charts: [new Charts.LineChart("Curvas de potencia", [new Charts.LineSeries("Potencia", [actual])], "Tamaño de muestra")],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["design"] = "paired",
                ["solve_for"] = solve,
                ["n"] = shownN,
                ["power"] = actual,
                ["effect_size"] = effect,
                ["alpha"] = alpha
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunProportions(AnalysisInput input)
    {
        var solve = input.Pstr("solve_for", "n");
        var alpha = input.Pnum("alpha", 0.05);
        var alternative = input.Pstr("alternative", "two-sided");
        var p0 = input.Pnum("p0", 0.5);
        var p1 = input.Pnum("p1", 0.6);
        var n = input.Pnum("n", 30);
        var power = input.Pnum("power", 0.8);
        var ratio = input.Pnum("ratio", 1.0);
        if (solve is "n" or "power" && (p1 == p0))
        {
            return ExtendedHelpers.Invalid("La proporción alternativa debe ser distinta de la de referencia.");
        }
        if (p0 <= 0 || p0 >= 1) return ExtendedHelpers.Invalid("La proporción de referencia debe estar entre 0 y 1.");

        double H(double value) => Math.Abs(2 * Math.Asin(Math.Sqrt(value)) - 2 * Math.Asin(Math.Sqrt(p0)));
        double PowerAt(double alternativeP, double size)
        {
            var effect = H(alternativeP);
            var nc = effect * Math.Sqrt(size * ratio / (1 + ratio));
            var za = SpecialFunctions.NormalInv(1 - (alternative == "two-sided" ? alpha / 2 : alpha));
            var beta = SpecialFunctions.NormalCdf(nc - za);
            return beta + (alternative == "two-sided" ? SpecialFunctions.NormalCdf(-nc - za) : 0);
        }

        if (solve == "n")
        {
            var effect = H(p1);
            var za = SpecialFunctions.NormalInv(1 - (alternative == "two-sided" ? alpha / 2 : alpha));
            var zp = SpecialFunctions.NormalInv(power);
            n = Math.Pow((za + zp) / effect, 2) * (1 + ratio) / ratio;
        }
        else if (solve == "power")
        {
            power = PowerAt(p1, n);
        }
        else
        {
            var za = SpecialFunctions.NormalInv(1 - (alternative == "two-sided" ? alpha / 2 : alpha));
            var zp = SpecialFunctions.NormalInv(power);
            var effect = (za + zp) / Math.Sqrt(n * ratio / (1 + ratio));
            var direction = alternative == "smaller" ? -1 : 1;
            var angle = Math.Asin(Math.Sqrt(p0)) + (direction == 1 || alternative == "two-sided" ? effect / 2 : -effect / 2);
            if (angle <= 0 || angle >= Math.PI / 2)
            {
                return ExtendedHelpers.Error(DomainErrorCode.NotSupported,
                    "Con ese tamaño de muestra no se puede detectar ninguna diferencia (la proporción saldría fuera de [0, 1]).");
            }
            p1 = Math.Pow(Math.Sin(angle), 2);
        }
        var shownN = solve == "n" ? Math.Ceiling(n) : n;
        var actual = solve == "n" ? PowerAt(p1, shownN) : power;
        var p0Value = p0;
        var p1Value = p1;

        return Report(
            "power-prop2",
            "Potencia y tamaño de muestra: dos proporciones",
            $"α = {SigmaText.General(alpha)} · {AlternativeLabel(alternative, correlation: false)}",
            "Tamaño de muestra por grupo",
            "Proporción alternativa",
            "observaciones por grupo",
            solve, shownN, actual, p1Value, alpha,
            [
                ["Proporción de referencia", p0Value],
                ["Diferencia", p1Value - p0Value],
                ["Efecto h de Cohen", H(p1Value)]
            ],
            "Usa la aproximación normal con la transformación arcoseno (h de Cohen).",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = shownN,
                ["power"] = actual,
                ["p1"] = p1Value,
                ["p0"] = p0Value
            },
            chartCurves: 1,
            unitLabel: "observaciones por grupo");
    }

    private static Result<AnalysisResult> Report(
        string id,
        string title,
        string subtitle,
        string nLabel,
        string effectLabel,
        string unit,
        string solve,
        double shownN,
        double power,
        double effect,
        double alpha,
        IReadOnlyList<IReadOnlyList<object?>> extra,
        string note,
        IReadOnlyDictionary<string, object?> keyResults,
        int chartCurves,
        string? unitLabel = null)
    {
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem(nLabel, JsNumber.ToString(shownN)),
                new TileItem("Potencia", ExtendedHelpers.N(power)),
                new TileItem(effectLabel, ExtendedHelpers.N(effect)),
                new TileItem("α", ExtendedHelpers.N(alpha))
            ])
        };
        if (solve == "n")
        {
            sections.Add(new AlertSection(
                "Aviso",
                "info",
                $"Se necesitan {SigmaText.General(shownN)} {unitLabel ?? unit} para alcanzar una potencia de {SigmaText.Fixed4(power)}."));
        }

        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { nLabel, shownN },
            new object?[] { "Potencia", power },
            new object?[] { effectLabel, effect },
            new object?[] { "α", alpha }
        };
        rows.AddRange(extra);
        sections.Add(new TableSection(
            "Parámetros",
            ["Parámetro", "Valor"],
            rows.Select(row => (IReadOnlyList<string>)row.Select(FormatValue).ToArray()).ToArray(),
            Note: note));

        var series = new List<Charts.LineSeries>();
        for (var index = 0; index < chartCurves; index++)
        {
            series.Add(new Charts.LineSeries($"Curva {index + 1}", [power]));
        }

        return Result.Ok(new AnalysisResult(
            id,
            title,
            sections,
            Subtitle: subtitle,
            Charts: [new Charts.LineChart("Curvas de potencia", series, nLabel)],
            KeyResults: keyResults,
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static string FormatValue(object? value) =>
        value switch
        {
            null => string.Empty,
            string text => text,
            double number => ExtendedHelpers.N(number),
            int number => JsNumber.ToString(number),
            _ => value.ToString() ?? string.Empty
        };

    private static string AlternativeLabel(string alternative, bool correlation) =>
        alternative switch
        {
            "larger" => correlation ? "Unilateral" : "Cola derecha (>)",
            "smaller" => "Cola izquierda (<)",
            _ => correlation ? "Bilateral (≠)" : "Bilateral (≠)"
        };

    private static double TostPower(double difference, double margin, double sd, double n, double alpha)
    {
        var df = 2 * n - 2;
        var a = Math.Sqrt(2 / n);
        var critical = InferenceDistributions.TQuantile(1 - alpha, df);

        double Integrand(double s)
        {
            var sampleSd = sd * s;
            var upper = (margin - critical * sampleSd * a - difference) / (sd * a);
            var lower = (-margin + critical * sampleSd * a - difference) / (sd * a);
            var inside = Math.Max(0, SpecialFunctions.NormalCdf(upper) - SpecialFunctions.NormalCdf(lower));
            return inside * ChiSquareDensityInS(s, df);
        }

        return Math.Clamp(Integrate(Integrand, 0, 8), 0, 1);
    }

    private static double ChiSquarePdf(double x, double df)
    {
        if (x <= 0) return 0;
        return Math.Exp((df / 2 - 1) * Math.Log(x) - x / 2 - (df / 2) * Math.Log(2) - SpecialFunctions.LogGamma(df / 2));
    }

    /// <summary>Densidad de χ²(df) tras el cambio u = df·s² (integrando sin singularidad en 0).</summary>
    private static double ChiSquareDensityInS(double s, double df)
    {
        if (s <= 0) return 0;
        return ChiSquarePdf(df * s * s, df) * 2 * df * s;
    }

    private static double NoncentralTCdf(double t, double df, double ncp)
    {
        double Integrand(double s) => SpecialFunctions.NormalCdf(t * s - ncp) * ChiSquareDensityInS(s, df);
        return Math.Clamp(Integrate(Integrand, 0, 8), 0, 1);
    }

    /// <summary>Simpson adaptativo por paneles: suficiente para las densidades suaves de estos diseños.</summary>
    private static double Integrate(Func<double, double> function, double lower, double upper)
    {
        const int panels = 128;
        var width = (upper - lower) / panels;
        var total = 0.0;
        for (var panel = 0; panel < panels; panel++)
        {
            var a = lower + panel * width;
            var b = a + width;
            var middle = (a + b) / 2;
            var fa = function(a);
            var fm = function(middle);
            var fb = function(b);
            var whole = (b - a) / 6 * (fa + 4 * fm + fb);
            total += IntegrateRecursive(function, a, b, fa, fm, fb, whole, 1e-11, 20);
        }
        return total;
    }

    private static double IntegrateRecursive(
        Func<double, double> function,
        double lower,
        double upper,
        double fa,
        double fm,
        double fb,
        double whole,
        double epsilon,
        int depth)
    {
        var middle = (lower + upper) / 2;
        var leftMiddle = (lower + middle) / 2;
        var rightMiddle = (middle + upper) / 2;
        var flm = function(leftMiddle);
        var frm = function(rightMiddle);
        var left = (middle - lower) / 6 * (fa + 4 * flm + fm);
        var right = (upper - middle) / 6 * (fm + 4 * frm + fb);
        var delta = left + right - whole;
        if (depth <= 0 || Math.Abs(delta) < 15 * epsilon)
        {
            return left + right + delta / 15;
        }
        return IntegrateRecursive(function, lower, middle, fa, flm, fm, left, epsilon / 2, depth - 1)
               + IntegrateRecursive(function, middle, upper, fm, frm, fb, right, epsilon / 2, depth - 1);
    }

    private static double Bisect(double low, double high, Func<double, double> value, double target)
    {
        var fLow = value(low);
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var middle = (low + high) / 2;
            var current = value(middle);
            if (Math.Abs(current - target) < 1e-10) return middle;
            if ((current < target) == (fLow < target))
            {
                low = middle;
                fLow = current;
            }
            else
            {
                high = middle;
            }
            if (high - low < 1e-12 * Math.Max(1, high)) break;
        }
        return (low + high) / 2;
    }

    private static double Bracket(Func<double, double> powerOf, double target, double low, double high)
    {
        if (powerOf(high) < target) return double.NaN;
        if (powerOf(low) >= target) return low;
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var middle = (low + high) / 2;
            if (powerOf(middle) < target) low = middle;
            else high = middle;
            if (high - low < 1e-10 * Math.Max(1, high)) break;
        }
        return (low + high) / 2;
    }
}
