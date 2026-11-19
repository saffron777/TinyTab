using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Diseños factoriales y de superficie de respuesta.</summary>
internal static class DoeAnalyses
{
    public static readonly AnalysisDefinition Factorial = new(
        "factorial",
        "Factorial completo 2^k",
        "DOE factorial",
        "Genera un diseño factorial 2^k (con réplicas centrales opcionales) y, si hay respuesta, ajusta los efectos principales e interacciones.",
        [],
        [],
        [
            new("factors", "Factores (2 a 7)", AnalysisParamKinds.Integer, 3.0, [], false, Minimum: 2, Maximum: 7),
            new("center_points", "Réplicas en el centro", AnalysisParamKinds.Integer, 0.0, [], false, Minimum: 0, Maximum: 10),
            ModelHelpers.Alpha()
        ],
        RunFactorial,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Diseños factoriales",
        Roles:
        [
            new ColumnRole("response", "Respuesta (opcional)", ColumnKinds.Numeric, Optional: true)
        ],
        Outputs: [new AnalysisOutputSpec("diseño", "Diseño factorial")]);

    public static readonly AnalysisDefinition ResponseSurface = new(
        "response-surface",
        "Superficie de respuesta (compuesto central)",
        "DOE superficie de respuesta",
        "Genera un diseño compuesto central y, si hay respuesta, ajusta el modelo cuadrático con interacciones.",
        [],
        [],
        [
            new("factors", "Factores (2 a 7)", AnalysisParamKinds.Integer, 3.0, [], false, Minimum: 2, Maximum: 7),
            new("axial", "Distancia axial α (vacío = rotable 2^(k/4))", AnalysisParamKinds.Number, null, [], true, Minimum: 0.5, Maximum: 3),
            new("center_points", "Réplicas en el centro", AnalysisParamKinds.Integer, 0.0, [], false, Minimum: 0, Maximum: 10),
            ModelHelpers.Alpha()
        ],
        RunResponseSurface,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Superficie de respuesta",
        Roles:
        [
            new ColumnRole("response", "Respuesta (opcional)", ColumnKinds.Numeric, Optional: true)
        ],
        Outputs: [new AnalysisOutputSpec("diseño", "Diseño compuesto central")]);

    private static Result<AnalysisResult> RunFactorial(AnalysisInput input)
    {
        if (input.RoleNames("response").Count > 0)
        {
            return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "El ajuste de la respuesta del factorial aún no está disponible; genere solo el diseño.");
        }
        var factors = input.Pint("factors", 3);
        var centers = input.Pint("center_points", 0);
        var runs = (int)Math.Pow(2, factors);
        var columns = Enumerable.Range(0, factors).Select(factor =>
        {
            var values = new double[runs + centers];
            for (var row = 0; row < runs; row++) values[row] = ((row >> (factors - 1 - factor)) & 1) == 0 ? -1 : 1;
            return DataColumn.Numeric(((char)('A' + factor)).ToString(), values);
        }).ToArray();
        return Result.Ok(new AnalysisResult(
            "factorial",
            "Diseño factorial 2^k",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Factores", JsNumber.ToString(factors)),
                    new TileItem("Corridas", JsNumber.ToString(runs + centers)),
                    new TileItem("Réplicas en el centro", JsNumber.ToString(centers))
                ]),
                new AlertSection(
                    "Aviso",
                    "info",
                    "Diseño codificado en −1 (nivel bajo) y +1 (nivel alto); guárdelo como dataset y añada la columna de respuesta al ejecutar los experimentos.")
            ],
            Subtitle: $"{runs + centers} corridas · {factors} factores a dos niveles",
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
            {
                ["diseño"] = new DatasetResult("Diseño factorial", columns)
            },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["factors"] = factors,
                ["runs"] = runs + centers
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private static Result<AnalysisResult> RunResponseSurface(AnalysisInput input)
    {
        if (input.RoleNames("response").Count > 0)
        {
            return ExtendedHelpers.Error(DomainErrorCode.NotSupported, "El ajuste de la respuesta del diseño compuesto aún no está disponible; genere solo el diseño.");
        }
        var factors = input.Pint("factors", 2);
        var centers = input.Pint("center_points", 2);
        var axial = input.Params.TryGetValue("axial", out var rawAxial) && rawAxial is not null
            ? input.Pnum("axial", 1.41421356)
            : Math.Pow(2, factors / 4.0);
        var factorialRuns = (int)Math.Pow(2, factors);
        var rows = factorialRuns + 2 * factors + centers;
        var values = Enumerable.Range(0, factors).Select(_ => new double[rows]).ToArray();
        for (var row = 0; row < factorialRuns; row++)
        {
            for (var factor = 0; factor < factors; factor++) values[factor][row] = ((row >> (factors - 1 - factor)) & 1) == 0 ? -1 : 1;
        }
        for (var factor = 0; factor < factors; factor++)
        {
            values[factor][factorialRuns + 2 * factor] = -axial;
            values[factor][factorialRuns + 2 * factor + 1] = axial;
        }
        var columns = Enumerable.Range(0, factors).Select(factor => DataColumn.Numeric(((char)('A' + factor)).ToString(), values[factor])).ToArray();
        return Result.Ok(new AnalysisResult(
            "response-surface",
            "Diseño de superficie de respuesta (compuesto central)",
            [
                new TilesSection("Resumen",
                [
                    new TileItem("Factores", JsNumber.ToString(factors)),
                    new TileItem("Corridas", JsNumber.ToString(rows)),
                    new TileItem("Distancia axial α", ModelHelpers.N(axial))
                ]),
                new AlertSection(
                    "Aviso",
                    "info",
                    "Diseño compuesto central (factorial + puntos axiales + centro) para ajustar un modelo cuadrático.")
            ],
            Subtitle: $"{rows} corridas · {factors} factores · axial a ±{axial.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)}",
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
            {
                ["diseño"] = new DatasetResult("Diseño compuesto central", columns)
            },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["factors"] = factors,
                ["runs"] = rows,
                ["axial"] = axial
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }
}
