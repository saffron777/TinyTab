using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Optimización de asignación con presupuesto (programación lineal continua).</summary>
internal static class OptimizationAnalyses
{
    public static readonly AnalysisDefinition LinearProgramming = new(
        "lp",
        "Programación lineal / entera (asignación con presupuesto)",
        "Optimización",
        "Optimiza la asignación de recursos: maximiza o minimiza el valor sujeto a un presupuesto y límites por elemento.",
        [],
        [],
        [
            new(
                "sense",
                "Sentido",
                AnalysisParamKinds.Select,
                "maximize",
                [new("maximize", "Maximizar"), new("minimize", "Minimizar")],
                false),
            new("budget", "Presupuesto (recurso disponible)", AnalysisParamKinds.Number, 10.0, [], false, Minimum: 0),
            new("integer", "Variables enteras (0/1 con límite 1)", AnalysisParamKinds.Boolean, false, [], false)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Programación lineal",
        Roles:
        [
            new ColumnRole("value", "Coeficiente del objetivo (por unidad)", ColumnKinds.Numeric),
            new ColumnRole("cost", "Uso de recurso por unidad (opcional)", ColumnKinds.Numeric, Optional: true),
            new ColumnRole("bound", "Límite superior por elemento (opcional)", ColumnKinds.Numeric, Optional: true)
        ],
        Outputs: [new AnalysisOutputSpec("solucion", "Asignación óptima")]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        if (input.Pbool("integer"))
        {
            return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "La optimización entera aún no está disponible; use variables continuas.");
        }
        var frame = input.AsFrame();
        var valueName = ExtendedHelpers.FirstRole(input, "value", "Coeficiente del objetivo (por unidad)");
        var costName = ExtendedHelpers.FirstRole(input, "cost", "Uso de recurso por unidad");
        var boundName = ExtendedHelpers.FirstRole(input, "bound", "Límite superior por elemento");
        var n = frame.RowCount;
        if (n < 1) return ExtendedHelpers.Invalid("Se necesita al menos una fila (un elemento).");
        var value = frame.Numeric(valueName);
        var cost = costName.Length > 0 ? frame.Numeric(costName) : Enumerable.Repeat(1.0, n).ToArray();
        var bound = boundName.Length > 0 ? frame.Numeric(boundName) : Enumerable.Repeat(1.0, n).ToArray();
        if (value.Any(double.IsNaN)) return ExtendedHelpers.Invalid("El coeficiente del objetivo tiene valores no numéricos o vacíos.");
        if (cost.Any(valueCost => double.IsNaN(valueCost) || valueCost < 0)) return ExtendedHelpers.Invalid("El uso de recurso debe ser un número no negativo.");
        if (bound.Any(limit => double.IsNaN(limit) || limit < 0)) return ExtendedHelpers.Invalid("El límite superior debe ser un número no negativo.");
        var budget = input.Pnum("budget", 10);
        var maximize = input.Pstr("sense", "maximize") == "maximize";
        var x = new double[n];
        if (maximize)
        {
            var order = Enumerable.Range(0, n)
                .OrderByDescending(index => cost[index] > 0 ? value[index] / cost[index] : double.PositiveInfinity)
                .ToArray();
            var remaining = budget;
            foreach (var index in order)
            {
                if (remaining <= 0) break;
                if (cost[index] <= 0)
                {
                    x[index] = bound[index];
                    continue;
                }
                var quantity = Math.Min(bound[index], remaining / cost[index]);
                x[index] = quantity;
                remaining -= quantity * cost[index];
            }
        }
        else
        {
            var order = Enumerable.Range(0, n).Where(index => value[index] < 0)
                .OrderBy(index => cost[index] > 0 ? value[index] / cost[index] : double.NegativeInfinity)
                .ToArray();
            var remaining = budget;
            foreach (var index in order)
            {
                if (remaining <= 0) break;
                var quantity = cost[index] > 0 ? Math.Min(bound[index], remaining / cost[index]) : bound[index];
                x[index] = quantity;
                remaining -= quantity * cost[index];
            }
        }
        var objective = value.Select((coefficient, index) => coefficient * x[index]).Sum();
        var used = cost.Select((coefficient, index) => coefficient * x[index]).Sum();
        var selected = x.Count(quantity => quantity > 1e-9);
        var rows = Enumerable.Range(0, n).Select(index => (IReadOnlyList<string>)new[]
        {
            JsNumber.ToString(index + 1),
            ModelHelpers.N(value[index]),
            ModelHelpers.N(cost[index]),
            ModelHelpers.N(x[index])
        }).ToArray();
        return Result.Ok(new AnalysisResult(
            "lp",
            "Optimización de recursos",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Valor objetivo", ModelHelpers.N(objective)),
                    new TileItem("Recurso usado", ModelHelpers.N(used)),
                    new TileItem("Elementos con x > 0", JsNumber.ToString(selected))
                ]),
                new AlertSection(
                    "Conclusión",
                    double.IsFinite(objective) ? "success" : "warning",
                    $"Solución continua óptima: objetivo = {objective.ToString("G4", System.Globalization.CultureInfo.InvariantCulture)}, usando {used.ToString("G4", System.Globalization.CultureInfo.InvariantCulture)} de {SigmaText.General(budget)} del recurso."),
                new TableSection(
                    "Solución",
                    ["Elemento", "Valor por unidad", "Recurso por unidad", "Cantidad óptima (x)"],
                    rows,
                    Note: "x es la cantidad a asignar a cada elemento; se muestra la solución óptima.")
            ],
            Subtitle: $"{n} elementos · {(maximize ? "maximizar" : "minimizar")} · presupuesto {SigmaText.General(budget)}",
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
            {
                ["solucion"] = new DatasetResult(
                    "Asignación óptima",
                    [
                        DataColumn.Numeric("elemento", Enumerable.Range(1, n).Select(index => (double)index).ToArray()),
                        DataColumn.Numeric("x", x)
                    ])
            },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["objective"] = objective,
                ["resource_used"] = used,
                ["n_selected"] = selected
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
