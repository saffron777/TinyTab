using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>
/// MANOVA (port de <c>manova</c> de Sigma): contraste multivariado de cada factor con las cuatro
/// estadísticas clásicas, pruebas univariadas de seguimiento con Bonferroni y M de Box.
/// </summary>
internal static class ManovaAnalyses
{
    public static readonly AnalysisDefinition Manova = new(
        "anova-manova",
        "MANOVA",
        "Modelos lineales",
        "Compara grupos en varias respuestas a la vez: Wilks, Pillai, Hotelling-Lawley y Roy, con pruebas univariadas de seguimiento y M de Box.",
        [],
        [],
        [ExtendedHelpers.Alpha()],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Modelos lineales",
        Roles:
        [
            new ColumnRole("responses", "Respuestas numéricas (2 o más)", ColumnKinds.Numeric, Multiple: true, MinCount: 2),
            new ColumnRole("factors", "Factores", ColumnKinds.Any, Multiple: true, MinCount: 1, MaxCount: 3)
        ]);

    private sealed record MultivariateTest(string Name, double Value, double F, double Df1, double Df2, double PValue);

    private sealed record ManovaEffect(string Name, int Df, MultivariateTest[] Tests, double[] HypothesisSquares);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var responses = input.RoleNames("responses", true).ToArray();
        var factors = input.RoleNames("factors", true).ToArray();
        var alpha = input.Pnum("alpha", 0.05);
        if (responses.Length < 2)
        {
            return ExtendedHelpers.Invalid("Elija al menos dos columnas para «Respuestas numéricas (2 o más)».");
        }

        var columns = responses.Concat(factors).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var column in columns)
        {
            if (!frame.Has(column)) return ExtendedHelpers.Invalid($"La columna «{column}» no existe en el dataset.");
        }

        var complete = frame.CompleteCases(columns);
        var n = complete.RowCount;
        var count = responses.Length;
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

        var responseValues = responses.Select(response => complete.Numeric(response)).ToArray();
        var y = new double[n][];
        for (var row = 0; row < n; row++)
        {
            y[row] = new double[count];
            for (var response = 0; response < count; response++) y[row][response] = responseValues[response][row];
        }

        var design = BuildDesign(n, factors, levels, texts);
        var parameterCount = design.Columns.Count;
        if (n <= parameterCount + count)
        {
            return ExtendedHelpers.Invalid(
                $"Se necesitan más observaciones que parámetros: hay {n} filas completas y {parameterCount + count} parámetros.");
        }

        var x = new double[n][];
        for (var row = 0; row < n; row++)
        {
            x[row] = new double[parameterCount];
            for (var column = 0; column < parameterCount; column++) x[row][column] = design.Columns[column][row];
        }

        var inverseResult = InverseCrossProduct(x);
        if (inverseResult.IsError) return Result.Err<AnalysisResult>(inverseResult.Error.Code, inverseResult.Error.Message);
        var inverse = inverseResult.Value;
        var crossProduct = new double[parameterCount][];
        for (var i = 0; i < parameterCount; i++) crossProduct[i] = new double[count];
        for (var row = 0; row < n; row++)
        {
            for (var i = 0; i < parameterCount; i++)
            {
                for (var response = 0; response < count; response++) crossProduct[i][response] += x[row][i] * y[row][response];
            }
        }
        var beta = new double[parameterCount][];
        for (var i = 0; i < parameterCount; i++)
        {
            beta[i] = new double[count];
            for (var response = 0; response < count; response++)
            {
                var sum = 0.0;
                for (var j = 0; j < parameterCount; j++) sum += inverse[i][j] * crossProduct[j][response];
                beta[i][response] = sum;
            }
        }
        var residuals = new double[n][];
        for (var row = 0; row < n; row++)
        {
            residuals[row] = new double[count];
            for (var response = 0; response < count; response++)
            {
                var fitted = 0.0;
                for (var column = 0; column < parameterCount; column++) fitted += x[row][column] * beta[column][response];
                residuals[row][response] = y[row][response] - fitted;
            }
        }
        var error = new double[count][];
        for (var i = 0; i < count; i++)
        {
            error[i] = new double[count];
            for (var j = 0; j < count; j++)
            {
                var sum = 0.0;
                for (var row = 0; row < n; row++) sum += residuals[row][i] * residuals[row][j];
                error[i][j] = sum;
            }
        }
        var residualDf = n - parameterCount;
        var errorInverseSquare = InverseSquareRoot(error);
        if (errorInverseSquare.IsError) return Result.Err<AnalysisResult>(errorInverseSquare.Error.Code, errorInverseSquare.Error.Message);

        var effects = new List<ManovaEffect>();
        for (var index = 0; index < factors.Length; index++)
        {
            var factorColumns = design.FactorColumns[index];
            var hypothesis = HypothesisMatrix(factorColumns, beta, inverse, count);
            if (hypothesis.IsError) return Result.Err<AnalysisResult>(hypothesis.Error.Code, hypothesis.Error.Message);
            var eigenvalues = Eigenvalues(errorInverseSquare.Value, hypothesis.Value);
            if (eigenvalues.IsError) return Result.Err<AnalysisResult>(eigenvalues.Error.Code, eigenvalues.Error.Message);
            var squares = Enumerable.Range(0, count).Select(response => hypothesis.Value[response][response]).ToArray();
            effects.Add(new ManovaEffect(
                factors[index],
                factorColumns.Length,
                ComputeTests(eigenvalues.Value, count, factorColumns.Length, residualDf),
                squares));
        }

        var box = BoxM(y, factors, levels, texts, n, count);

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("Respuestas", JsNumber.ToString(count)),
                new TileItem("Factores", JsNumber.ToString(factors.Length))
            ])
        };

        var multivariateRows = new List<IReadOnlyList<string>>();
        var wilks = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var effect in effects)
        {
            foreach (var test in effect.Tests)
            {
                multivariateRows.Add([
                    effect.Name,
                    test.Name,
                    ModelHelpers.N(test.Value),
                    ModelHelpers.N(test.F),
                    ModelHelpers.N(test.Df1),
                    ModelHelpers.N(test.Df2),
                    ModelHelpers.N(test.PValue)
                ]);
            }
            wilks[effect.Name] = effect.Tests[0].PValue;
        }
        sections.Add(new TableSection(
            "Pruebas multivariadas",
            ["Efecto", "Estadístico", "Valor", "F", "gl (num)", "gl (den)", "Valor p"],
            multivariateRows,
            Note: "Pillai es la más robusta ante incumplimientos; Wilks es la más usada; Roy es la más potente pero la más sensible."));

        var univariateRows = new List<IReadOnlyList<string>>();
        var bonferroni = alpha / count;
        foreach (var effect in effects)
        {
            for (var response = 0; response < count; response++)
            {
                var squares = effect.HypothesisSquares[response];
                var sse = 0.0;
                for (var row = 0; row < n; row++) sse += residuals[row][response] * residuals[row][response];
                var f = (squares / effect.Df) / (sse / residualDf);
                var pValue = ExtendedStatistics.FSf(f, effect.Df, residualDf);
                univariateRows.Add([
                    responses[response],
                    effect.Name,
                    JsNumber.ToString(effect.Df),
                    ModelHelpers.N(f),
                    ModelHelpers.N(pValue),
                    pValue < bonferroni ? "si" : "no"
                ]);
            }
        }
        sections.Add(new TableSection(
            "Pruebas univariadas de seguimiento",
            ["Respuesta", "Efecto", "gl", "F", "Valor p", $"Significativo con α/{count}"],
            univariateRows,
            Note: $"Se usa la corrección de Bonferroni: α/{count} = {SigmaText.Fixed4(bonferroni)}."));

        sections.Add(new TableSection(
            "Igualdad de matrices de covarianza (M de Box)",
            ["χ²", "gl", "Valor p"],
            [
                [ModelHelpers.N(box.Statistic), JsNumber.ToString(box.Df), ModelHelpers.N(box.PValue)]
            ],
            Note: "M de Box es muy sensible a la no normalidad; con grupos de igual tamaño el MANOVA tolera diferencias moderadas."));

        var cells = CellMeans(y, factors, levels, texts, count);
        var cellRows = cells
            .Select(cell => (IReadOnlyList<string>)[.. cell.Labels, .. Enumerable.Range(0, count).Select(response => ModelHelpers.N(cell.Means[response]))])
            .ToList();
        sections.Add(new TableSection("Medias por celda", [.. factors, .. responses], cellRows));

        return Result.Ok(new AnalysisResult(
            "anova-manova",
            "MANOVA",
            sections,
            Subtitle: $"{count} variables de respuesta · factores: {string.Join(", ", factors)} · n = {n}",
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["wilks_p"] = wilks
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private sealed record Design(List<double[]> Columns, List<int[]> FactorColumns);

    private static Design BuildDesign(int n, string[] factors, string[][] levels, string?[][] texts)
    {
        var columns = new List<double[]> { Enumerable.Repeat(1.0, n).ToArray() };
        var blocks = new List<int[]>();
        for (var index = 0; index < factors.Length; index++)
        {
            var block = new int[levels[index].Length - 1];
            for (var level = 1; level < levels[index].Length; level++)
            {
                var values = new double[n];
                for (var row = 0; row < n; row++)
                {
                    values[row] = string.Equals(texts[index][row], levels[index][level], StringComparison.Ordinal) ? 1.0 : 0.0;
                }
                block[level - 1] = columns.Count;
                columns.Add(values);
            }
            blocks.Add(block);
        }
        return new Design(columns, blocks);
    }

    private static Result<double[][]> InverseCrossProduct(double[][] x)
    {
        var parameters = x[0].Length;
        var crossProduct = new double[parameters][];
        for (var i = 0; i < parameters; i++)
        {
            crossProduct[i] = new double[parameters];
            for (var j = 0; j < parameters; j++)
            {
                var sum = 0.0;
                for (var row = 0; row < x.Length; row++) sum += x[row][i] * x[row][j];
                crossProduct[i][j] = sum;
            }
        }
        return LinearAlgebra.Inverse(crossProduct);
    }

    /// <summary>Inversa de la raíz cuadrada de una matriz simétrica definida positiva.</summary>
    private static Result<double[][]> InverseSquareRoot(double[][] matrix)
    {
        var size = matrix.Length;
        var eigen = LinearAlgebra.EigenSymmetric(matrix);
        var maximum = eigen.Values.Select(Math.Abs).DefaultIfEmpty(0).Max();
        if (maximum <= 0) return Result.Err<double[][]>(DomainErrorCode.SingularMatrix, "La matriz de covarianza del error es singular.");
        var inverseRoot = new double[size];
        for (var index = 0; index < size; index++)
        {
            var value = eigen.Values[index];
            if (value <= maximum * 1e-12) return Result.Err<double[][]>(DomainErrorCode.SingularMatrix, "La matriz de covarianza del error es singular.");
            inverseRoot[index] = 1 / Math.Sqrt(value);
        }
        var output = new double[size][];
        for (var i = 0; i < size; i++)
        {
            output[i] = new double[size];
            for (var j = 0; j < size; j++)
            {
                var sum = 0.0;
                for (var k = 0; k < size; k++) sum += eigen.Vectors[i][k] * inverseRoot[k] * eigen.Vectors[j][k];
                output[i][j] = sum;
            }
        }
        return Result.Ok(output);
    }

    private static Result<double[][]> HypothesisMatrix(
        int[] factorColumns,
        double[][] beta,
        double[][] inverse,
        int count)
    {
        var size = factorColumns.Length;
        if (size == 0)
        {
            var zero = new double[count][];
            for (var i = 0; i < count; i++) zero[i] = new double[count];
            return Result.Ok(zero);
        }
        var contrast = new double[size][];
        for (var i = 0; i < size; i++)
        {
            contrast[i] = new double[count];
            for (var response = 0; response < count; response++) contrast[i][response] = beta[factorColumns[i]][response];
        }
        var middle = new double[size][];
        for (var i = 0; i < size; i++)
        {
            middle[i] = new double[size];
            for (var j = 0; j < size; j++) middle[i][j] = inverse[factorColumns[i]][factorColumns[j]];
        }
        var middleInverse = LinearAlgebra.Inverse(middle);
        if (middleInverse.IsError) return Result.Err<double[][]>(middleInverse.Error.Code, middleInverse.Error.Message);
        var hypothesis = new double[count][];
        for (var i = 0; i < count; i++) hypothesis[i] = new double[count];
        for (var a = 0; a < count; a++)
        {
            for (var b = 0; b < count; b++)
            {
                var sum = 0.0;
                for (var i = 0; i < size; i++)
                {
                    for (var j = 0; j < size; j++) sum += contrast[i][a] * middleInverse.Value[i][j] * contrast[j][b];
                }
                hypothesis[a][b] = sum;
            }
        }
        return Result.Ok(hypothesis);
    }

    /// <summary>Eigenvalores de E⁻¹H a partir de la raíz inversa de E.</summary>
    private static Result<double[]> Eigenvalues(double[][] errorInverseSquare, double[][] hypothesis)
    {
        var size = hypothesis.Length;
        var first = LinearAlgebra.Multiply(errorInverseSquare, hypothesis);
        var matrix = LinearAlgebra.Multiply(first, errorInverseSquare);
        var symmetric = new double[size][];
        for (var i = 0; i < size; i++)
        {
            symmetric[i] = new double[size];
            for (var j = 0; j < size; j++) symmetric[i][j] = (matrix[i][j] + matrix[j][i]) / 2;
        }
        var eigen = LinearAlgebra.EigenSymmetric(symmetric);
        var values = eigen.Values.Select(value => Math.Max(0, value)).OrderBy(value => value).ToArray();
        if (values.Any(double.IsNaN)) return Result.Err<double[]>(DomainErrorCode.SingularMatrix, "No se pudieron calcular los eigenvalores del MANOVA.");
        return Result.Ok(values);
    }

    private static MultivariateTest[] ComputeTests(double[] eigenvalues, int responses, int contrastDf, int residualDf)
    {
        const double tolerance = 1e-8;
        var filtered = eigenvalues.Where(value => value > tolerance).ToArray();
        if (filtered.Length == 0) filtered = [0];
        var p = responses;
        var q = contrastDf;
        var v = residualDf;
        var s = Math.Min(p, q);
        var m = (Math.Abs(p - q) - 1) / 2.0;
        var nn = (v - p - 1) / 2.0;

        var wilks = filtered.Aggregate(1.0, (current, value) => current * (1 / (1 + value)));
        var pillai = filtered.Sum(value => value / (1 + value));
        var hotelling = filtered.Sum();
        var roy = filtered.Max();

        var tests = new List<MultivariateTest>();

        var r = v - (p - q + 1) / 2.0;
        var u = (p * q - 2) / 4.0;
        var df1 = p * q;
        var t = p * p + q * q - 5 > 0 ? Math.Sqrt((p * p * q * q - 4.0) / (p * p + q * q - 5)) : 1.0;
        var df2 = r * t - 2 * u;
        var lambda = Math.Pow(wilks, 1 / t);
        var f = (1 - lambda) / lambda * df2 / df1;
        tests.Add(new MultivariateTest("Wilks' lambda", wilks, f, df1, df2, ExtendedStatistics.FSf(f, df1, df2)));

        var pillaiDf1 = s * (2 * m + s + 1);
        var pillaiDf2 = s * (2 * nn + s + 1);
        var pillaiF = pillaiDf2 / pillaiDf1 * pillai / (s - pillai);
        tests.Add(new MultivariateTest("Pillai's trace", pillai, pillaiF, pillaiDf1, pillaiDf2, ExtendedStatistics.FSf(pillaiF, pillaiDf1, pillaiDf2)));

        double hotellingDf1;
        double hotellingDf2;
        double hotellingF;
        if (nn > 0)
        {
            var b = (p + 2 * nn) * (q + 2 * nn) / 2 / (2 * nn + 1) / (nn - 1);
            hotellingDf1 = p * q;
            hotellingDf2 = 4 + (p * q + 2) / (b - 1);
            var c = (hotellingDf2 - 2) / 2 / nn;
            hotellingF = hotellingDf2 / hotellingDf1 * hotelling / c;
        }
        else
        {
            hotellingDf1 = s * (2 * m + s + 1);
            hotellingDf2 = s * (s * nn + 1);
            hotellingF = hotellingDf2 / hotellingDf1 / s * hotelling;
        }
        tests.Add(new MultivariateTest("Hotelling-Lawley trace", hotelling, hotellingF, hotellingDf1, hotellingDf2, ExtendedStatistics.FSf(hotellingF, hotellingDf1, hotellingDf2)));

        var royDf1 = Math.Max(p, q);
        var royDf2 = v - royDf1 + q;
        var royF = royDf2 / (double)royDf1 * roy;
        tests.Add(new MultivariateTest("Roy's greatest root", roy, royF, royDf1, royDf2, ExtendedStatistics.FSf(royF, royDf1, royDf2)));

        return [.. tests];
    }

    private static (double Statistic, int Df, double PValue) BoxM(
        double[][] y,
        string[] factors,
        string[][] levels,
        string?[][] texts,
        int n,
        int count)
    {
        var groups = new List<List<int>>();
        foreach (var labels in CartesianLabels(levels))
        {
            var rows = new List<int>();
            for (var row = 0; row < n; row++)
            {
                var inside = true;
                for (var index = 0; index < factors.Length; index++)
                {
                    if (!string.Equals(texts[index][row], labels[index], StringComparison.Ordinal)) inside = false;
                }
                if (inside) rows.Add(row);
            }
            if (rows.Count > 0) groups.Add(rows);
        }
        var k = groups.Count;
        if (k < 2 || groups.Any(rows => rows.Count <= count)) return (double.NaN, 0, double.NaN);

        var pooled = new double[count][];
        for (var i = 0; i < count; i++) pooled[i] = new double[count];
        var reciprocal = 0.0;
        foreach (var rows in groups)
        {
            var covariance = Covariance(y, [.. rows], count, rows.Count - 1);
            for (var i = 0; i < count; i++)
            {
                for (var j = 0; j < count; j++) pooled[i][j] += (rows.Count - 1) * covariance[i][j];
            }
            reciprocal += 1.0 / (rows.Count - 1);
        }
        for (var i = 0; i < count; i++)
        {
            for (var j = 0; j < count; j++) pooled[i][j] /= n - k;
        }
        var pooledDeterminant = LinearAlgebra.Determinant(pooled);
        if (pooledDeterminant <= 0) return (double.NaN, 0, double.NaN);
        var statistic = (n - k) * Math.Log(pooledDeterminant);
        foreach (var rows in groups)
        {
            var covariance = Covariance(y, [.. rows], count, rows.Count - 1);
            var determinant = LinearAlgebra.Determinant(covariance);
            if (determinant <= 0) return (double.NaN, 0, double.NaN);
            statistic -= (rows.Count - 1) * Math.Log(determinant);
        }
        reciprocal -= 1.0 / (n - k);
        var correction = reciprocal * (2.0 * count * count + 3.0 * count - 1) / (6.0 * (count + 1) * (k - 1));
        var chiSquare = statistic * (1 - correction);
        var df = count * (count + 1) * (k - 1) / 2;
        return (chiSquare, df, ExtendedStatistics.ChiSquareSf(chiSquare, df));
    }

    private static double[][] Covariance(double[][] y, int[] rows, int count, int divisor)
    {
        var means = new double[count];
        for (var response = 0; response < count; response++)
        {
            var sum = 0.0;
            foreach (var row in rows) sum += y[row][response];
            means[response] = sum / rows.Length;
        }
        var covariance = new double[count][];
        for (var i = 0; i < count; i++) covariance[i] = new double[count];
        foreach (var row in rows)
        {
            for (var i = 0; i < count; i++)
            {
                for (var j = 0; j < count; j++)
                {
                    covariance[i][j] += (y[row][i] - means[i]) * (y[row][j] - means[j]);
                }
            }
        }
        for (var i = 0; i < count; i++)
        {
            for (var j = 0; j < count; j++) covariance[i][j] /= divisor;
        }
        return covariance;
    }

    private sealed record Cell(string[] Labels, int Count, double[] Means);

    private static List<Cell> CellMeans(double[][] y, string[] factors, string[][] levels, string?[][] texts, int count)
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
            if (rows.Count == 0) continue;
            var means = new double[count];
            for (var response = 0; response < count; response++)
            {
                means[response] = rows.Average(row => y[row][response]);
            }
            cells.Add(new Cell(labels, rows.Count, means));
        }
        return cells;
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
}
