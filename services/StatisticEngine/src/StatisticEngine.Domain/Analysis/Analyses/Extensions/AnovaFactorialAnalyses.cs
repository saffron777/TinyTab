using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Inference;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>
/// ANOVA de varios factores con covariables (port de <c>glm_anova</c> de Sigma): suma de cuadrados
/// tipo I, II o III con codificación suma, medias marginales estimadas y comprobación de supuestos.
/// </summary>
internal static class AnovaFactorialAnalyses
{
    public static readonly AnalysisDefinition Factorial = new(
        "anova-factorial",
        "ANOVA de varios factores y ANCOVA",
        "Modelos lineales",
        "Hasta 4 factores con interacciones y covariables, suma de cuadrados tipo I, II o III, medias marginales y gráficos de efectos.",
        [],
        [],
        [
            new(
                "interactions",
                "Interacciones",
                AnalysisParamKinds.Select,
                "full",
                [
                    new("none", "Solo efectos principales"),
                    new("two", "Hasta de dos factores"),
                    new("full", "Todas")
                ],
                false),
            new(
                "ss_type",
                "Suma de cuadrados",
                AnalysisParamKinds.Select,
                "III",
                [
                    new("I", "Tipo I (secuencial)"),
                    new("II", "Tipo II"),
                    new("III", "Tipo III (la habitual con interacciones)")
                ],
                false),
            ExtendedHelpers.Alpha()
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Modelos lineales",
        Roles:
        [
            new ColumnRole("response", "Respuesta (Y)", ColumnKinds.Numeric),
            new ColumnRole("factors", "Factores (categorías)", ColumnKinds.Any, Multiple: true, MinCount: 1, MaxCount: 4),
            new ColumnRole(
                "covariates",
                "Covariables numéricas (ANCOVA, opcional)",
                ColumnKinds.Numeric,
                Multiple: true,
                Optional: true)
        ]);

    /// <summary>Un término del modelo: nombre legible, factores que lo componen y columnas del diseño.</summary>
    private sealed record Term(string Name, IReadOnlyList<string> Factors, int[] Columns, int Df, bool IsCovariate);

    /// <summary>Diseño completo: columnas (matriz por columnas), términos y bloques de cada factor.</summary>
    private sealed record Design(
        List<double[]> Matrix,
        List<Term> Terms,
        List<Term> FactorTerms,
        List<int[]> FactorBlocks,
        List<Term> CovariateTerms);

    /// <summary>Ajuste por mínimos cuadrados del diseño completo.</summary>
    private sealed record Fit(double[] Beta, double[][] Inverse, double Sse, int Df, int N);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var response = ExtendedHelpers.FirstRole(input, "response", "Respuesta (Y)");
        var factors = input.RoleNames("factors", true).ToArray();
        var covariates = input.RoleNames("covariates").ToArray();
        var interactions = input.Pstr("interactions", "full");
        var ssType = input.Pstr("ss_type", "III").ToUpperInvariant();
        var alpha = input.Pnum("alpha", 0.05);
        if (interactions is not ("none" or "two" or "full")) return ExtendedHelpers.Invalid("Valor no válido para «Interacciones».");
        if (ssType is not ("I" or "II" or "III")) return ExtendedHelpers.Invalid("Valor no válido para «Suma de cuadrados».");

        var columns = new[] { response }
            .Concat(factors)
            .Concat(covariates)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var column in columns)
        {
            if (!frame.Has(column)) return ExtendedHelpers.Invalid($"La columna «{column}» no existe en el dataset.");
        }

        var complete = frame.CompleteCases(columns);
        var n = complete.RowCount;
        var y = complete.Numeric(response);
        var levels = new string[factors.Length][];
        var texts = new string?[factors.Length][];
        for (var index = 0; index < factors.Length; index++)
        {
            levels[index] = complete.Levels(factors[index]);
            if (levels[index].Length < 2)
            {
                return ExtendedHelpers.Invalid($"«{factors[index]}» necesita al menos 2 categorías distintas.");
            }
            texts[index] = complete.Texts(factors[index]);
        }
        var covariateValues = covariates.Select(covariate => complete.Numeric(covariate)).ToArray();

        var design = BuildDesign(n, factors, levels, texts, covariates, covariateValues, interactions);
        var parameterCount = design.Matrix.Count;
        if (n <= parameterCount)
        {
            return ExtendedHelpers.Invalid(
                $"Se necesitan más observaciones que parámetros: hay {n} filas completas y {parameterCount} parámetros.");
        }

        var matrix = new double[n][];
        for (var row = 0; row < n; row++)
        {
            matrix[row] = new double[parameterCount];
            for (var column = 0; column < parameterCount; column++) matrix[row][column] = design.Matrix[column][row];
        }

        var fitResult = FitModel(matrix, y);
        if (fitResult.IsError) return Result.Err<AnalysisResult>(fitResult.Error.Code, fitResult.Error.Message);
        var fit = fitResult.Value;
        var mean = NumericMath.Mean(y);
        var sst = 0.0;
        for (var row = 0; row < n; row++) sst += Math.Pow(y[row] - mean, 2);
        var mse = fit.Sse / fit.Df;
        var r2 = sst > 0 ? 1 - fit.Sse / sst : 1;
        var r2Adjusted = 1 - (1 - r2) * ((n - 1) / (double)Math.Max(1, fit.Df));

        var sums = ssType switch
        {
            "I" => SequentialSums(matrix, y, design.Terms, sst),
            "II" => TypeTwoSums(matrix, y, design.Terms),
            _ => TypeThreeSums(fit, design.Terms)
        };
        if (sums.Any(double.IsNaN))
        {
            return ExtendedHelpers.Error(
                DomainErrorCode.SingularMatrix,
                "El diseño es singular: revise las celdas vacías o la colinealidad de las covariables.");
        }

        var rows = new List<IReadOnlyList<string>>();
        var effects = new Dictionary<string, object?>(StringComparer.Ordinal);
        var significant = new List<string>();
        for (var index = 0; index < design.Terms.Count; index++)
        {
            var term = design.Terms[index];
            var sum = sums[index];
            var meanSquare = sum / term.Df;
            var f = meanSquare / mse;
            var pValue = ExtendedStatistics.FSf(f, term.Df, fit.Df);
            var etaPartial = sum + fit.Sse > 0 ? sum / (sum + fit.Sse) : double.NaN;
            var omegaPartial = Math.Max(0, (sum - term.Df * mse) / (sum + (n - term.Df) * mse));
            rows.Add([
                term.Name,
                JsNumber.ToString(term.Df),
                ModelHelpers.N(sum),
                ModelHelpers.N(meanSquare),
                ModelHelpers.N(f),
                ModelHelpers.N(pValue),
                ModelHelpers.N(etaPartial),
                ModelHelpers.N(omegaPartial)
            ]);
            effects[term.Name] = pValue;
            if (pValue < alpha) significant.Add(term.Name);
        }
        rows.Add(["Error", JsNumber.ToString(fit.Df), ModelHelpers.N(fit.Sse), ModelHelpers.N(mse), "*", "*", "*", "*"]);
        rows.Add(["Total", JsNumber.ToString(n - 1), ModelHelpers.N(sst), "*", "*", "*", "*", "*"]);

        var note = "η² parcial = SC del efecto / (SC del efecto + SC del error). ω² parcial es la versión menos sesgada."
                   + (ssType == "III" ? " Con tipo III los factores usan codificación suma (desvíos de la media global)." : string.Empty);

        var sections = new List<AnalysisSection>
        {
            new TableSection(
                "Análisis de varianza",
                ["Fuente", "gl", "Suma de cuadrados", "Cuadrado medio", "F", "Valor p", "η² parcial", "ω² parcial"],
                rows,
                Note: note),
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("R²", ModelHelpers.N(r2)),
                new TileItem("R² ajustado", ModelHelpers.N(r2Adjusted)),
                new TileItem("Efectos significativos", JsNumber.ToString(significant.Count))
            ])
        };

        var percentage = SigmaText.General(100 * (1 - alpha));
        sections.Add(significant.Count > 0
            ? new AlertSection("Conclusión", "success", $"Efectos significativos al {percentage} %: {string.Join(", ", significant)}.")
            : new AlertSection("Conclusión", "info", $"Ningún efecto es significativo al {percentage} %."));

        var cells = CellMeans(y, factors, levels, texts);
        var cellRows = new List<IReadOnlyList<string>>();
        foreach (var cell in cells)
        {
            cellRows.Add([
                .. cell.Labels,
                JsNumber.ToString(cell.Rows.Count),
                ModelHelpers.N(NumericMath.Mean(cell.Rows.Select(row => y[row]).ToArray())),
                ModelHelpers.N(NumericMath.Sd(cell.Rows.Select(row => y[row]).ToArray()))
            ]);
        }

        var brown = BrownForsythe(cells, y);
        var shapiro = ShapiroWilk(fit, matrix, y);
        var assumptionRows = new List<IReadOnlyList<string>>();
        assumptionRows.Add([
            "Homogeneidad de varianzas entre celdas (Brown-Forsythe)",
            ModelHelpers.N(brown.Statistic),
            ModelHelpers.N(brown.PValue)
        ]);
        assumptionRows.Add([
            "Normalidad de los residuos (Shapiro-Wilk)",
            ModelHelpers.N(shapiro.Statistic),
            ModelHelpers.N(shapiro.PValue)
        ]);
        for (var index = 0; index < covariates.Length; index++)
        {
            var slopes = SlopeHomogeneity(matrix, y, fit, design.FactorBlocks, covariateValues[index]);
            assumptionRows.Add([
                $"Homogeneidad de pendientes: {covariates[index]} × factores",
                ModelHelpers.N(slopes.Statistic),
                ModelHelpers.N(slopes.PValue)
            ]);
        }
        sections.Add(new TableSection(
            "Comprobación de supuestos",
            ["Prueba", "Estadístico", "Valor p"],
            assumptionRows,
            Note: covariates.Length > 0
                ? "Valores p pequeños indican incumplimiento. En ANCOVA las pendientes de la covariable deben ser iguales entre grupos."
                : "Valores p pequeños indican incumplimiento."));

        sections.Add(new TableSection("Medias por celda", [.. factors, "n", "Media", "Desv. est."], cellRows));

        var critical = InferenceDistributions.TQuantile(1 - alpha / 2, fit.Df);
        var means = new Dictionary<string, double[]>[design.FactorTerms.Count];
        for (var index = 0; index < design.FactorTerms.Count; index++)
        {
            var term = design.FactorTerms[index];
            var factorLevels = levels[index];
            var table = new List<IReadOnlyList<string>>();
            means[index] = new Dictionary<string, double[]>(StringComparer.Ordinal);
            for (var level = 0; level < factorLevels.Length; level++)
            {
                var point = EmmsPoint(parameterCount, term, level, design.CovariateTerms, covariateValues);
                var estimate = Dot(fit.Beta, point);
                var variance = 0.0;
                for (var i = 0; i < parameterCount; i++)
                {
                    for (var j = 0; j < parameterCount; j++) variance += point[i] * fit.Inverse[i][j] * point[j];
                }
                var standardError = Math.Sqrt(Math.Max(0, mse * variance));
                var low = estimate - critical * standardError;
                var high = estimate + critical * standardError;
                means[index][factorLevels[level]] = [estimate, standardError, low, high];
                table.Add([
                    factorLevels[level],
                    ModelHelpers.N(estimate),
                    ModelHelpers.N(standardError),
                    ModelHelpers.N(low),
                    ModelHelpers.N(high)
                ]);
            }
            var sectionTitle = covariates.Length > 0
                ? $"Medias marginales estimadas de «{factors[index]}» (covariables en su media)"
                : $"Medias marginales estimadas de «{factors[index]}»";
            sections.Add(new TableSection(sectionTitle, ["Nivel", "Media", "Error est.", "IC inf.", "IC sup."], table));
        }

        var charts = new List<ChartSpec>();
        for (var index = 0; index < design.FactorTerms.Count; index++)
        {
            var points = levels[index]
                .Select((level, position) => new ScatterPoint(position + 1, means[index][level][0]))
                .ToList();
            charts.Add(new ScatterChart($"Medias marginales de «{factors[index]}»", points, factors[index], "Media"));
        }
        for (var first = 0; first < factors.Length; first++)
        {
            for (var second = first + 1; second < factors.Length; second++)
            {
                var points = new List<ScatterPoint>();
                for (var a = 0; a < levels[first].Length; a++)
                {
                    for (var b = 0; b < levels[second].Length; b++)
                    {
                        var inside = cells
                            .Where(cell => string.Equals(cell.Labels[first], levels[first][a], StringComparison.Ordinal)
                                           && string.Equals(cell.Labels[second], levels[second][b], StringComparison.Ordinal))
                            .SelectMany(cell => cell.Rows)
                            .ToArray();
                        if (inside.Length == 0) continue;
                        points.Add(new ScatterPoint(a + 1, NumericMath.Mean(inside.Select(row => y[row]).ToArray())));
                    }
                }
                charts.Add(new ScatterChart($"Interacción «{factors[first]}» × «{factors[second]}»", points, factors[first], "Media"));
            }
        }

        var title = factors.Length == 1 ? "ANOVA de 1 factor" : $"ANOVA de {factors.Length} factores";
        if (covariates.Length > 0) title += " con covariables (ANCOVA)";

        return Result.Ok(new AnalysisResult(
            "anova-factorial",
            $"{title}: {response}",
            sections,
            Subtitle: $"suma de cuadrados tipo {ssType} · n = {n}",
            Charts: charts,
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["r2"] = r2,
                ["effects"] = effects,
                ["mse"] = mse
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    /// <summary>Una celda del diseño: etiquetas de los factores y filas que caen en ella.</summary>
    private sealed record Cell(string[] Labels, List<int> Rows);

    private static List<Cell> CellMeans(double[] y, string[] factors, string[][] levels, string?[][] texts)
    {
        var cells = new List<Cell>();
        foreach (var labels in CartesianLabels(levels))
        {
            var rows = new List<int>();
            for (var row = 0; row < y.Length; row++)
            {
                var inside = true;
                for (var index = 0; index < factors.Length; index++)
                {
                    if (!string.Equals(texts[index][row], labels[index], StringComparison.Ordinal)) inside = false;
                }
                if (inside) rows.Add(row);
            }
            if (rows.Count > 0) cells.Add(new Cell(labels, rows));
        }
        return cells;
    }

    private static Design BuildDesign(
        int n,
        string[] factorNames,
        string[][] levels,
        string?[][] texts,
        string[] covariates,
        double[][] covariateValues,
        string interactions)
    {
        var matrix = new List<double[]> { Enumerable.Repeat(1.0, n).ToArray() };
        var terms = new List<Term>();
        var factorTerms = new List<Term>();
        var factorBlocks = new List<int[]>();
        var covariateTerms = new List<Term>();

        for (var index = 0; index < factorNames.Length; index++)
        {
            var block = new int[levels[index].Length - 1];
            for (var level = 1; level < levels[index].Length; level++)
            {
                var values = new double[n];
                for (var row = 0; row < n; row++)
                {
                    var label = texts[index][row];
                    values[row] = string.Equals(label, levels[index][level], StringComparison.Ordinal)
                        ? 1.0
                        : string.Equals(label, levels[index][0], StringComparison.Ordinal) ? -1.0 : 0.0;
                }
                block[level - 1] = matrix.Count;
                matrix.Add(values);
            }
            var term = new Term(factorNames[index], [factorNames[index]], block, block.Length, false);
            factorBlocks.Add(block);
            factorTerms.Add(term);
            terms.Add(term);
        }

        var depth = interactions switch
        {
            "none" => 1,
            "two" => Math.Min(2, factorNames.Length),
            _ => factorNames.Length
        };
        for (var size = 2; size <= depth; size++)
        {
            foreach (var combination in Combinations(factorNames.Length, size))
            {
                var name = string.Join(" × ", combination.Select(index => factorNames[index]));
                var columns = new List<int>();
                foreach (var selection in Cartesian(combination.Select(index => factorBlocks[index]).ToArray()))
                {
                    var values = new double[n];
                    Array.Fill(values, 1.0);
                    foreach (var column in selection)
                    {
                        for (var row = 0; row < n; row++) values[row] *= matrix[column][row];
                    }
                    columns.Add(matrix.Count);
                    matrix.Add(values);
                }
                terms.Add(new Term(name, combination.Select(index => factorNames[index]).ToArray(), [.. columns], columns.Count, false));
            }
        }

        for (var index = 0; index < covariates.Length; index++)
        {
            var column = matrix.Count;
            matrix.Add((double[])covariateValues[index].Clone());
            var term = new Term(covariates[index], [covariates[index]], [column], 1, true);
            covariateTerms.Add(term);
            terms.Add(term);
        }

        return new Design(matrix, terms, factorTerms, factorBlocks, covariateTerms);
    }

    private static Result<Fit> FitModel(double[][] x, double[] y)
    {
        var n = x.Length;
        var parameters = x[0].Length;
        var xtx = new double[parameters][];
        var xty = new double[parameters];
        for (var i = 0; i < parameters; i++)
        {
            xtx[i] = new double[parameters];
            for (var j = 0; j < parameters; j++)
            {
                var sum = 0.0;
                for (var row = 0; row < n; row++) sum += x[row][i] * x[row][j];
                xtx[i][j] = sum;
            }
            var sumY = 0.0;
            for (var row = 0; row < n; row++) sumY += x[row][i] * y[row];
            xty[i] = sumY;
        }
        var beta = LinearAlgebra.SolveLinear(xtx, xty);
        if (beta.IsError) return Result.Err<Fit>(beta.Error.Code, beta.Error.Message);
        var inverse = LinearAlgebra.Inverse(xtx);
        if (inverse.IsError) return Result.Err<Fit>(inverse.Error.Code, inverse.Error.Message);
        var sse = 0.0;
        for (var row = 0; row < n; row++)
        {
            var fitted = 0.0;
            for (var column = 0; column < parameters; column++) fitted += x[row][column] * beta.Value[column];
            sse += Math.Pow(y[row] - fitted, 2);
        }
        return Result.Ok(new Fit(beta.Value, inverse.Value, sse, n - parameters, n));
    }

    private static double ResidualSse(double[][] x, double[] y)
    {
        var fit = FitModel(x, y);
        return fit.IsError ? double.NaN : fit.Value.Sse;
    }

    private static double[] TypeThreeSums(Fit fit, List<Term> terms)
    {
        var sums = new double[terms.Count];
        for (var index = 0; index < terms.Count; index++)
        {
            var columns = terms[index].Columns;
            var block = new double[columns.Length][];
            for (var i = 0; i < columns.Length; i++)
            {
                block[i] = new double[columns.Length];
                for (var j = 0; j < columns.Length; j++) block[i][j] = fit.Inverse[columns[i]][columns[j]];
            }
            var inverse = LinearAlgebra.Inverse(block);
            if (inverse.IsError)
            {
                sums[index] = double.NaN;
                continue;
            }
            var coefficient = columns.Select(column => fit.Beta[column]).ToArray();
            var sum = 0.0;
            for (var i = 0; i < coefficient.Length; i++)
            {
                for (var j = 0; j < coefficient.Length; j++) sum += coefficient[i] * inverse.Value[i][j] * coefficient[j];
            }
            sums[index] = Math.Max(0, sum);
        }
        return sums;
    }

    private static double[] SequentialSums(double[][] design, double[] y, List<Term> terms, double sst)
    {
        var sums = new double[terms.Count];
        var included = new List<int> { 0 };
        var previous = sst;
        for (var index = 0; index < terms.Count; index++)
        {
            included.AddRange(terms[index].Columns);
            var sse = ResidualSse(Columns(design, included), y);
            sums[index] = previous - sse;
            previous = sse;
        }
        return sums;
    }

    private static double[] TypeTwoSums(double[][] design, double[] y, List<Term> terms)
    {
        var sums = new double[terms.Count];
        for (var index = 0; index < terms.Count; index++)
        {
            var target = terms[index];
            var restricted = new List<int> { 0 };
            foreach (var term in terms)
            {
                if (!Contains(term, target)) restricted.AddRange(term.Columns);
            }
            var without = ResidualSse(Columns(design, restricted), y);
            restricted.AddRange(target.Columns);
            var with = ResidualSse(Columns(design, restricted), y);
            sums[index] = without - with;
        }
        return sums;
    }

    private static bool Contains(Term term, Term target)
    {
        if (target.IsCovariate) return term.IsCovariate && term.Name == target.Name;
        var factors = term.Factors.ToHashSet(StringComparer.Ordinal);
        return target.Factors.All(factors.Contains);
    }

    private static double[][] Columns(double[][] design, IReadOnlyList<int> columns)
    {
        var result = new double[design.Length][];
        for (var row = 0; row < design.Length; row++)
        {
            result[row] = new double[columns.Count];
            for (var column = 0; column < columns.Count; column++) result[row][column] = design[row][columns[column]];
        }
        return result;
    }

    private static (double Statistic, double PValue) BrownForsythe(List<Cell> cells, double[] y)
    {
        var groups = new List<double[]>();
        foreach (var cell in cells)
        {
            if (cell.Rows.Count < 2) return (double.NaN, double.NaN);
            groups.Add(cell.Rows.Select(row => y[row]).ToArray());
        }
        if (groups.Count < 2) return (double.NaN, double.NaN);
        var levene = ExtendedHelpers.Levene(groups, false);
        return (levene.Statistic, levene.PValue);
    }

    private static (double Statistic, double PValue) ShapiroWilk(Fit fit, double[][] design, double[] y)
    {
        var residuals = new double[fit.N];
        for (var row = 0; row < fit.N; row++)
        {
            var fitted = 0.0;
            for (var column = 0; column < fit.Beta.Length; column++) fitted += design[row][column] * fit.Beta[column];
            residuals[row] = y[row] - fitted;
        }
        return AnovaStatistics.ShapiroWilk(residuals);
    }

    private static (double Statistic, double PValue) SlopeHomogeneity(
        double[][] design,
        double[] y,
        Fit fit,
        List<int[]> blocks,
        double[] covariate)
    {
        var extra = new List<double[]>();
        foreach (var block in blocks)
        {
            foreach (var column in block)
            {
                var values = new double[design.Length];
                for (var row = 0; row < design.Length; row++) values[row] = design[row][column] * covariate[row];
                extra.Add(values);
            }
        }
        var extended = new double[design.Length][];
        for (var row = 0; row < design.Length; row++)
        {
            extended[row] = [.. design[row], .. extra.Select(column => column[row])];
        }
        var sse = ResidualSse(extended, y);
        if (double.IsNaN(sse)) return (double.NaN, double.NaN);
        var denominatorDf = design.Length - design[0].Length - extra.Count;
        if (denominatorDf <= 0) return (double.NaN, double.NaN);
        var sum = Math.Max(0, fit.Sse - sse);
        var f = (sum / extra.Count) / (sse / denominatorDf);
        return (f, ExtendedStatistics.FSf(f, extra.Count, denominatorDf));
    }

    private static double[] EmmsPoint(
        int parameters,
        Term factorTerm,
        int level,
        List<Term> covariateTerms,
        double[][] covariateValues)
    {
        var point = new double[parameters];
        point[0] = 1;
        for (var column = 0; column < factorTerm.Columns.Length; column++)
        {
            point[factorTerm.Columns[column]] = level == 0 ? -1.0 : (level == column + 1 ? 1.0 : 0.0);
        }
        for (var index = 0; index < covariateTerms.Count; index++)
        {
            point[covariateTerms[index].Columns[0]] = NumericMath.Mean(covariateValues[index]);
        }
        return point;
    }

    private static double Dot(double[] left, double[] right)
    {
        var sum = 0.0;
        for (var index = 0; index < left.Length; index++) sum += left[index] * right[index];
        return sum;
    }

    private static List<string[]> CartesianLabels(string[][] levels)
    {
        var result = new List<string[]> { Array.Empty<string>() };
        foreach (var level in levels)
        {
            var next = new List<string[]>(result.Count * level.Length);
            foreach (var prefix in result)
            {
                foreach (var value in level) next.Add([.. prefix, value]);
            }
            result = next;
        }
        return result;
    }

    private static IEnumerable<int[]> Combinations(int count, int size)
    {
        var indices = new int[size];
        for (var index = 0; index < size; index++) indices[index] = index;
        while (true)
        {
            yield return (int[])indices.Clone();
            var position = size - 1;
            while (position >= 0 && indices[position] == count - size + position) position--;
            if (position < 0) yield break;
            indices[position]++;
            for (var index = position + 1; index < size; index++) indices[index] = indices[index - 1] + 1;
        }
    }

    private static List<int[]> Cartesian(IReadOnlyList<int[]> groups)
    {
        var result = new List<int[]> { Array.Empty<int>() };
        foreach (var group in groups)
        {
            var next = new List<int[]>(result.Count * group.Length);
            foreach (var prefix in result)
            {
                foreach (var value in group) next.Add([.. prefix, value]);
            }
            result = next;
        }
        return result;
    }
}
