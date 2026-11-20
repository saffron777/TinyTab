using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>
/// Cambio estructural de fecha desconocida (Quandt-Andrews) con valor p simulado por Monte Carlo
/// (port de <c>econ_models._qa_break</c>, <c>sup_f_path</c> y <c>andrews_p_value</c>).
/// <para>
/// La simulación usa la misma semilla (20240601), el mismo generador PCG64 de NumPy y el mismo
/// orden de consumo de normales que Sigma, de modo que el valor p y el crítico coinciden bit a bit.
/// </para>
/// </summary>
internal static class QuandtAndrewsAnalyses
{
    private const double SimulationSeed = 20240601;

    private const int SimulationReps = 4000;

    private const int GridPoints = 600;

    private const int SimulationChunk = 500;

    public static readonly AnalysisDefinition QaBreak = new(
        "qa-break",
        "Cambio estructural de fecha desconocida (Quandt-Andrews)",
        "Cambio estructural",
        "Busca el punto de cambio más probable en la media o en una regresión con la prueba sup-F y valor p simulado.",
        [],
        [],
        [
            new("trim", "Recorte de los extremos", AnalysisParamKinds.Number, 0.15, [], false, Minimum: 0.05, Maximum: 0.4),
            ExtendedHelpers.Alpha()
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Cambio estructural",
        Roles:
        [
            new ColumnRole("series", "Serie", ColumnKinds.Numeric),
            new ColumnRole("time", "Columna de tiempo (opcional)", ColumnKinds.DateTime, Optional: true, Help: "Sin ella se usa el orden de las filas."),
            new ColumnRole("regressors", "Regresores (opcional; vacío = cambio en la media)", ColumnKinds.Numeric, Multiple: true, Optional: true)
        ]);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var seriesColumn = ExtendedHelpers.FirstRole(input, "series", "Serie");
        var timeColumn = ExtendedHelpers.FirstRole(input, "time", "Columna de tiempo");
        var loaded = SeriesLoader.Load(frame, seriesColumn, timeColumn, 0, minimum: 30);
        if (loaded.IsError) return Result.Err<AnalysisResult>(loaded.Error.Code, loaded.Error.Message);
        var series = loaded.Value;
        var values = series.Values;
        var n = values.Length;
        if (n > 5000)
        {
            return ExtendedHelpers.Invalid("La prueba de Quandt-Andrews admite hasta 5 000 observaciones.");
        }

        var trim = input.Pnum("trim", 0.15);
        var alpha = input.Pnum("alpha", 0.05);
        var regressors = input.RoleNames("regressors");
        double[][] design;
        string[] names;
        if (regressors.Count > 0)
        {
            var built = BuildRegressors(frame, seriesColumn, timeColumn, regressors, n);
            if (built.IsError) return Result.Err<AnalysisResult>(built.Error.Code, built.Error.Message);
            design = built.Value.Design;
            names = built.Value.Names;
        }
        else
        {
            design = new double[n][];
            for (var index = 0; index < n; index++) design[index] = new double[] { 1.0 };
            names = ["Constante (media)"];
        }

        var k = design[0].Length;
        var path = SupFPath(design, values, trim);
        if (path.Positions.Length < 5)
        {
            return ExtendedHelpers.Invalid("Con ese recorte quedan muy pocos puntos candidatos: reduzca el recorte.");
        }

        var best = 0;
        for (var index = 1; index < path.Statistics.Length; index++)
        {
            if (path.Statistics[index] > path.Statistics[best]) best = index;
        }
        var supF = path.Statistics[best];
        var wald = k * supF;
        var simulation = AndrewsPValue(wald, k, trim);
        var when = series.Labels[path.Positions[best]];

        var sections = new List<AnalysisSection>();
        if (series.Dropped > 0)
        {
            sections.Add(new AlertSection(
                "Aviso",
                "info",
                $"Se omitieron {series.Dropped} valor(es) faltante(s) al principio o al final de la serie."));
        }
        if (series.Irregular)
        {
            sections.Add(new AlertSection(
                "Aviso",
                "info",
                "Las fechas no son regulares: el análisis usa el orden de las observaciones, sin periodo calendario."));
        }

        sections.Add(new TilesSection(
            "Resumen",
            [
                new TileItem("sup-F", ModelHelpers.N(supF)),
                new TileItem("sup-Wald (k·F)", ModelHelpers.N(wald)),
                new TileItem("Valor p", ExtendedHelpers.P(simulation.P)),
                new TileItem("Quiebre más probable", when)
            ]));
        sections.Add(new AlertSection(
            "Aviso",
            simulation.P < alpha ? "warning" : "success",
            simulation.P < alpha
                ? $"Hay evidencia de un cambio estructural; el punto más probable es la observación {path.Positions[best] + 1} ({when}), p = {SigmaText.Fixed4(simulation.P)}."
                : $"No hay evidencia de un cambio estructural de ubicación desconocida (p = {SigmaText.Fixed4(simulation.P)})."));

        var before = Segment(design, values, 0, path.Positions[best]);
        var after = Segment(design, values, path.Positions[best], n);
        var coefficientRows = new List<IReadOnlyList<string>>();
        for (var index = 0; index < k; index++)
        {
            coefficientRows.Add([
                names[index],
                ModelHelpers.N(before.Coefficients[index]),
                ModelHelpers.N(after.Coefficients[index]),
                ModelHelpers.N(after.Coefficients[index] - before.Coefficients[index])
            ]);
        }
        sections.Add(new TableSection(
            "Coeficientes antes y después del quiebre más probable",
            ["Término", "Antes", "Después", "Diferencia"],
            coefficientRows));
        sections.Add(new TableSection(
            "Prueba",
            ["sup-F", "sup-Wald", "Crítico 5 % (simulado)", "Valor p (simulado)"],
            [
                [
                    ModelHelpers.N(supF),
                    ModelHelpers.N(wald),
                    ModelHelpers.N(simulation.Critical),
                    ExtendedHelpers.P(simulation.P)
                ]
            ],
            Note: "El valor p y el crítico se simulan a partir de la distribución asintótica de Andrews (1993), que depende solo del número de coeficientes y del recorte."));

        var x = path.Positions.Select(position => (double)(position + 1)).ToArray();
        return Result.Ok(new AnalysisResult(
            "qa-break",
            $"Quandt-Andrews: cambio estructural en {seriesColumn}",
            sections,
            Subtitle: $"{k} coeficiente(s) · recorte {SigmaText.General(100 * trim)} % · n = {n}",
            Charts:
            [
                new SeriesChart(
                    "Estadístico F de Chow para cada fecha candidata",
                    [new XYSeries("F de Chow", x, path.Statistics)],
                    "Observación",
                    "F")
            ],
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sup_f"] = supF,
                ["sup_wald"] = wald,
                ["p"] = simulation.P,
                ["break_position"] = path.Positions[best] + 1,
                ["break_label"] = when
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    private sealed record DesignResult(double[][] Design, string[] Names);

    private static Result<DesignResult> BuildRegressors(
        Frame frame,
        string seriesColumn,
        string timeColumn,
        IReadOnlyList<string> regressors,
        int expected)
    {
        var seriesValues = frame.Numeric(seriesColumn);
        var order = Enumerable.Range(0, frame.RowCount).ToArray();
        if (!string.IsNullOrEmpty(timeColumn))
        {
            var column = frame.Column(timeColumn);
            order = column.Kind == DataColumnKind.DateTime
                ? [.. order.OrderBy(index => column.Dates[index] ?? DateTime.MaxValue)]
                : [.. order.OrderBy(index => column.TextAt(index), StringComparer.Ordinal)];
        }
        var sorted = order.Select(index => seriesValues[index]).ToArray();
        var first = 0;
        while (first < sorted.Length && double.IsNaN(sorted[first])) first++;
        var last = sorted.Length - 1;
        while (last >= first && double.IsNaN(sorted[last])) last--;
        var used = order[first..(last + 1)];
        if (used.Length != expected)
        {
            return Result.Err<DesignResult>(DomainErrorCode.InvalidInput, "Los regresores no se pudieron alinear con la serie.");
        }

        var design = new double[used.Length][];
        for (var row = 0; row < used.Length; row++)
        {
            design[row] = new double[regressors.Count + 1];
            design[row][0] = 1;
        }
        for (var columnIndex = 0; columnIndex < regressors.Count; columnIndex++)
        {
            var values = frame.Numeric(regressors[columnIndex]);
            for (var row = 0; row < used.Length; row++)
            {
                var value = values[used[row]];
                if (double.IsNaN(value))
                {
                    return Result.Err<DesignResult>(
                        DomainErrorCode.InvalidInput,
                        "Los regresores no deben tener faltantes en las filas de la serie.");
                }
                design[row][columnIndex + 1] = value;
            }
        }
        return Result.Ok(new DesignResult(design, ["Constante", .. regressors]));
    }

    private sealed record SegmentFit(double Ssr, double[] Coefficients);

    private sealed record PathResult(int[] Positions, double[] Statistics);

    /// <summary>Momentos acumulados de la regresión: X'X, X'y e y'y por prefijo.</summary>
    private sealed record Moments(double[][][] Xtx, double[][] Xty, double[] Yty);

    private static Moments CumulativeMoments(double[][] x, double[] y)
    {
        var n = y.Length;
        var k = x[0].Length;
        var xtx = new double[n + 1][][];
        var xty = new double[n + 1][];
        var yty = new double[n + 1];
        xtx[0] = Enumerable.Range(0, k).Select(_ => new double[k]).ToArray();
        xty[0] = new double[k];
        for (var row = 0; row < n; row++)
        {
            var previous = xtx[row];
            var current = new double[k][];
            for (var a = 0; a < k; a++) current[a] = (double[])previous[a].Clone();
            for (var a = 0; a < k; a++)
            {
                for (var b = 0; b < k; b++) current[a][b] += x[row][a] * x[row][b];
            }
            xtx[row + 1] = current;
            var previousY = xty[row];
            var currentY = new double[k];
            for (var a = 0; a < k; a++) currentY[a] = previousY[a] + x[row][a] * y[row];
            xty[row + 1] = currentY;
            yty[row + 1] = yty[row] + y[row] * y[row];
        }
        return new Moments(xtx, xty, yty);
    }

    /// <summary>SCR y coeficientes de la regresión sobre las observaciones <c>start … end − 1</c>.</summary>
    private static SegmentFit Segment(Moments moments, int start, int end)
    {
        var k = moments.Xty[0].Length;
        var a = new double[k][];
        for (var row = 0; row < k; row++)
        {
            a[row] = new double[k];
            for (var column = 0; column < k; column++)
            {
                a[row][column] = moments.Xtx[end][row][column] - moments.Xtx[start][row][column];
            }
        }
        var b = new double[k];
        for (var row = 0; row < k; row++) b[row] = moments.Xty[end][row] - moments.Xty[start][row];
        var c = moments.Yty[end] - moments.Yty[start];
        var solved = LinearAlgebra.SolveLinear(a, b);
        var coefficients = solved.IsError ? new double[k] : solved.Value;
        var explained = 0.0;
        for (var row = 0; row < k; row++) explained += coefficients[row] * b[row];
        return new SegmentFit(Math.Max(c - explained, 0.0), coefficients);
    }

    private static SegmentFit Segment(double[][] x, double[] y, int start, int end) =>
        Segment(CumulativeMoments(x, y), start, end);

    /// <summary>Estadístico F de Chow de cada fecha candidata (posiciones 0-based y valores de F).</summary>
    private static PathResult SupFPath(double[][] x, double[] y, double trim)
    {
        var n = y.Length;
        var k = x[0].Length;
        var moments = CumulativeMoments(x, y);
        var total = Segment(moments, 0, n).Ssr;
        var low = Math.Max((int)Math.Ceiling(trim * n), k + 1);
        var high = Math.Min((int)Math.Floor((1 - trim) * n), n - k - 1);
        if (high < low || n <= 2 * k) return new PathResult([], []);
        var count = Math.Max(high - low + 1, 0);
        var positions = new int[count];
        var statistics = new double[count];
        for (var index = 0; index < count; index++)
        {
            var cut = low + index;
            var split = Segment(moments, 0, cut).Ssr + Segment(moments, cut, n).Ssr;
            positions[index] = cut;
            statistics[index] = ((total - split) / k) / (split / (n - 2 * k));
        }
        return new PathResult(positions, statistics);
    }

    private sealed record SimulationResult(double P, double Critical);

    /// <summary>
    /// Valor p y crítico al 5 % del sup-Wald: supremo del cuadrado de un puente de Bessel sobre la
    /// rejilla de 600 puntos, con 4 000 réplicas (port de <c>andrews_p_value</c>).
    /// </summary>
    private static SimulationResult AndrewsPValue(double wald, int k, double trim)
    {
        var random = new NumpyRandom((ulong)SimulationSeed);
        var grid = new double[GridPoints];
        for (var index = 0; index < GridPoints; index++) grid[index] = (index + 1) / (double)GridPoints;
        var inside = new List<int>();
        for (var index = 0; index < GridPoints; index++)
        {
            if (grid[index] >= trim && grid[index] <= 1 - trim) inside.Add(index);
        }
        var weight = inside.Select(index => grid[index] * (1 - grid[index])).ToArray();
        var root = Math.Sqrt(GridPoints);
        var walk = new double[GridPoints];
        var sup = new double[SimulationReps];
        for (var rep = 0; rep < SimulationReps; rep++)
        {
            var accumulated = 0.0;
            for (var point = 0; point < GridPoints; point++)
            {
                var normal = 0.0;
                for (var dimension = 0; dimension < k; dimension++) normal += random.StandardNormal();
                accumulated += normal / root;
                walk[point] = accumulated;
            }
            var last = walk[GridPoints - 1];
            var maximum = double.NegativeInfinity;
            for (var index = 0; index < inside.Count; index++)
            {
                var bridge = walk[inside[index]] - grid[inside[index]] * last;
                var statistic = bridge * bridge / weight[index];
                if (statistic > maximum) maximum = statistic;
            }
            sup[rep] = maximum;
        }
        var exceed = 0;
        for (var index = 0; index < sup.Length; index++)
        {
            if (sup[index] >= wald) exceed++;
        }
        var pValue = (double)exceed / SimulationReps;
        return new SimulationResult(pValue, Quantile(sup, 0.95));
    }

    /// <summary><c>np.quantile(values, q)</c> con interpolación lineal (método por defecto de NumPy).</summary>
    private static double Quantile(double[] values, double q)
    {
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        var position = (sorted.Length - 1) * q;
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, sorted.Length - 1);
        var fraction = position - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }
}
