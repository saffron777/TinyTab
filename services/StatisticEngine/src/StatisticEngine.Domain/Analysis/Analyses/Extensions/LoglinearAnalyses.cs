using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Modelo loglineal de una tabla de contingencia (port de <c>models_advanced._loglinear</c>).</summary>
internal static class LoglinearAnalyses
{
    public static readonly AnalysisDefinition Loglinear = new(
        "regression-loglinear",
        "Modelo loglineal",
        "Regresión",
        "Modela la frecuencia de cada celda de una tabla de contingencia y examina la asociación entre los factores.",
        [],
        [],
        [new("interactions", "Incluir interacciones de dos factores", AnalysisParamKinds.Boolean, false, [], false)],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Tablas de contingencia",
        Roles:
        [
            new ColumnRole("count", "Frecuencia (conteo)", ColumnKinds.Numeric),
            new ColumnRole("factors", "Factores (variables categóricas)", ColumnKinds.Categorical, Multiple: true, MinCount: 1, MaxCount: 6)
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var countColumn = ExtendedHelpers.FirstRole(input, "count", "Frecuencia (conteo)");
        var factors = input.RoleNames("factors", true);
        if (factors.Count == 0) return ExtendedHelpers.Invalid("Elija al menos un factor.");

        var names = new List<string> { countColumn };
        names.AddRange(factors);
        var complete = frame.CompleteCases(names);
        if (complete.RowCount < 4)
        {
            return ExtendedHelpers.Invalid("Se necesitan al menos 4 filas para el modelo loglineal.");
        }

        var counts = complete.Numeric(countColumn);
        var texts = factors.Select(complete.Texts).ToArray();
        var buckets = new Dictionary<string, double>(StringComparer.Ordinal);
        var labels = new Dictionary<string, string[]>(StringComparer.Ordinal);
        for (var row = 0; row < complete.RowCount; row++)
        {
            var key = string.Join('\u0001', texts.Select(column => column[row] ?? string.Empty));
            var values = new string[factors.Count];
            for (var index = 0; index < factors.Count; index++) values[index] = texts[index][row] ?? string.Empty;
            labels[key] = values;
            buckets.TryGetValue(key, out var current);
            buckets[key] = current + counts[row];
        }

        var orderedKeys = buckets.Keys
            .OrderBy(key => labels[key], Comparer<string[]>.Create(CompareLabels))
            .ToArray();
        var aggregateColumns = new List<DataColumn>
        {
            DataColumn.Numeric("count", orderedKeys.Select(key => buckets[key]).ToArray())
        };
        for (var index = 0; index < factors.Count; index++)
        {
            var columnIndex = index;
            aggregateColumns.Add(DataColumn.Text(factors[index], orderedKeys.Select(key => labels[key][columnIndex]).ToArray()));
        }
        var aggregate = new Frame(aggregateColumns, orderedKeys.Length);

        var designResult = DesignBuilder.Build(
            aggregate,
            "count",
            factors,
            interactions: input.Pbool("interactions") ? "two" : "none",
            minRows: 1);
        if (designResult.IsError) return Result.Err<AnalysisResult>(designResult.Error.Code, designResult.Error.Message);
        var design = designResult.Value;
        var fitResult = GlmModel.Fit(design.Matrix, design.Response, GlmFamily.Poisson, GlmLink.Log);
        if (fitResult.IsError) return Result.Err<AnalysisResult>(fitResult.Error.Code, fitResult.Error.Message);
        var fit = fitResult.Value;
        var cells = aggregate.RowCount;

        var sections = new List<AnalysisSection>
        {
            new TilesSection("Resumen",
            [
                new TileItem("Celdas", JsNumber.ToString(cells)),
                new TileItem("Desvianza", ModelHelpers.N(fit.Deviance)),
                new TileItem("AIC", ModelHelpers.N(fit.Aic)),
                new TileItem("BIC", ModelHelpers.N(-2 * fit.LogLikelihood + fit.Parameters * Math.Log(cells)))
            ])
        };

        var rows = new List<IReadOnlyList<string>>();
        for (var index = 0; index < fit.Coefficients.Length; index++)
        {
            var name = design.ColumnNames[index] == "Intercept" ? "Constante" : design.Pretty(design.ColumnNames[index]);
            rows.Add([
                name,
                ModelHelpers.N(fit.Coefficients[index]),
                ModelHelpers.N(fit.StandardErrors[index]),
                ModelHelpers.N(fit.ZValues[index]),
                ModelHelpers.P(fit.PValues[index]),
                ModelHelpers.N(Math.Exp(fit.Coefficients[index]))
            ]);
        }
        sections.Add(new TableSection(
            "Parámetros (efectos sobre el logaritmo del conteo)",
            ["Término", "Coeficiente", "Error est.", "z", "Valor p", "Exp(coef.)"],
            rows,
            Note: "Exp(coef.) es el factor multiplicativo del conteo por cada efecto."));
        sections.Add(new AlertSection(
            "Aviso",
            "info",
            "El modelo loglineal describe la estructura de asociación de la tabla; la desvianza residual mide el ajuste."));

        return Result.Ok(new AnalysisResult(
            "regression-loglinear",
            "Modelo loglineal (tabla de contingencia)",
            sections,
            Subtitle: $"{cells} celdas · {factors.Count} factor(es)",
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["cells"] = cells,
                ["deviance"] = fit.Deviance,
                ["aic"] = fit.Aic,
                ["bic"] = -2 * fit.LogLikelihood + fit.Parameters * Math.Log(cells)
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static int CompareLabels(string[] left, string[] right)
    {
        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            var comparison = string.Compare(left[index], right[index], StringComparison.Ordinal);
            if (comparison != 0) return comparison;
        }
        return left.Length.CompareTo(right.Length);
    }
}
