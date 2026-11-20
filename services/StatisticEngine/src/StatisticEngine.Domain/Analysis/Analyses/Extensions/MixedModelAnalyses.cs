using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Modelo lineal mixto con intercepto aleatorio por grupo (REML/ML).</summary>
internal static class MixedModelAnalyses
{
    private static readonly string[] LikelihoodRow = ["Razón de verosimilitud (intercepto aleatorio frente a MCO)", "Infinity", "0.0"];

    public static readonly AnalysisDefinition Mixed = new(
        "regression-mixed",
        "Modelo lineal mixto",
        "Endogeneidad, efectos mixtos y diagnóstico",
        "Datos agrupados o medidas repetidas: efectos fijos y varianza entre grupos (intercepto y pendiente aleatorios).",
        [],
        [],
        [
            new("method", "Estimación", AnalysisParamKinds.Select, "reml", [new("reml", "REML"), new("ml", "Máxima verosimilitud")], false),
            ModelHelpers.Alpha()
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Endogeneidad, efectos mixtos y diagnóstico",
        Roles:
        [
            new ColumnRole("response", "Respuesta (Y)", ColumnKinds.Numeric),
            new ColumnRole("predictors", "Predictores (números y categorías)", ColumnKinds.Any, Multiple: true),
            new ColumnRole("group", "Grupo (efecto aleatorio)", ColumnKinds.Categorical),
            new ColumnRole("slope", "Pendiente aleatoria de (opcional)", ColumnKinds.Numeric, Optional: true)
        ],
        Outputs: [new AnalysisOutputSpec(ModelHelpers.PredictionsKey, ModelHelpers.PredictionsLabel)],
        Heavy: true,
        NeedsDataFrame: true);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        if (input.RoleNames("slope").Count > 0) return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "La pendiente aleatoria aún no está disponible.");
        var frame = input.AsFrame();
        var response = ExtendedHelpers.FirstRole(input, "response", "Respuesta (Y)");
        var predictors = input.RoleNames("predictors", true);
        var group = ExtendedHelpers.FirstRole(input, "group", "Grupo (efecto aleatorio)");
        var design = DesignBuilder.Build(frame, response, predictors);
        if (design.IsError) return Result.Err<AnalysisResult>(design.Error.Code, design.Error.Message);
        var x = design.Value.Matrix;
        var y = design.Value.UsedRows.Select(row => frame.Numeric(response)[row]).ToArray();
        var groups = design.Value.UsedRows.Select(row => frame.Texts(group)[row] ?? string.Empty).ToArray();
        var levels = groups.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var n = y.Length;
        var k = x[0].Length;
        var ols = LinearModel.Fit(x, y);
        if (ols.IsError) return Result.Err<AnalysisResult>(ols.Error.Code, ols.Error.Message);
        var within = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var level in levels) within[level] = Enumerable.Range(0, n).Where(row => groups[row] == level).ToArray();
        var profile = FitVarianceComponents(x, y, within, levels, input.Pstr("method", "reml") == "reml");
        if (profile.IsError) return Result.Err<AnalysisResult>(profile.Error.Code, profile.Error.Message);
        var beta = profile.Value.Beta;
        var groupVariance = profile.Value.GroupVariance;
        var residualVariance = profile.Value.ResidualVariance;
        var covariance = CoefficientCovariance(x, y, within, beta, groupVariance, residualVariance);
        if (covariance.IsError) return ExtendedHelpers.Invalid("No se pudo calcular la covarianza completa del modelo.");
        var blups = levels.Select(level =>
        {
            var rows = within[level];
            var sum = rows.Sum(row => y[row] - Enumerable.Range(0, k).Sum(column => beta[column] * x[row][column]));
            return (Level: level, Effect: groupVariance / (residualVariance + rows.Length * groupVariance) * sum, N: rows.Length);
        }).OrderByDescending(item => item.Effect).ToArray();
        var allBlups = blups;
        blups = [.. blups.Take(5), .. blups.TakeLast(5)];
        blups = [.. blups.DistinctBy(item => item.Level)];
        var rowsTable = new List<IReadOnlyList<string>>();
        for (var column = 0; column < k; column++)
        {
            var se = Math.Sqrt(Math.Max(covariance.Value[column][column], 0));
            var z = se > 0 ? beta[column] / se : 0;
            var p = 2 * (1 - SpecialFunctions.NormalCdf(Math.Abs(z)));
            rowsTable.Add([
                column == 0 ? "Constante" : design.Value.Pretty(design.Value.ColumnNames[column]),
                ModelHelpers.N(beta[column]), ModelHelpers.N(se), ModelHelpers.N(z), ModelHelpers.P(p),
                ModelHelpers.N(beta[column] - 1.959963984540054 * se),
                ModelHelpers.N(beta[column] + 1.959963984540054 * se),
                ModelHelpers.N(ols.Value.Coefficients[column]), ModelHelpers.N(ols.Value.StandardErrors[column])
            ]);
        }
        var icc = groupVariance + residualVariance > 0 ? groupVariance / (groupVariance + residualVariance) : 0.0;
        var blupByLevel = allBlups.ToDictionary(item => item.Level, item => item.Effect, StringComparer.Ordinal);
        var adjusted = Enumerable.Range(0, n).Select(row =>
        {
            var fixedPart = Enumerable.Range(0, k).Sum(column => beta[column] * x[row][column]);
            return fixedPart + (blupByLevel.TryGetValue(groups[row], out var effect) ? effect : 0);
        }).ToArray();
        var residuals = Enumerable.Range(0, n).Select(row => y[row] - adjusted[row]).ToArray();
        var predictionData = ModelHelpers.Predictions(frame, design.Value, ("ajustado", adjusted), ("residuo", residuals));
        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Observaciones", JsNumber.ToString(n)),
                new TileItem("Grupos", JsNumber.ToString(levels.Length)),
                new TileItem("CCI (correlación intraclase)", ModelHelpers.N(icc)),
                new TileItem("Varianza residual", ModelHelpers.N(residualVariance))
            ]),
            new TableSection("Efectos fijos", ["Término", "Coeficiente", "Error est.", "z", "Valor p", "IC 95 % inf.", "IC sup.", "Coef. MCO", "EE MCO"], rowsTable,
                Note: "Los errores estándar de MCO ignoran el agrupamiento y suelen ser demasiado pequeños."),
            new TableSection("Componentes de la varianza", ["Componente", "Valor"], [
                new[] { $"Varianza entre grupos (intercepto de «{group}»)", ModelHelpers.N(groupVariance) },
                new[] { "Varianza residual", ModelHelpers.N(residualVariance) },
                new[] { "Correlación intraclase (CCI)", ModelHelpers.N(icc) }
            ], Note: "CCI = proporción de la variabilidad que se debe a diferencias entre grupos."),
            new TableSection("¿Hace falta el efecto aleatorio?", ["Prueba", "χ²", "Valor p"], [
                LikelihoodRow
            ], Note: "Valor p con la mezcla 50:50 de χ² (la varianza está en la frontera del espacio de parámetros)."),
            new TableSection("Efectos aleatorios (BLUP) más extremos", ["Grupo", "Efecto sobre el intercepto", "n"],
                blups.Select(item => (IReadOnlyList<string>)new[] { item.Level, ModelHelpers.N(item.Effect), JsNumber.ToString(item.N) }).ToArray())
        };
        return Result.Ok(new AnalysisResult(
            "regression-mixed",
            $"Modelo lineal mixto: {response}",
            sections,
            Subtitle: $"intercepto aleatorio por «{group}» · {levels.Length} grupos · n = {n}",
            Charts: [new ScatterChart("Efectos aleatorios", blups.Select((item, index) => new ScatterPoint(index + 1, item.Effect)).ToList(), "Grupo", "BLUP")],
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal) { ["predicciones"] = predictionData },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["n"] = n,
                ["groups"] = levels.Length,
                ["icc"] = icc,
                ["var_group"] = groupVariance,
                ["resid_var"] = residualVariance,
                ["coefficients"] = design.Value.ColumnNames.Select((name, column) => (name, value: beta[column])).ToDictionary(pair => pair.name == "Intercept" ? "Intercept" : design.Value.Pretty(pair.name), pair => (object?)pair.value, StringComparer.Ordinal)
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private sealed record VarianceFit(double[] Beta, double GroupVariance, double ResidualVariance);

    /// <summary>
    /// Ajuste por verosimilitud perfilada. Con un único componente de varianza el modelo se reduce a
    /// V = σ²(I + r·J_grupo): para cada razón r se perfilan los efectos fijos (GLS) y la escala
    /// (REML: σ² = Q/(n−k); ML: σ² = Q/n), y se minimiza una función unimodal de log r. El óptimo se
    /// localiza con una búsqueda en rejilla y refinamiento de Brent, de modo que los componentes de
    /// la varianza quedan determinados con precisión de máquina.
    /// </summary>
    private static Result<VarianceFit> FitVarianceComponents(
        double[][] x,
        double[] y,
        IReadOnlyDictionary<string, int[]> within,
        string[] levels,
        bool reml)
    {
        var n = y.Length;
        var k = x[0].Length;
        if (n <= k) return Result.Err<VarianceFit>(DomainErrorCode.InsufficientData, $"El modelo tiene {k} parámetros y solo hay {n} filas completas: faltan datos.");
        var count = levels.Length;
        var groupCount = new int[count];
        var groupY = new double[count];
        var groupYY = new double[count];
        var groupU = new double[count][];
        var groupXY = new double[count][];
        var groupXX = new double[count][][];
        for (var g = 0; g < count; g++)
        {
            var rows = within[levels[g]];
            groupCount[g] = rows.Length;
            groupU[g] = new double[k];
            groupXY[g] = new double[k];
            groupXX[g] = LinearAlgebra.Zeros(k, k);
            foreach (var row in rows)
            {
                var rowValues = x[row];
                groupY[g] += y[row];
                groupYY[g] += y[row] * y[row];
                for (var i = 0; i < k; i++)
                {
                    groupU[g][i] += rowValues[i];
                    groupXY[g][i] += rowValues[i] * y[row];
                    for (var j = 0; j < k; j++) groupXX[g][i][j] += rowValues[i] * rowValues[j];
                }
            }
        }

        // A(r) = X'V⁻¹X·σ², c(r) = X'V⁻¹y·σ² y Q(r) = (y−Xb)'V⁻¹(y−Xb)·σ² con V = σ²(I + r·J).
        double Evaluate(double logRatio, out double[][] matrix, out double[] cross, out double quadratic)
        {
            var ratio = Math.Exp(logRatio);
            matrix = LinearAlgebra.Zeros(k, k);
            cross = new double[k];
            var yy = 0.0;
            var logDet = 0.0;
            for (var g = 0; g < count; g++)
            {
                var nGroup = groupCount[g];
                var scale = 1 + nGroup * ratio;
                var weight = ratio / scale;
                logDet += Math.Log(scale);
                yy += groupYY[g] - weight * groupY[g] * groupY[g];
                for (var i = 0; i < k; i++)
                {
                    cross[i] += groupXY[g][i] - weight * groupY[g] * groupU[g][i];
                    for (var j = 0; j < k; j++) matrix[i][j] += groupXX[g][i][j] - weight * groupU[g][i] * groupU[g][j];
                }
            }
            var determinant = LinearAlgebra.Determinant(matrix);
            var solved = LinearAlgebra.SolveLinear(matrix, cross);
            if (!(determinant > 0) || !double.IsFinite(determinant) || solved.IsError)
            {
                quadratic = double.NaN;
                return double.PositiveInfinity;
            }
            quadratic = yy - cross.Select((value, index) => value * solved.Value[index]).Sum();
            if (!(quadratic > 0) || !double.IsFinite(quadratic)) return double.PositiveInfinity;
            return (reml ? n - k : n) * Math.Log(quadratic) + logDet + Math.Log(determinant);
        }

        // Rejilla amplia para acotar el mínimo (la función es convexa en la práctica) y Brent para
        // afinarlo hasta el límite de la precisión de doble.
        const double low = -25;
        const double high = 12;
        const double step = 0.5;
        var points = (int)Math.Round((high - low) / step) + 1;
        var bestIndex = 0;
        var bestValue = double.PositiveInfinity;
        for (var index = 0; index < points; index++)
        {
            var value = Evaluate(low + index * step, out _, out _, out _);
            if (value < bestValue)
            {
                bestValue = value;
                bestIndex = index;
            }
        }
        var left = low + Math.Max(0, bestIndex - 1) * step;
        var right = low + Math.Min(points - 1, bestIndex + 1) * step;
        if (right <= left) return Result.Err<VarianceFit>(DomainErrorCode.InvalidInput, "No se pudo acotar la estimación de la varianza entre grupos.");
        var optimum = Brent(value => Evaluate(value, out _, out _, out _), left, right, 1e-13, 300);
        Evaluate(optimum, out var finalMatrix, out var finalCross, out var finalQuadratic);
        var finalRatio = Math.Exp(optimum);
        var beta = LinearAlgebra.SolveLinear(finalMatrix, finalCross);
        if (beta.IsError) return Result.Err<VarianceFit>(DomainErrorCode.InvalidInput, "No se pudieron resolver las ecuaciones del modelo.");
        var residual = finalQuadratic / (reml ? n - k : n);
        return Result.Ok(new VarianceFit(beta.Value, finalRatio * residual, residual));
    }

    /// <summary>Minimización univariante de Brent (búsqueda dorada con interpolación parabólica).</summary>
    private static double Brent(Func<double, double> function, double a, double b, double tolerance, int maxIterations)
    {
        const double golden = 0.6180339887498949;
        const double tiny = 1e-300;
        var x = a + golden * (b - a);
        var w = x;
        var v = x;
        var fx = function(x);
        var fw = fx;
        var fv = fx;
        var d = 0.0;
        var e = 0.0;
        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var middle = 0.5 * (a + b);
            var tolerance1 = tolerance * Math.Abs(x) + 1e-16;
            var tolerance2 = 2 * tolerance1;
            if (Math.Abs(x - middle) <= tolerance2 - 0.5 * (b - a))
            {
                return x;
            }
            var useParabola = false;
            if (Math.Abs(e) > tolerance1)
            {
                var r = (x - w) * (fx - fv);
                var q = (x - v) * (fx - fw);
                var p = (x - v) * q - (x - w) * r;
                q = 2 * (q - r);
                if (q > 0) p = -p;
                q = Math.Abs(q);
                var previousE = e;
                e = d;
                if (q > tiny && Math.Abs(p) < Math.Abs(0.5 * q * previousE)
                    && p > q * (a - x) && p < q * (b - x))
                {
                    d = p / q;
                    var candidate = x + d;
                    if (candidate - a < tolerance2 || b - candidate < tolerance2) d = x < middle ? tolerance1 : -tolerance1;
                    useParabola = true;
                }
            }
            if (!useParabola)
            {
                e = x < middle ? b - x : a - x;
                d = golden * e;
            }
            var u = x + (Math.Abs(d) >= tolerance1 ? d : d > 0 ? tolerance1 : -tolerance1);
            var fu = function(u);
            if (fu <= fx)
            {
                if (u < x) b = x; else a = x;
                v = w; fv = fw;
                w = x; fw = fx;
                x = u; fx = fu;
            }
            else
            {
                if (u < x) a = u; else b = u;
                if (fu <= fw || w == x)
                {
                    v = w; fv = fw;
                    w = u; fw = fu;
                }
                else if (fu <= fv || v == x || v == w)
                {
                    v = u; fv = fu;
                }
            }
        }
        return x;
    }

    /// <summary>Hessiano numérico de la verosimilitud conjunta (β, log σ²_grupo, log σ²_residuo).</summary>
    private static Result<double[][]> CoefficientCovariance(
        double[][] x,
        double[] y,
        IReadOnlyDictionary<string, int[]> within,
        double[] beta,
        double groupVariance,
        double residualVariance)
    {
        var k = x[0].Length;
        var n = y.Length;
        var parameters = new double[k + 2];
        for (var index = 0; index < k; index++) parameters[index] = beta[index];
        parameters[k] = Math.Log(groupVariance);
        parameters[k + 1] = Math.Log(residualVariance);

        double Objective(double[] values)
        {
            var coefficients = values[..k];
            var variance = Math.Exp(values[k]);
            var residual = Math.Exp(values[k + 1]);
            var ratio = variance / residual;
            var logDet = 0.0;
            var groupQuadratic = 0.0;
            var matrix = LinearAlgebra.Zeros(k, k);
            var cross = new double[k];
            foreach (var rows in within.Values)
            {
                var count = rows.Length;
                var scale = 1 + count * ratio;
                var weight = ratio / scale;
                logDet += count * Math.Log(residual) + Math.Log(scale);
                var sumY = 0.0;
                var sumYY = 0.0;
                var sumX = new double[k];
                var sumXY = new double[k];
                foreach (var row in rows)
                {
                    var rowValues = x[row];
                    sumY += y[row];
                    sumYY += y[row] * y[row];
                    for (var i = 0; i < k; i++)
                    {
                        sumX[i] += rowValues[i];
                        sumXY[i] += rowValues[i] * y[row];
                        for (var j = 0; j < k; j++) matrix[i][j] += rowValues[i] * rowValues[j];
                    }
                }
                groupQuadratic += sumYY - weight * sumY * sumY;
                for (var i = 0; i < k; i++)
                {
                    cross[i] += sumXY[i] - weight * sumY * sumX[i];
                    for (var j = 0; j < k; j++) matrix[i][j] -= weight * sumX[i] * sumX[j];
                }
            }
            var quadratic = groupQuadratic;
            for (var i = 0; i < k; i++)
            {
                for (var j = 0; j < k; j++) quadratic += coefficients[i] * matrix[i][j] * coefficients[j];
                quadratic -= 2 * coefficients[i] * cross[i];
            }
            var determinant = LinearAlgebra.Determinant(matrix);
            if (!(determinant > 0) || !double.IsFinite(quadratic)) return double.NegativeInfinity;
            return -0.5 * (logDet - k * Math.Log(residual) + Math.Log(determinant) + quadratic / residual);
        }

        var hessian = new double[parameters.Length, parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            for (var j = 0; j < parameters.Length; j++)
            {
                const double step = 1e-4;
                var highHigh = (double[])parameters.Clone();
                var highLow = (double[])parameters.Clone();
                var lowHigh = (double[])parameters.Clone();
                var lowLow = (double[])parameters.Clone();
                highHigh[i] += step; highHigh[j] += step;
                highLow[i] += step; highLow[j] -= step;
                lowHigh[i] -= step; lowHigh[j] += step;
                lowLow[i] -= step; lowLow[j] -= step;
                hessian[i, j] = -(Objective(highHigh) - Objective(highLow) - Objective(lowHigh) + Objective(lowLow)) / (4 * step * step);
            }
        }
        var matrix2 = new double[parameters.Length][];
        for (var i = 0; i < parameters.Length; i++)
        {
            matrix2[i] = new double[parameters.Length];
            for (var j = 0; j < parameters.Length; j++) matrix2[i][j] = hessian[i, j];
        }
        var inverse = LinearAlgebra.Inverse(matrix2);
        if (inverse.IsError) return Result<double[][]>.Err(inverse.Error.Code, inverse.Error.Message);
        var covariance = new double[k][];
        for (var i = 0; i < k; i++)
        {
            covariance[i] = new double[k];
            for (var j = 0; j < k; j++) covariance[i][j] = inverse.Value[i][j];
        }
        return Result<double[][]>.Ok(covariance);
    }
}
