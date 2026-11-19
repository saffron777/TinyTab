using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Acuerdo entre evaluadores: kappa de Cohen/Fleiss e ICC de Shrout-Fleiss.</summary>
internal static class AgreementAnalyses
{
    private const string Category = "Pruebas adicionales";

    private static readonly AnalysisParamDefinition Alpha = ExtendedHelpers.Alpha();

    private static readonly AnalysisDefinition Kappa = new(
        "kappa",
        "Kappa de Cohen y de Fleiss (acuerdo)",
        Category,
        "Acuerdo entre 2 evaluadores (Cohen, simple o ponderado) o más (Fleiss), corregido por el azar.",
        [],
        [],
        [
            new(
                "weights",
                "Ponderación (2 evaluadores, categorías ordenadas)",
                AnalysisParamKinds.Select,
                "none",
                [
                    new("none", "Sin ponderar"),
                    new("linear", "Lineal"),
                    new("quadratic", "Cuadrática")
                ],
                false)
        ],
        RunKappa,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Acuerdo entre evaluadores",
        Roles:
        [
            new ColumnRole("raters", "Evaluadores (una columna cada uno)", ColumnKinds.Any, Multiple: true, MinCount: 2)
        ]);

    private static readonly AnalysisDefinition Icc = new(
        "icc",
        "Correlación intraclase (CCI)",
        Category,
        "Fiabilidad de mediciones continuas de varios evaluadores: las seis formas de Shrout y Fleiss con IC y pruebas F.",
        [],
        [],
        [Alpha],
        RunIcc,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Acuerdo entre evaluadores",
        Roles:
        [
            new ColumnRole("raters", "Evaluadores (una columna cada uno; una fila por sujeto)", ColumnKinds.Numeric, Multiple: true, MinCount: 2)
        ]);

    private static readonly Lazy<IReadOnlyList<AnalysisDefinition>> LazyAll =
        new(() => [Kappa, Icc], isThreadSafe: true);

    public static IReadOnlyList<AnalysisDefinition> All => LazyAll.Value;

    private static Result<AnalysisResult> RunKappa(AnalysisInput input)
    {
        var frame = ExtendedHelpers.FrameOf(input);
        var names = input.RoleNames("raters", true);
        if (names.Count < 2) return ExtendedHelpers.Invalid("Se necesitan al menos 2 evaluadores.");
        var complete = frame.CompleteCases(names);
        if (complete.RowCount < 5) return ExtendedHelpers.Invalid("Se necesitan al menos 5 sujetos calificados por todos los evaluadores.");

        var labels = KappaLabels(complete, names);
        if (labels.Count < 2 || labels.Count > 30)
        {
            return ExtendedHelpers.Invalid("Las calificaciones deben tener entre 2 y 30 categorías distintas.");
        }
        var labelIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < labels.Count; index++) labelIndex[labels[index]] = index;

        var n = complete.RowCount;
        var coded = new int[n][];
        var texts = names.Select(complete.Texts).ToArray();
        for (var row = 0; row < n; row++)
        {
            coded[row] = new int[names.Count];
            for (var column = 0; column < names.Count; column++)
            {
                coded[row][column] = labelIndex[texts[column][row] ?? string.Empty];
            }
        }

        var agreement = coded.Average(row => row.Distinct().Count() == 1 ? 1.0 : 0.0);
        var output = new List<AnalysisSection>();
        var title = "Acuerdo entre evaluadores (kappa)";
        var subtitle = $"{names.Count} evaluadores · {n} sujetos · {labels.Count} categorías";
        var resultRows = new List<IReadOnlyList<string>>();
        double kappaResult;

        if (names.Count == 2)
        {
            var table = new double[labels.Count, labels.Count];
            foreach (var row in coded) table[row[0], row[1]]++;
            var weights = input.Pstr("weights", "none");
            var (kappa, low, high, pvalue) = CohenKappa(table, weights);
            kappaResult = kappa;
            output.Add(new TilesSection("Resumen",
            [
                new TileItem("Kappa", ExtendedHelpers.N(kappa)),
                new TileItem("IC 95 %", $"{low.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} a {high.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}"),
                new TileItem("Acuerdo observado", ExtendedHelpers.N(agreement)),
                new TileItem("Valor p", ExtendedHelpers.P(pvalue))
            ]));

            var rows = new List<IReadOnlyList<string>>();
            for (var row = 0; row < labels.Count; row++)
            {
                var cells = new string[labels.Count + 1];
                cells[0] = labels[row];
                for (var column = 0; column < labels.Count; column++) cells[column + 1] = JsNumber.ToString(table[row, column]);
                rows.Add(cells);
            }
            output.Add(new TableSection(
                "Tabla de calificaciones",
                [$"{names[0]} \\ {names[1]}", .. labels],
                rows));
            if (weights != "none")
            {
                output.Add(new AlertSection("Aviso", "info", "El kappa ponderado supone categorías ordinales, en el orden en que aparecen arriba."));
            }
            var measure = weights == "none" ? "Kappa de Cohen" : $"Kappa de Cohen ponderado ({weights})";
            resultRows.Add([measure, ExtendedHelpers.N(kappa), LandisKoch(kappa)]);
        }
        else
        {
            var counts = new double[n, labels.Count];
            for (var row = 0; row < n; row++)
            {
                foreach (var category in coded[row]) counts[row, category]++;
            }
            var subjects = new double[n];
            for (var row = 0; row < n; row++)
            {
                var sum = 0.0;
                for (var column = 0; column < labels.Count; column++) sum += counts[row, column] * counts[row, column];
                subjects[row] = (sum - names.Count) / (names.Count * (names.Count - 1.0));
            }
            var pBar = subjects.Average();
            var pj = new double[labels.Count];
            for (var column = 0; column < labels.Count; column++)
            {
                for (var row = 0; row < n; row++) pj[column] += counts[row, column];
                pj[column] /= n * names.Count;
            }
            var pExp = pj.Sum(value => value * value);
            kappaResult = (pBar - pExp) / (1 - pExp);
            output.Add(new TilesSection("Resumen",
            [
                new TileItem("Kappa", ExtendedHelpers.N(kappaResult)),
                new TileItem("Acuerdo total", ExtendedHelpers.N(agreement))
            ]));
            var rows = new List<IReadOnlyList<string>>();
            for (var column = 0; column < labels.Count; column++)
            {
                var total = 0.0;
                for (var row = 0; row < n; row++) total += counts[row, column];
                rows.Add([labels[column], JsNumber.ToString(total), ExtendedHelpers.N(100 * total / (n * names.Count))]);
            }
            output.Add(new TableSection("Distribución de calificaciones", ["Categoría", "Calificaciones", "%"], rows));
            resultRows.Add(["Kappa de Fleiss", ExtendedHelpers.N(kappaResult), LandisKoch(kappaResult)]);
        }

        output.Add(new TableSection("Resultado", ["Medida", "Valor", "Interpretación (Landis y Koch)"], resultRows));
        return Result.Ok(new AnalysisResult(
            "kappa",
            title,
            output,
            Subtitle: subtitle,
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["kappa"] = kappaResult,
                ["agreement"] = agreement,
                ["n"] = n
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunIcc(AnalysisInput input)
    {
        var frame = ExtendedHelpers.FrameOf(input);
        var names = input.RoleNames("raters", true);
        if (names.Count < 2) return ExtendedHelpers.Invalid("Se necesitan al menos 2 evaluadores.");
        var complete = frame.CompleteCases(names);
        if (complete.RowCount < 3) return ExtendedHelpers.Invalid("Se necesitan al menos 3 sujetos con todas las mediciones.");

        var n = complete.RowCount;
        var k = names.Count;
        var data = new double[n, k];
        for (var row = 0; row < n; row++)
        {
            for (var column = 0; column < k; column++) data[row, column] = complete.Numeric(names[column])[row];
        }
        var alpha = input.Pnum("alpha", 0.05);
        var results = IccTable(data, alpha);

        var rows = new List<IReadOnlyList<string>>();
        var ci = $"IC {SigmaText.General(100 * (1 - alpha))} %";
        foreach (var item in results)
        {
            rows.Add([
                item.Label,
                IccModel(item.Label),
                ExtendedHelpers.N(item.Icc),
                ExtendedHelpers.N(item.F),
                ExtendedHelpers.N(item.Df1),
                ExtendedHelpers.N(item.Df2),
                ExtendedHelpers.P(item.P),
                ExtendedHelpers.N(item.Low),
                ExtendedHelpers.N(item.High)
            ]);
        }

        return Result.Ok(new AnalysisResult(
            "icc",
            "Coeficiente de correlación intraclase",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("ICC(2,1)", ExtendedHelpers.N(results[1].Icc)),
                    new TileItem("ICC(3,1)", ExtendedHelpers.N(results[2].Icc)),
                    new TileItem("ICC(2,k)", ExtendedHelpers.N(results[4].Icc))
                ]),
                new TableSection(
                    "Las seis formas del ICC",
                    ["Forma", "Modelo", "ICC", "F", "gl1", "gl2", "Valor p", $"{ci} inf.", $"{ci} sup."],
                    rows,
                    Note: "Elija según el diseño: ICC(2,·) si los evaluadores son una muestra de una población de evaluadores, ICC(3,·) si son los únicos de interés. Menos de 0.5 pobre, 0.5–0.75 moderada, 0.75–0.9 buena, más de 0.9 excelente (Koo y Li, 2016).")
            ],
            Subtitle: $"{k} evaluadores · {n} sujetos",
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["icc"] = results.ToDictionary(item => item.Label, item => (object?)item.Icc, StringComparer.Ordinal)
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private sealed record IccRow(string Label, double Icc, double F, double Df1, double Df2, double P, double Low, double High);

    private static List<IccRow> IccTable(double[,] data, double alpha)
    {
        var n = data.GetLength(0);
        var k = data.GetLength(1);
        var grand = 0.0;
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < k; j++) grand += data[i, j];
        }
        grand /= n * k;

        var rowMeans = new double[n];
        var colMeans = new double[k];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < k; j++) rowMeans[i] += data[i, j];
            rowMeans[i] /= k;
        }
        for (var j = 0; j < k; j++)
        {
            for (var i = 0; i < n; i++) colMeans[j] += data[i, j];
            colMeans[j] /= n;
        }

        var ssRows = k * rowMeans.Sum(value => Math.Pow(value - grand, 2));
        var ssCols = n * colMeans.Sum(value => Math.Pow(value - grand, 2));
        var ssTotal = 0.0;
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < k; j++) ssTotal += Math.Pow(data[i, j] - grand, 2);
        }
        var ssError = ssTotal - ssRows - ssCols;
        var msr = ssRows / (n - 1);
        var msc = ssCols / (k - 1);
        var mse = ssError / ((n - 1.0) * (k - 1));
        var msw = (ssCols + ssError) / (n * (k - 1.0));
        var fLow = 1 - alpha / 2;

        var results = new List<IccRow>();
        var icc1 = (msr - msw) / (msr + (k - 1) * msw);
        var f1 = msr / msw;
        var fl = f1 / ExtendedStatistics.FInv(fLow, n - 1, n * (k - 1.0));
        var fu = f1 * ExtendedStatistics.FInv(fLow, n * (k - 1.0), n - 1);
        results.Add(new("ICC(1,1)", icc1, f1, n - 1, n * (k - 1.0), ExtendedStatistics.FSf(f1, n - 1, n * (k - 1.0)), (fl - 1) / (fl + k - 1), (fu - 1) / (fu + k - 1)));

        var icc2 = (msr - mse) / (msr + (k - 1) * mse + k * (msc - mse) / n);
        var f2 = msr / mse;
        var fj = msc / mse;
        var a = k * icc2 * fj + n * (1 + (k - 1) * icc2) - k * icc2;
        var vn = (k - 1.0) * (n - 1) * a * a / ((n - 1) * k * k * icc2 * icc2 * fj * fj + Math.Pow(n * (1 + (k - 1) * icc2) - k * icc2, 2));
        var fl2 = ExtendedStatistics.FInv(fLow, n - 1, vn);
        var fu2 = ExtendedStatistics.FInv(fLow, vn, n - 1);
        var denom = k * msc + (k * n - k - n) * mse;
        var low2 = n * (msr - fl2 * mse) / (fl2 * denom + n * msr);
        var high2 = n * (fu2 * msr - mse) / (denom + n * fu2 * msr);
        results.Add(new("ICC(2,1)", icc2, f2, n - 1, (n - 1.0) * (k - 1), ExtendedStatistics.FSf(f2, n - 1, (n - 1.0) * (k - 1)), low2, high2));

        var icc3 = (msr - mse) / (msr + (k - 1) * mse);
        var fl3 = f2 / ExtendedStatistics.FInv(fLow, n - 1, (n - 1.0) * (k - 1));
        var fu3 = f2 * ExtendedStatistics.FInv(fLow, (n - 1.0) * (k - 1), n - 1);
        results.Add(new("ICC(3,1)", icc3, f2, n - 1, (n - 1.0) * (k - 1), ExtendedStatistics.FSf(f2, n - 1, (n - 1.0) * (k - 1)), (fl3 - 1) / (fl3 + k - 1), (fu3 - 1) / (fu3 + k - 1)));

        var singles = results.ToArray();
        var averages = new (string Label, double Icc, double F, double Df1, double Df2)[]
        {
            ("ICC(1,k)", (msr - msw) / msr, msr / msw, n - 1, n * (k - 1.0)),
            ("ICC(2,k)", (msr - mse) / (msr + (msc - mse) / n), msr / mse, n - 1, (n - 1.0) * (k - 1)),
            ("ICC(3,k)", (msr - mse) / msr, msr / mse, n - 1, (n - 1.0) * (k - 1))
        };
        for (var index = 0; index < averages.Length; index++)
        {
            var item = averages[index];
            var baseRow = singles[index];
            results.Add(new(
                item.Label,
                item.Icc,
                item.F,
                item.Df1,
                item.Df2,
                ExtendedStatistics.FSf(item.F, item.Df1, item.Df2),
                k * baseRow.Low / (1 + (k - 1) * baseRow.Low),
                k * baseRow.High / (1 + (k - 1) * baseRow.High)));
        }
        return results;
    }

    private static (double Kappa, double Low, double High, double PValue) CohenKappa(double[,] table, string weightsName)
    {
        var k = table.GetLength(0);
        var nobs = 0.0;
        for (var i = 0; i < k; i++)
        {
            for (var j = 0; j < k; j++) nobs += table[i, j];
        }
        var probs = new double[k, k];
        var freqRow = new double[k];
        var freqCol = new double[k];
        for (var i = 0; i < k; i++)
        {
            for (var j = 0; j < k; j++)
            {
                probs[i, j] = table[i, j] / nobs;
                freqRow[i] += probs[i, j];
                freqCol[j] += probs[i, j];
            }
        }

        double kappa;
        double variance;
        double variance0;
        if (weightsName == "none")
        {
            var agreeExp = 0.0;
            for (var i = 0; i < k; i++) agreeExp += freqRow[i] * freqCol[i];
            var agree = 0.0;
            for (var i = 0; i < k; i++) agree += probs[i, i];
            kappa = (agree - agreeExp) / (1 - agreeExp);

            var termA = 0.0;
            for (var i = 0; i < k; i++) termA += probs[i, i] * Math.Pow(1 - (freqRow[i] + freqCol[i]) * (1 - kappa), 2);
            var termB = 0.0;
            for (var i = 0; i < k; i++)
            {
                for (var j = 0; j < k; j++)
                {
                    if (i == j) continue;
                    termB += probs[i, j] * Math.Pow(freqCol[i] + freqRow[j], 2);
                }
            }
            termB *= Math.Pow(1 - kappa, 2);
            var termC = Math.Pow(kappa - agreeExp * (1 - kappa), 2);
            variance = (termA + termB - termC) / Math.Pow(1 - agreeExp, 2) / nobs;
            var termC0 = 0.0;
            for (var i = 0; i < k; i++) termC0 += freqCol[i] * freqRow[i] * (freqCol[i] + freqRow[i]);
            variance0 = (agreeExp + agreeExp * agreeExp - termC0) / Math.Pow(1 - agreeExp, 2) / nobs;
        }
        else
        {
            var distance = new double[k, k];
            for (var i = 0; i < k; i++)
            {
                for (var j = 0; j < k; j++)
                {
                    var raw = Math.Abs(i - j);
                    distance[i, j] = weightsName == "linear" ? raw / (k - 1.0) : raw * raw / ((k - 1.0) * (k - 1.0));
                }
            }
            var observed = 0.0;
            var expected = 0.0;
            for (var i = 0; i < k; i++)
            {
                for (var j = 0; j < k; j++)
                {
                    observed += distance[i, j] * table[i, j] / nobs;
                    expected += distance[i, j] * freqCol[j] * freqRow[i];
                }
            }
            kappa = 1 - observed / expected;

            var w = new double[k, k];
            var wRow = new double[k];
            var wCol = new double[k];
            for (var i = 0; i < k; i++)
            {
                for (var j = 0; j < k; j++) w[i, j] = 1 - distance[i, j];
            }
            for (var i = 0; i < k; i++)
            {
                for (var j = 0; j < k; j++) wRow[i] += freqCol[j] * w[i, j];
            }
            for (var j = 0; j < k; j++)
            {
                for (var i = 0; i < k; i++) wCol[j] += freqRow[i] * w[i, j];
            }
            var agreeWexp = 0.0;
            for (var i = 0; i < k; i++)
            {
                for (var j = 0; j < k; j++) agreeWexp += w[i, j] * freqCol[j] * freqRow[i];
            }
            var termA = 0.0;
            for (var i = 0; i < k; i++)
            {
                for (var j = 0; j < k; j++)
                {
                    termA += probs[i, j] * Math.Pow(w[i, j] - (wCol[j] + wRow[i]) * (1 - kappa), 2);
                }
            }
            var factor = 1 / (Math.Pow(1 - agreeWexp, 2) * nobs);
            variance = (termA - Math.Pow(kappa - agreeWexp * (1 - kappa), 2)) * factor;
            var variance0Sum = 0.0;
            for (var i = 0; i < k; i++)
            {
                for (var j = 0; j < k; j++)
                {
                    variance0Sum += freqCol[j] * freqRow[i] * Math.Pow(w[i, j] - (wCol[j] + wRow[i]), 2);
                }
            }
            variance0 = (variance0Sum - agreeWexp * agreeWexp) * factor;
        }

        var z = SpecialFunctions.NormalInv(0.975);
        var standardError = Math.Sqrt(Math.Max(0, variance));
        var pValue = 2 * (1 - SpecialFunctions.NormalCdf(Math.Abs(kappa) / Math.Sqrt(Math.Max(variance0, 1e-300))));
        return (kappa, kappa - z * standardError, kappa + z * standardError, pValue);
    }

    private static IReadOnlyList<string> KappaLabels(Frame frame, IReadOnlyList<string> names)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            foreach (var level in frame.Levels(name)) labels.Add(level);
        }
        return [.. labels.OrderBy(value => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ? 0 : 1)
            .ThenBy(value => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : 0)
            .ThenBy(value => value, StringComparer.Ordinal)];
    }

    private static string LandisKoch(double kappa)
    {
        foreach (var (limit, label) in new[] { (0.0, "pobre"), (0.2, "leve"), (0.4, "regular"), (0.6, "moderado"), (0.8, "sustancial") })
        {
            if (kappa <= limit) return label;
        }
        return "casi perfecto";
    }

    private static string IccModel(string label) =>
        label switch
        {
            "ICC(1,1)" => "Un factor aleatorio, una medición",
            "ICC(2,1)" => "Dos factores aleatorios, una medición (acuerdo absoluto)",
            "ICC(3,1)" => "Evaluadores fijos, una medición (consistencia)",
            "ICC(1,k)" => "Un factor aleatorio, promedio de k mediciones",
            "ICC(2,k)" => "Dos factores aleatorios, promedio de k",
            _ => "Evaluadores fijos, promedio de k"
        };
}
