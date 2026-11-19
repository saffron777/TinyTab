using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>ANOVA anidado con componentes de la varianza (port de <c>anova_advanced._nested</c>).</summary>
internal static class NestedAnovaAnalyses
{
    public static readonly AnalysisDefinition Nested = new(
        "anova-nested",
        "ANOVA anidado",
        "Modelos lineales",
        "Un factor cuyos niveles solo tienen sentido dentro de otro (lotes dentro de proveedores, aulas dentro de escuelas), con componentes de la varianza.",
        [],
        [],
        [
            new(
                "b_random",
                "El factor anidado es aleatorio",
                AnalysisParamKinds.Boolean,
                true,
                [],
                false,
                Help: "Aleatorio: los niveles son una muestra de muchos posibles (lo habitual)."),
            new AnalysisParamDefinition("alpha", "Nivel de significancia α", AnalysisParamKinds.Number, 0.05, [], false, Minimum: 0.001, Maximum: 0.5)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Modelos lineales",
        Roles:
        [
            new ColumnRole("response", "Respuesta (Y)", ColumnKinds.Numeric),
            new ColumnRole("factor_a", "Factor principal", ColumnKinds.Any),
            new ColumnRole("factor_b", "Factor anidado en el principal", ColumnKinds.Any)
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var response = ExtendedHelpers.FirstRole(input, "response", "Respuesta (Y)");
        var aName = ExtendedHelpers.FirstRole(input, "factor_a", "Factor principal");
        var bName = ExtendedHelpers.FirstRole(input, "factor_b", "Factor anidado");
        if (aName == bName) return ExtendedHelpers.Invalid("El factor anidado debe ser distinto del factor principal.");

        var complete = frame.CompleteCases([response, aName, bName]);
        var aLabels = complete.Texts(aName);
        var bLabels = complete.Texts(bName);
        var y = complete.Numeric(response);
        var n = complete.RowCount;
        var aLevels = complete.Levels(aName);
        var bLevels = complete.Levels(bName);
        var aIndex = aLabels.Select(label => Array.IndexOf(aLevels, label)).ToArray();
        var bIndex = bLabels.Select(label => Array.IndexOf(bLevels, label)).ToArray();
        var cells = new Dictionary<(int A, int B), List<int>>();
        for (var row = 0; row < n; row++)
        {
            var key = (aIndex[row], bIndex[row]);
            if (!cells.TryGetValue(key, out var list)) cells[key] = list = [];
            list.Add(row);
        }
        for (var a = 0; a < aLevels.Length; a++)
        {
            var count = cells.Keys.Count(key => key.A == a);
            if (count < 2) return ExtendedHelpers.Invalid("Cada nivel del factor principal necesita al menos 2 niveles del factor anidado.");
        }
        if (cells.Values.Any(list => list.Count < 2))
        {
            return ExtendedHelpers.Invalid("Cada combinación necesita al menos 2 observaciones para estimar el error.");
        }

        var intercept = Enumerable.Repeat(1.0, n).ToArray();
        var x0 = intercept.Select(value => new[] { value }).ToArray();
        var xa = new double[n][];
        for (var row = 0; row < n; row++)
        {
            xa[row] = new double[1 + (aLevels.Length - 1)];
            xa[row][0] = 1;
            for (var a = 1; a < aLevels.Length; a++) xa[row][a] = aIndex[row] == a ? 1 : 0;
        }
        var xab = new double[n][];
        for (var row = 0; row < n; row++)
        {
            var columns = new List<double> { 1 };
            for (var a = 1; a < aLevels.Length; a++) columns.Add(aIndex[row] == a ? 1 : 0);
            foreach (var a in Enumerable.Range(0, aLevels.Length))
            {
                for (var b = 1; b < bLevels.Length; b++) columns.Add(aIndex[row] == a && bIndex[row] == b ? 1 : 0);
            }
            xab[row] = [.. columns];
        }
        var fit0 = LinearModel.Fit(x0, y);
        var fitA = LinearModel.Fit(xa, y);
        var fitAB = LinearModel.Fit(xab, y);
        if (fit0.IsError) return Result.Err<AnalysisResult>(fit0.Error.Code, fit0.Error.Message);
        if (fitA.IsError) return Result.Err<AnalysisResult>(fitA.Error.Code, fitA.Error.Message);
        if (fitAB.IsError) return Result.Err<AnalysisResult>(fitAB.Error.Code, fitAB.Error.Message);

        var ssA = fit0.Value.Sse - fitA.Value.Sse;
        var ssB = fitA.Value.Sse - fitAB.Value.Sse;
        var sse = fitAB.Value.Sse;
        var dfA = aLevels.Length - 1;
        var dfB = aLevels.Length * (bLevels.Length - 1);
        var dfE = n - aLevels.Length * bLevels.Length;
        var msA = ssA / dfA;
        var msB = ssB / dfB;
        var mse = sse / dfE;
        var random = input.Pbool("b_random", true);
        var fA = msA / (random ? msB : mse);
        var dfDen = random ? dfB : dfE;
        var pA = ExtendedStatistics.FSf(fA, dfA, dfDen);
        var fB = msB / mse;
        var pB = ExtendedStatistics.FSf(fB, dfB, dfE);

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem($"Niveles de {aName}", JsNumber.ToString(aLevels.Length)),
                new TileItem($"Combinaciones {aName}/{bName}", JsNumber.ToString(cells.Count)),
                new TileItem("Error estándar residual", ModelHelpers.N(Math.Sqrt(mse)))
            ])
        };
        sections.Add(new TableSection(
            "Análisis de varianza",
            ["Fuente", "gl", "Suma de cuadrados", "Cuadrado medio", "Denominador de F", "F", "Valor p"],
            [
                new[]
                {
                    aName,
                    JsNumber.ToString(dfA),
                    ModelHelpers.N(ssA),
                    ModelHelpers.N(msA),
                    random ? $"{bName}({aName})" : "Error",
                    ModelHelpers.N(fA),
                    ModelHelpers.P(pA)
                },
                new[]
                {
                    $"{bName}({aName})",
                    JsNumber.ToString(dfB),
                    ModelHelpers.N(ssB),
                    ModelHelpers.N(msB),
                    "Error",
                    ModelHelpers.N(fB),
                    ModelHelpers.P(pB)
                },
                new[] { "Error", JsNumber.ToString(dfE), ModelHelpers.N(sse), ModelHelpers.N(mse), string.Empty, string.Empty, string.Empty }
            ],
            Note: "Con el factor anidado aleatorio, el factor principal se prueba contra el cuadrado medio del factor anidado."));

        var sizes = cells.Values.Select(list => list.Count).ToArray();
        var countsPerA = Enumerable.Range(0, aLevels.Length)
            .Select(a => cells.Keys.Count(key => key.A == a))
            .ToArray();
        var balanced = sizes.Distinct().Count() == 1 && countsPerA.Distinct().Count() == 1;
        if (random)
        {
            var m = sizes.Average();
            var nb = countsPerA.Average();
            var varB = Math.Max((msB - mse) / m, 0);
            var varA = Math.Max((msA - msB) / (nb * m), 0);
            var total = varA + varB + mse;
            sections.Add(new TableSection(
                "Componentes de la varianza",
                ["Fuente", "Varianza estimada", "% del total"],
                [
                    new[] { aName, ModelHelpers.N(varA), ModelHelpers.N(100 * varA / total) },
                    new[] { $"{bName}({aName})", ModelHelpers.N(varB), ModelHelpers.N(100 * varB / total) },
                    new[] { "Error", ModelHelpers.N(mse), ModelHelpers.N(100 * mse / total) }
                ],
                Note: "Estimación por cuadrados medios esperados"
                      + (balanced ? "." : " (aproximada: el diseño no está balanceado).")));
        }
        if (!balanced)
        {
            sections.Add(new AlertSection(
                "Aviso",
                "info",
                "El diseño no está balanceado: la suma de cuadrados secuencial depende del orden y los componentes son aproximados."));
        }

        var means = Enumerable.Range(0, aLevels.Length).Select(a =>
        {
            var values = Enumerable.Range(0, n).Where(row => aIndex[row] == a).Select(row => y[row]).ToArray();
            return (IReadOnlyList<string>)new[]
            {
                aLevels[a],
                JsNumber.ToString(values.Length),
                ModelHelpers.N(NumericMath.Mean(values)),
                ModelHelpers.N(NumericMath.Sd(values))
            };
        }).ToArray();
        sections.Add(new TableSection($"Medias por «{aName}»", [aName, "n", "Media", "Desv. est."], means));

        var points = new List<ScatterPoint>();
        foreach (var (key, rows) in cells.OrderBy(pair => pair.Key.A).ThenBy(pair => pair.Key.B))
        {
            var mean = NumericMath.Mean(rows.Select(row => y[row]).ToArray());
            points.Add(new ScatterPoint(key.B, mean));
        }

        return Result.Ok(new AnalysisResult(
            "anova-nested",
            $"ANOVA anidado: {response}",
            sections,
            Subtitle: $"«{bName}» anidado en «{aName}» ({(random ? "aleatorio" : "fijo")}) · n = {n}",
            Charts:
            [
                new ScatterChart($"Medias de cada «{bName}» dentro de «{aName}»", points, bName, "Media")
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["p_a"] = pA,
                ["p_b"] = pB,
                ["mse"] = mse
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
