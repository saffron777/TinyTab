using System.Globalization;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Medias multivariadas, correlación parcial e intervalos de confianza de una variable.</summary>
internal static class EstimationAnalyses
{
    private const string Category = "Pruebas adicionales";

    private static readonly AnalysisParamDefinition Alpha = ExtendedHelpers.Alpha();

    private static readonly AnalysisDefinition Hotelling = new(
        "hotelling-t2",
        "T² de Hotelling (vectores de medias)",
        Category,
        "Compara varias medias a la vez: un vector con una referencia, o dos grupos entre sí.",
        [],
        [],
        [
            new(
                "design",
                "Diseño",
                AnalysisParamKinds.Select,
                "two",
                [new("one", "Una muestra frente a un vector"), new("two", "Dos muestras independientes")],
                false),
            new(
                "mu0",
                "Vector de referencia (separado por comas; vacío = ceros)",
                AnalysisParamKinds.Text,
                null,
                [],
                true,
                VisibleIf: new("design", ["one"])),
            Alpha
        ],
        RunHotelling,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Equivalencia y multivariadas",
        Roles:
        [
            new ColumnRole("vars", "Variables (2 o más)", ColumnKinds.Numeric, Multiple: true, MinCount: 2),
            new ColumnRole("group", "Grupos (2 muestras)", ColumnKinds.Categorical, Optional: true)
        ]);

    private static readonly AnalysisDefinition Partial = new(
        "partial-correlation",
        "Correlación parcial",
        Category,
        "Correlación entre dos variables quitando el efecto de una o más covariables (Pearson o Spearman).",
        [],
        [],
        [
            new(
                "method",
                "Coeficiente",
                AnalysisParamKinds.Select,
                "pearson",
                [new("pearson", "Pearson"), new("spearman", "Spearman")],
                false),
            Alpha
        ],
        RunPartial,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Equivalencia y multivariadas",
        Roles:
        [
            new ColumnRole("x", "Variable X", ColumnKinds.Numeric),
            new ColumnRole("y", "Variable Y", ColumnKinds.Numeric),
            new ColumnRole("covars", "Covariables a controlar", ColumnKinds.Numeric, Multiple: true)
        ]);

    private static readonly AnalysisDefinition ConfidenceInterval = new(
        "confidence-interval",
        "Intervalos de confianza de una variable",
        Category,
        "Media (t), desviación estándar y varianza (χ²) y mediana (signos) con el nivel de confianza que elija.",
        [],
        [],
        [
            new(
                "confidence",
                "Nivel de confianza (%)",
                AnalysisParamKinds.Number,
                95.0,
                [],
                false,
                Minimum: 50,
                Maximum: 99.9)
        ],
        RunConfidenceInterval,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Estimación",
        Roles: [new ColumnRole("var", "Variable", ColumnKinds.Numeric)]);

    private static readonly Lazy<IReadOnlyList<AnalysisDefinition>> LazyAll =
        new(() => [Hotelling, Partial, ConfidenceInterval], isThreadSafe: true);

    public static IReadOnlyList<AnalysisDefinition> All => LazyAll.Value;

    private static Result<AnalysisResult> RunHotelling(AnalysisInput input)
    {
        var frame = ExtendedHelpers.FrameOf(input);
        var names = input.RoleNames("vars", true);
        if (names.Count < 2) return ExtendedHelpers.Invalid("Se necesitan al menos 2 variables.");
        var missing = names.Where(name => !frame.Has(name)).ToArray();
        if (missing.Length > 0) return ExtendedHelpers.Invalid($"La columna «{missing[0]}» no existe en el dataset.");
        var alpha = input.Pnum("alpha", 0.05);
        var design = input.Pstr("design", "two");
        var p = names.Count;

        if (design == "one")
        {
            var complete = frame.CompleteCases(names);
            var n = complete.RowCount;
            if (n <= p) return ExtendedHelpers.Invalid($"Se necesitan más de {p} filas completas.");
            var data = Rows(complete, names);
            var means = Means(data, p);
            var covariance = Covariance(data, p);
            var reference = ParseVector(input.Pstr("mu0", string.Empty), p);
            if (reference is null) return ExtendedHelpers.Invalid("El vector de referencia debe ser números separados por comas.");
            if (reference.Length != p)
            {
                return ExtendedHelpers.Invalid($"El vector de referencia necesita {p} valores (uno por variable).");
            }
            var diff = new double[p];
            for (var index = 0; index < p; index++) diff[index] = means[index] - reference[index];
            var solved = LinearAlgebra.SolveLinear(covariance, diff);
            if (solved.IsError) return Result.Err<AnalysisResult>(solved.Error.Code, solved.Error.Message);
            var t2 = n * Dot(diff, solved.Value);
            var df1 = (double)p;
            var df2 = n - p;
            var f = t2 * (n - p) / (p * (n - 1.0));
            var pValue = ExtendedStatistics.FSf(f, df1, df2);
            var rows = new List<IReadOnlyList<string>>();
            for (var index = 0; index < p; index++)
            {
                rows.Add([names[index], ExtendedHelpers.N(means[index]), ExtendedHelpers.N(reference[index])]);
            }
            return BuildHotelling(
                "hotelling-t2",
                "T² de Hotelling (una muestra)",
                $"{n} filas · vector de referencia {PythonList(reference)}",
                "Variable", "Media", "Referencia",
                rows, t2, f, df1, df2, pValue, alpha, names);
        }

        var group = ExtendedHelpers.FirstRole(input, "group", "Grupos (2 muestras)");
        var withGroup = frame.CompleteCases([.. names, group]);
        var levels = withGroup.Levels(group);
        if (levels.Length != 2)
        {
            return ExtendedHelpers.Invalid($"«{group}» debe tener exactamente 2 categorías (tiene {levels.Length}).");
        }
        var groupTexts = withGroup.Texts(group);
        var rowsA = new List<double[]>();
        var rowsB = new List<double[]>();
        for (var row = 0; row < withGroup.RowCount; row++)
        {
            var values = new double[p];
            for (var column = 0; column < p; column++) values[column] = withGroup.Numeric(names[column])[row];
            if (groupTexts[row] == levels[0]) rowsA.Add(values);
            else rowsB.Add(values);
        }
        var n1 = rowsA.Count;
        var n2 = rowsB.Count;
        if (n1 + n2 - 2 <= p) return ExtendedHelpers.Invalid($"Se necesitan más de {p + 2} filas en total.");
        var matrixA = rowsA.ToArray();
        var matrixB = rowsB.ToArray();
        var meansA = Means(matrixA, p);
        var meansB = Means(matrixB, p);
        var covA = Covariance(matrixA, p);
        var covB = Covariance(matrixB, p);
        var pooled = new double[p][];
        for (var i = 0; i < p; i++)
        {
            pooled[i] = new double[p];
            for (var j = 0; j < p; j++)
            {
                pooled[i][j] = ((n1 - 1) * covA[i][j] + (n2 - 1) * covB[i][j]) / (n1 + n2 - 2.0);
            }
        }
        var diff2 = new double[p];
        for (var index = 0; index < p; index++) diff2[index] = meansA[index] - meansB[index];
        var solved2 = LinearAlgebra.SolveLinear(pooled, diff2);
        if (solved2.IsError) return Result.Err<AnalysisResult>(solved2.Error.Code, solved2.Error.Message);
        var t22 = (double)n1 * n2 / (n1 + n2) * Dot(diff2, solved2.Value);
        var df12 = (double)p;
        var df22 = n1 + n2 - p - 1.0;
        var f2 = t22 * df22 / (p * (n1 + n2 - 2.0));
        var pValue2 = ExtendedStatistics.FSf(f2, df12, df22);
        var rows2 = new List<IReadOnlyList<string>>();
        for (var index = 0; index < p; index++)
        {
            rows2.Add([names[index], ExtendedHelpers.N(meansA[index]), ExtendedHelpers.N(meansB[index])]);
        }
        return BuildHotelling(
            "hotelling-t2",
            "T² de Hotelling (dos muestras)",
            $"{levels[0]} (n = {n1}) frente a {levels[1]} (n = {n2})",
            "Variable", $"Media {levels[0]}", $"Media {levels[1]}",
            rows2, t22, f2, df12, df22, pValue2, alpha, names);
    }

    private static Result<AnalysisResult> BuildHotelling(
        string id,
        string title,
        string subtitle,
        string firstHeader,
        string secondHeader,
        string thirdHeader,
        IReadOnlyList<IReadOnlyList<string>> rows,
        double t2,
        double f,
        double df1,
        double df2,
        double pValue,
        double alpha,
        IReadOnlyList<string> names)
    {
        return Result.Ok(new AnalysisResult(
            id,
            title,
            [
                new TilesSection("Resumen",
                [
                    new TileItem("T²", ExtendedHelpers.N(t2)),
                    new TileItem("F", ExtendedHelpers.N(f)),
                    new TileItem("gl", $"{JsNumber.ToString(df1)}, {JsNumber.ToString(df2)}"),
                    new TileItem("Valor p", ExtendedHelpers.P(pValue))
                ]),
                SigmaText.Decision(pValue, alpha, "los vectores de medias son iguales"),
                new TableSection("Medias", [firstHeader, secondHeader, thirdHeader], rows)
            ],
            Subtitle: subtitle,
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["t2"] = t2,
                ["f"] = f,
                ["p"] = pValue
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunPartial(AnalysisInput input)
    {
        var frame = ExtendedHelpers.FrameOf(input);
        var xName = ExtendedHelpers.FirstRole(input, "x", "Variable X");
        var yName = ExtendedHelpers.FirstRole(input, "y", "Variable Y");
        var covars = input.RoleNames("covars", true);
        if (covars.Count == 0) return ExtendedHelpers.Invalid("Elija al menos una covariable.");
        var names = new List<string> { xName, yName };
        names.AddRange(covars);
        var complete = frame.CompleteCases(names);
        var n = complete.RowCount;
        var k = covars.Count;
        if (n <= k + 3) return ExtendedHelpers.Invalid($"Se necesitan más de {k + 3} filas completas.");
        var method = input.Pstr("method", "pearson");
        var alpha = input.Pnum("alpha", 0.05);

        var work = new double[n][];
        for (var row = 0; row < n; row++)
        {
            work[row] = new double[names.Count];
            for (var column = 0; column < names.Count; column++) work[row][column] = complete.Numeric(names[column])[row];
        }
        if (method == "spearman")
        {
            for (var column = 0; column < names.Count; column++)
            {
                var ranks = NumericMath.Ranks(work.Select(row => row[column]).ToArray());
                for (var row = 0; row < n; row++) work[row][column] = ranks[row];
            }
        }

        var xIndex = 0;
        var yIndex = 1;
        var rx = Residuals(work, n, xIndex, k);
        var ry = Residuals(work, n, yIndex, k);
        if (rx.Max() - rx.Min() == 0 || ry.Max() - ry.Min() == 0)
        {
            return ExtendedHelpers.Invalid("Una de las variables queda sin variación al quitar el efecto de las covariables.");
        }
        var r = NumericMath.Correlation(rx, ry);
        var df = n - 2.0 - k;
        var t = r * Math.Sqrt(df / (1 - r * r));
        var pValue = 2 * (1 - InferenceDistributions.TCdf(Math.Abs(t), df));
        var z = Math.Atanh(r);
        var se = 1 / Math.Sqrt(n - 3.0 - k);
        var critical = SpecialFunctions.NormalInv(1 - alpha / 2);
        var low = Math.Tanh(z - critical * se);
        var high = Math.Tanh(z + critical * se);
        var simple = NumericMath.Correlation(work.Select(row => row[xIndex]).ToArray(), work.Select(row => row[yIndex]).ToArray());

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Correlación parcial", ExtendedHelpers.N(r)),
                new TileItem("Correlación simple", ExtendedHelpers.N(simple)),
                new TileItem("Valor p", ExtendedHelpers.P(pValue)),
                new TileItem("n", JsNumber.ToString(n))
            ]),
            SigmaText.Decision(pValue, alpha, "la correlación parcial es 0"),
            new TableSection(
                "Resultado",
                ["Correlación parcial", "gl", "t", "Valor p", $"IC {SigmaText.General(100 * (1 - alpha))} % inf.", "IC sup."],
                [[
                    ExtendedHelpers.N(r),
                    JsNumber.ToString(df),
                    ExtendedHelpers.N(t),
                    ExtendedHelpers.P(pValue),
                    ExtendedHelpers.N(low),
                    ExtendedHelpers.N(high)
                ]],
                Note: "El intervalo usa la transformación z de Fisher con n − 3 − (covariables) grados de libertad.")
        };
        if (Math.Abs(simple) > Math.Abs(r) + 0.1)
        {
            sections.Add(new AlertSection(
                "Aviso",
                "info",
                "La correlación baja al controlar por las covariables: parte de la relación simple se debe a ellas."));
        }

        return Result.Ok(new AnalysisResult(
            "partial-correlation",
            $"Correlación parcial de {xName} y {yName}",
            sections,
            Subtitle: $"controlando por {string.Join(", ", covars)} · {(method == "spearman" ? "Spearman" : "Pearson")}",
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["partial"] = r,
                ["simple"] = simple,
                ["p"] = pValue,
                ["n"] = n
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunConfidenceInterval(AnalysisInput input)
    {
        var frame = ExtendedHelpers.FrameOf(input);
        var name = ExtendedHelpers.FirstRole(input, "var", "Variable");
        var values = ExtendedHelpers.Complete(frame.Numeric(name));
        if (values.Length < 5) return ExtendedHelpers.Invalid(SigmaText.NeedMessage(name, 5));

        var level = input.Pnum("confidence", 95) / 100;
        var alpha = 1 - level;
        var n = values.Length;
        var mean = NumericMath.Mean(values);
        var sd = NumericMath.Sd(values);
        var half = InferenceDistributions.TQuantile(1 - alpha / 2, n - 1) * sd / Math.Sqrt(n);
        var sdLow = sd * Math.Sqrt((n - 1) / ExtendedStatistics.ChiSquareInv(1 - alpha / 2, n - 1));
        var sdHigh = sd * Math.Sqrt((n - 1) / ExtendedStatistics.ChiSquareInv(alpha / 2, n - 1));

        var ordered = NumericMath.Sorted(values);
        var k = 0;
        while (k + 1 <= n / 2 && 2 * ExtendedStatistics.BinomialCdf(k, n, 0.5) <= alpha) k++;
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "Media (t de Student)", ExtendedHelpers.N(mean), ExtendedHelpers.N(mean - half), ExtendedHelpers.N(mean + half) },
            new[] { "Desviación estándar (χ²)", ExtendedHelpers.N(sd), ExtendedHelpers.N(sdLow), ExtendedHelpers.N(sdHigh) },
            new[] { "Varianza (χ²)", ExtendedHelpers.N(sd * sd), ExtendedHelpers.N(sdLow * sdLow), ExtendedHelpers.N(sdHigh * sdHigh) }
        };
        if (k >= 1)
        {
            var confidence = 100 * (1 - 2 * ExtendedStatistics.BinomialCdf(k - 1, n, 0.5));
            rows.Add([
                $"Mediana (signos, confianza {confidence.ToString("F1", CultureInfo.InvariantCulture)} %)",
                ExtendedHelpers.N(NumericMath.Median(values)),
                ExtendedHelpers.N(ordered[k - 1]),
                ExtendedHelpers.N(ordered[n - k])
            ]);
        }

        return Result.Ok(new AnalysisResult(
            "confidence-interval",
            $"Intervalos de confianza de {name}",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Media", ExtendedHelpers.N(mean)),
                    new TileItem("Mediana", ExtendedHelpers.N(NumericMath.Median(values))),
                    new TileItem("Desv. estándar", ExtendedHelpers.N(sd))
                ]),
                new TableSection(
                    "Intervalos de confianza",
                    ["Parámetro", "Estimación", "Límite inf.", "Límite sup."],
                    rows,
                    Note: "La media y las desviaciones suponen datos aproximadamente normales; el de la mediana no.")
            ],
            Subtitle: $"nivel {SigmaText.General(input.Pnum("confidence", 95))} % · n = {n}",
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["mean"] = mean,
                ["sd"] = sd,
                ["mean_ci"] = new[] { mean - half, mean + half }
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static double[] Residuals(double[][] work, int n, int target, int k)
    {
        var design = new double[n][];
        for (var row = 0; row < n; row++)
        {
            design[row] = new double[k];
            for (var column = 0; column < k; column++) design[row][column] = work[row][column + 2];
        }
        var y = new double[n];
        for (var row = 0; row < n; row++) y[row] = work[row][target];
        var fit = LinearAlgebra.LeastSquares(design, y);
        if (fit.IsError) throw new InvalidOperationException(fit.Error.Message);
        var residuals = new double[n];
        for (var row = 0; row < n; row++) residuals[row] = y[row] - fit.Value.Fitted[row];
        return residuals;
    }

    private static double[][] Rows(Frame frame, IReadOnlyList<string> names)
    {
        var rows = new double[frame.RowCount][];
        for (var row = 0; row < frame.RowCount; row++)
        {
            rows[row] = new double[names.Count];
            for (var column = 0; column < names.Count; column++) rows[row][column] = frame.Numeric(names[column])[row];
        }
        return rows;
    }

    private static double[] Means(double[][] rows, int p)
    {
        var means = new double[p];
        foreach (var row in rows)
        {
            for (var column = 0; column < p; column++) means[column] += row[column];
        }
        for (var column = 0; column < p; column++) means[column] /= rows.Length;
        return means;
    }

    private static double[][] Covariance(double[][] rows, int p)
    {
        var means = Means(rows, p);
        var matrix = new double[p][];
        for (var i = 0; i < p; i++) matrix[i] = new double[p];
        foreach (var row in rows)
        {
            for (var i = 0; i < p; i++)
            {
                for (var j = 0; j < p; j++) matrix[i][j] += (row[i] - means[i]) * (row[j] - means[j]);
            }
        }
        for (var i = 0; i < p; i++)
        {
            for (var j = 0; j < p; j++) matrix[i][j] /= rows.Length - 1;
        }
        return matrix;
    }

    private static double Dot(double[] left, double[] right)
    {
        var sum = 0.0;
        for (var index = 0; index < left.Length; index++) sum += left[index] * right[index];
        return sum;
    }

    private static double[]? ParseVector(string text, int p)
    {
        if (string.IsNullOrWhiteSpace(text)) return new double[p];
        var parts = text.Replace(';', ',').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var values = new double[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out values[index])) return null;
        }
        return values;
    }

    private static string PythonList(IReadOnlyList<double> values)
    {
        var formatted = values.Select(value =>
        {
            var text = value.ToString("R", CultureInfo.InvariantCulture);
            return text.Contains('.') || text.Contains('E') || text.Contains('e') ? text : text + ".0";
        });
        return "[" + string.Join(", ", formatted) + "]";
    }
}
