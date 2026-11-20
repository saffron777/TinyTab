using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>
/// Pruebas de raíz unitaria y estacionariedad (port de <c>series_models._unit_root</c>).
/// Reimplementa ADF y KPSS de statsmodels y Phillips-Perron y DF-GLS de arch con las mismas
/// tablas de valores críticos y de p-valores, para que cada celda coincida con Sigma.
/// </summary>
internal static class UnitRootAnalyses
{
    private static readonly (string Value, string Label)[] TestOptions =
    [
        ("adf", "Dickey-Fuller aumentada (ADF)"),
        ("pp", "Phillips-Perron"),
        ("dfgls", "DF-GLS (Elliott-Rothenberg-Stock)"),
        ("kpss", "KPSS"),
        ("za", "Zivot-Andrews (con un quiebre)")
    ];

    public static readonly AnalysisDefinition UnitRoot = new(
        "unit-root",
        "Raíces unitarias y estacionariedad (ADF, PP, DF-GLS, KPSS, Zivot-Andrews)",
        "Series temporales",
        "Decide si la serie es estacionaria y cuántas diferencias necesita, con varias pruebas a la vez.",
        [],
        [],
        [
            new(
                "tests",
                "Pruebas",
                AnalysisParamKinds.Multiselect,
                new[] { "adf", "pp", "kpss" },
                [.. TestOptions.Select(option => new AnalysisParamOption(option.Value, option.Label))],
                false),
            new(
                "regression",
                "Términos deterministas",
                AnalysisParamKinds.Select,
                "c",
                [
                    new("c", "Constante"),
                    new("ct", "Constante y tendencia"),
                    new("n", "Ninguno (ADF y PP)")
                ],
                false),
            new(
                "differences",
                "Probar también en diferencias",
                AnalysisParamKinds.Select,
                "01",
                [
                    new("0", "Solo en niveles"),
                    new("01", "Niveles y primeras diferencias"),
                    new("012", "Hasta segundas diferencias")
                ],
                false),
            new(
                "max_lags",
                "Rezagos máximos de ADF (vacío = automático)",
                AnalysisParamKinds.Integer,
                null,
                [],
                true,
                Minimum: 0,
                Maximum: 60),
            new("period", "Periodo estacional (0 = según la fecha)", AnalysisParamKinds.Integer, 0.0, [], false, Minimum: 0, Maximum: 400),
            new("alpha", "Nivel de significancia α", AnalysisParamKinds.Number, 0.05, [], false, Minimum: 0.001, Maximum: 0.5)
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Estacionariedad",
        Roles:
        [
            new ColumnRole("series", "Serie", ColumnKinds.Numeric),
            new ColumnRole("time", "Columna de tiempo (opcional)", ColumnKinds.DateTime, Optional: true, Help: "Sin ella se usa el orden de las filas.")
        ]);

    private static readonly Dictionary<string, string> TrendNames = new(StringComparer.Ordinal)
    {
        ["c"] = "constante",
        ["ct"] = "constante y tendencia",
        ["n"] = "sin término determinista"
    };

    private sealed record UrRow(string Label, string NullHypothesis, double Stat, double P, double Crit5, int Lags, bool Stationary);

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var seriesColumn = ExtendedHelpers.FirstRole(input, "series", "Serie");
        var timeColumn = ExtendedHelpers.FirstRole(input, "time", "Columna de tiempo");
        var loaded = SeriesLoader.Load(frame, seriesColumn, timeColumn, input.Pint("period", 0), minimum: 15);
        if (loaded.IsError) return Result.Err<AnalysisResult>(loaded.Error.Code, loaded.Error.Message);
        var series = loaded.Value;
        var tests = input.Plist("tests").ToList();
        if (tests.Count == 0) return ExtendedHelpers.Invalid("Elija al menos una prueba.");
        var alpha = input.Pnum("alpha", 0.05);
        var regression = input.Pstr("regression", "c");
        var orders = input.Pstr("differences", "01") switch
        {
            "0" => new[] { 0 },
            "012" => new[] { 0, 1, 2 },
            _ => new[] { 0, 1 }
        };
        int? maxLags = null;
        if (input.Params.TryGetValue("max_lags", out var rawMax) && rawMax is not null)
        {
            var parsed = input.Pint("max_lags", 0);
            // Sigma trata 0 como "sin límite" (es falsy en Python).
            if (parsed > 0) maxLags = parsed;
        }

        var n = series.Values.Length;
        var sections = new List<AnalysisSection>();
        var verdicts = new Dictionary<int, bool[]>();
        foreach (var d in orders)
        {
            var values = d == 0 ? series.Values : Difference(series.Values, d);
            if (values.Length < 12) continue;
            var regressionUsed = d > 0 && regression == "ct" ? "c" : regression;
            var rows = new List<UrRow>();
            foreach (var test in tests)
            {
                switch (test)
                {
                    case "adf":
                        rows.Add(Adf(values, regressionUsed, alpha, maxLags));
                        break;
                    case "pp":
                        rows.Add(PhillipsPerron(values, regressionUsed, alpha));
                        break;
                    case "dfgls":
                        rows.Add(DfGls(values, regressionUsed, alpha));
                        break;
                    case "kpss":
                        rows.Add(Kpss(values, regressionUsed, alpha));
                        break;
                    case "za":
                        rows.Add(ZivotAndrews(values, regressionUsed, alpha));
                        break;
                }
            }
            verdicts[d] = rows.Select(row => row.Stationary).ToArray();
            var title = d switch
            {
                0 => "Serie en niveles",
                1 => "Serie en primeras diferencias",
                _ => "Serie en segundas diferencias"
            };
            sections.Add(new TableSection(
                title,
                ["Prueba", "Hipótesis nula", "Estadístico", "Valor p", "Valor crítico 5 %", "Rezagos", "¿Estacionaria?"],
                rows.Select(row => (IReadOnlyList<string>)new[]
                {
                    row.Label,
                    row.NullHypothesis,
                    ModelHelpers.N(row.Stat),
                    ModelHelpers.P(row.P),
                    ModelHelpers.N(row.Crit5),
                    JsNumber.ToString(row.Lags),
                    row.Stationary ? "sí" : "no"
                }).ToArray(),
                Note: d == 0
                    ? "Para ADF, PP, DF-GLS y Zivot-Andrews se rechaza la raíz unitaria con un valor p pequeño; para KPSS, un valor p pequeño indica NO estacionariedad. Los valores p de KPSS se acotan a [0.01, 0.10]."
                    : null));
        }

        int? integrated = null;
        foreach (var d in orders)
        {
            if (verdicts.TryGetValue(d, out var items) && items.Count(value => value) > items.Length / 2.0)
            {
                integrated = d;
                break;
            }
        }
        if (integrated is null)
        {
            sections.Add(new AlertSection(
                "Aviso",
                "warning",
                "La mayoría de las pruebas no indican estacionariedad ni siquiera en las diferencias probadas: pruebe más diferencias o transforme la serie."));
        }
        else if (integrated == 0)
        {
            sections.Add(new AlertSection("Aviso", "success", "La mayoría de las pruebas indican que la serie es estacionaria en niveles."));
        }
        else
        {
            sections.Add(new AlertSection(
                "Aviso",
                "info",
                $"La serie parece integrada de orden {integrated}: se vuelve estacionaria tras {integrated} diferencia(s)."));
        }

        sections.Add(new TilesSection("Resumen",
        [
            new TileItem("Observaciones", JsNumber.ToString(n)),
            new TileItem("Pruebas", JsNumber.ToString(tests.Count)),
            new TileItem("Orden de integración sugerido", integrated is { } order ? JsNumber.ToString(order) : "—")
        ]));

        var charts = new List<ChartSpec>();
        if (orders.Length > 1 && n > 15)
        {
            var x = Enumerable.Range(1, n).Select(index => (double)index).ToArray();
            var differences = Difference(series.Values, 1);
            charts.Add(new SeriesChart(
                $"{seriesColumn}: niveles y primeras diferencias",
                [
                    new XYSeries("Niveles", x, series.Values),
                    new XYSeries("Primeras diferencias", x[1..], differences)
                ],
                "Observación",
                seriesColumn));
        }

        var verdictMap = verdicts.ToDictionary(
            pair => pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture),
            pair => (object?)pair.Value,
            StringComparer.Ordinal);
        return Result.Ok(new AnalysisResult(
            "unit-root",
            $"Pruebas de raíz unitaria y estacionariedad: {seriesColumn}",
            sections,
            Subtitle: $"n = {n} · especificación: {TrendNames[regression]} · α = {SigmaText.General(alpha)}",
            Charts: charts,
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["integration_order"] = integrated,
                ["verdicts"] = verdictMap
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    // ------------------------------------------------------------------ ADF (statsmodels adfuller)

    private static UrRow Adf(double[] y, string regression, double alpha, int? maxLags)
    {
        var n = y.Length;
        var trendColumns = regression == "n" ? 0 : regression.Length;
        int maxlag;
        if (maxLags is { } requested && requested > 0)
        {
            maxlag = Math.Min(requested, Math.Max(0, n / 2 - trendColumns - 1));
        }
        else
        {
            maxlag = (int)Math.Ceiling(12.0 * Math.Pow(n / 100.0, 1.0 / 4.0));
            maxlag = Math.Min(n / 2 - trendColumns - 1, maxlag);
        }
        maxlag = Math.Max(0, maxlag);

        var differences = Difference(y, 1);
        var levelIndex = trendColumns;
        var full = BuildAdfRows(y, differences, maxlag, trendColumns, regression);
        var response = differences[maxlag..];

        var bestLag = 0;
        var bestAic = double.PositiveInfinity;
        for (var lag = 0; lag <= maxlag; lag++)
        {
            var design = Prefix(full, trendColumns + 1 + lag);
            var fit = Fit(design, response);
            if (fit.Aic < bestAic)
            {
                bestAic = fit.Aic;
                bestLag = lag;
            }
        }

        // statsmodels vuelve a estimar con el número de rezagos elegido, y eso acorta el
        // recorte del lagmat (la muestra final tiene más observaciones que la de selección).
        var rows = differences.Length - bestLag;
        var finalRows = BuildAdfRows(y, differences, bestLag, trendColumns, regression);
        var final = Fit(Prefix(finalRows, trendColumns + 1 + bestLag), differences[bestLag..]);
        var stat = final.TValues[levelIndex];
        var p = MacKinnonP(stat, regression);
        var crit = MacKinnonCrit(regression, rows)[1];
        return new UrRow("ADF", "raíz unitaria (no estacionaria)", stat, p, crit, bestLag, p < alpha);
    }

    /// <summary>
    /// Filas del diseño ADF: [const, (tendencia), nivel, Δy_{t-1}…Δy_{t-maxlag}] con
    /// <c>trim="both", original="in"</c> de statsmodels.
    /// </summary>
    private static double[][] BuildAdfRows(double[] y, double[] differences, int maxLag, int trendColumns, string regression)
    {
        var rows = differences.Length - maxLag;
        var result = new double[rows][];
        for (var index = 0; index < rows; index++)
        {
            var row = new double[trendColumns + 1 + maxLag];
            var column = 0;
            if (regression != "n")
            {
                row[column++] = 1;
                if (regression == "ct")
                {
                    // add_trend numera la tendencia 1..nobs sobre los datos ya recortados.
                    row[column++] = index + 1.0;
                }
            }
            row[column++] = y[maxLag + index];
            for (var lag = 1; lag <= maxLag; lag++) row[column++] = differences[maxLag + index - lag];
            result[index] = row;
        }
        return result;
    }

    private static double MacKinnonP(double stat, string regression)
    {
        var (max, min, star, small, large) = regression switch
        {
            "n" => (double.PositiveInfinity, -19.04, -1.04,
                Scale([0.6344, 1.2378, 3.2496], [1, 1, 1e-2]),
                Scale([0.4797, 9.3557, -0.6999, 3.3066], [1, 1e-1, 1e-1, 1e-2])),
            "ct" => (0.7, -16.18, -2.89,
                Scale([3.2512, 1.6047, 4.9588], [1, 1, 1e-2]),
                Scale([2.5261, 6.1654, -3.7956, -6.0285], [1, 1e-1, 1e-1, 1e-2])),
            _ => (2.74, -18.83, -1.61,
                Scale([2.1659, 1.4412, 3.8269], [1, 1, 1e-2]),
                Scale([1.7339, 9.3202, -1.2745, -1.0368], [1, 1e-1, 1e-1, 1e-2]))
        };
        if (stat > max) return 1.0;
        if (stat < min) return 0.0;
        var coefficients = stat <= star ? small : large;
        return SpecialFunctions.NormalCdf(Poly(coefficients, stat));
    }

    private static double[] MacKinnonCrit(string regression, int nobs)
    {
        var rows = regression switch
        {
            "n" => new[]
            {
                new[] { -2.56574, -2.2358, -3.627, 0.0 },
                new[] { -1.94100, -0.2686, -3.365, 31.223 },
                new[] { -1.61682, 0.2656, -2.714, 25.364 }
            },
            "ct" => new[]
            {
                new[] { -3.95877, -9.0531, -28.428, -134.155 },
                new[] { -3.41049, -4.3904, -9.036, -45.374 },
                new[] { -3.12705, -2.5856, -3.925, -22.380 }
            },
            _ => new[]
            {
                new[] { -3.43035, -6.5393, -16.786, -79.433 },
                new[] { -2.86154, -2.8903, -4.234, -40.040 },
                new[] { -2.56677, -1.5384, -2.809, 0.0 }
            }
        };
        var inverse = 1.0 / nobs;
        return rows.Select(row => row[0] + row[1] * inverse + row[2] * inverse * inverse + row[3] * inverse * inverse * inverse).ToArray();
    }

    // ------------------------------------------------------------------ Phillips-Perron (arch)

    private static UrRow PhillipsPerron(double[] y, string regression, double alpha)
    {
        var n = y.Length;
        var lags = (int)Math.Ceiling(12.0 * Math.Pow(n / 100.0, 1.0 / 4.0));
        var observations = n - 1;
        var trendColumns = regression == "n" ? 0 : regression == "ct" ? 2 : 1;
        var design = new double[observations][];
        for (var index = 0; index < observations; index++)
        {
            var row = new double[1 + trendColumns];
            row[0] = y[index];
            var column = 1;
            if (regression != "n")
            {
                row[column++] = 1;
                if (regression == "ct") row[column] = index + 1.0;
            }
            design[index] = row;
        }
        var response = y[1..];
        var fit = Fit(design, response);
        var residuals = fit.Residuals;
        var k = design[0].Length;
        var lam2 = NeweyWest(residuals, lags);
        var lam = Math.Sqrt(lam2);
        var s2 = residuals.Sum(value => value * value) / (observations - k);
        var s = Math.Sqrt(s2);
        var gamma0 = s2 * (observations - k) / observations;
        var sigma = fit.StandardErrors[0];
        var sigma2 = sigma * sigma;
        var rho = fit.Coefficients[0];
        var stat = Math.Sqrt(gamma0 / lam2) * ((rho - 1) / sigma)
                   - 0.5 * ((lam2 - gamma0) / lam) * (observations * sigma / s);
        var p = ArchMacKinnonP(stat, regression, dfgls: false);
        var crit = ArchMacKinnonCrit(regression, observations, dfgls: false)[1];
        return new UrRow("Phillips-Perron", "raíz unitaria (no estacionaria)", stat, p, crit, lags, p < alpha);
    }

    private static double NeweyWest(double[] values, int lags)
    {
        var n = values.Length;
        var sum = 0.0;
        for (var index = 0; index < n; index++) sum += values[index] * values[index];
        for (var lag = 1; lag <= lags; lag++)
        {
            var cross = 0.0;
            for (var index = lag; index < n; index++) cross += values[index] * values[index - lag];
            sum += 2 * (1.0 - lag / (lags + 1.0)) * cross;
        }
        return sum / n;
    }

    // ------------------------------------------------------------------ DF-GLS (arch)

    private static UrRow DfGls(double[] y, string regression, double alpha)
    {
        var n = y.Length;
        var trend = regression == "ct" ? "ct" : "c";
        var cScalar = trend == "ct" ? -13.5 : -7.0;
        var ct = cScalar / n;
        var trendColumns = trend == "ct" ? 2 : 1;
        var z = new double[n][];
        for (var index = 0; index < n; index++)
        {
            z[index] = trend == "ct" ? [1.0, index + 1.0] : [1.0];
        }

        var deltaZ = new double[n][];
        var deltaY = new double[n];
        deltaZ[0] = (double[])z[0].Clone();
        deltaY[0] = y[0];
        for (var index = 1; index < n; index++)
        {
            deltaZ[index] = new double[trendColumns];
            for (var column = 0; column < trendColumns; column++)
            {
                deltaZ[index][column] = z[index][column] - (1 + ct) * z[index - 1][column];
            }
            deltaY[index] = y[index] - (1 + ct) * y[index - 1];
        }
        var glsCoef = SolveLeastSquares(deltaZ, deltaY);
        var detrended = new double[n];
        for (var index = 0; index < n; index++)
        {
            var acc = 0.0;
            for (var column = 0; column < trendColumns; column++) acc += z[index][column] * glsCoef[column];
            detrended[index] = y[index] - acc;
        }

        var olsCoef = SolveLeastSquares(z, y);
        var olsDetrended = new double[n];
        for (var index = 0; index < n; index++)
        {
            var acc = 0.0;
            for (var column = 0; column < trendColumns; column++) acc += z[index][column] * olsCoef[column];
            olsDetrended[index] = y[index] - acc;
        }

        var maxLag = (int)Math.Ceiling(12.0 * Math.Pow(n / 100.0, 1.0 / 4.0));
        maxLag = Math.Min(maxLag, Math.Max(0, (n - 1) / 2 - 1));

        var olsDiff = Difference(olsDetrended, 1);
        var selectionDesign = BuildNoTrendRows(olsDetrended, olsDiff, maxLag);
        var selectionResponse = olsDiff[maxLag..];
        var bestLag = 0;
        var bestAic = double.PositiveInfinity;
        for (var lag = 0; lag <= maxLag; lag++)
        {
            var fit = Fit(Prefix(selectionDesign, 1 + lag), selectionResponse);
            var aic = -2 * fit.LogLikelihood + 2 * lag;
            if (aic < bestAic)
            {
                bestAic = aic;
                bestLag = lag;
            }
        }

        var finalDiff = Difference(detrended, 1);
        var finalRows = finalDiff.Length - bestLag;
        var finalDesign = BuildNoTrendRows(detrended, finalDiff, bestLag, bestLag);
        var final = Fit(Prefix(finalDesign, 1 + bestLag), finalDiff[bestLag..]);
        var stat = final.TValues[0];
        var p = ArchMacKinnonP(stat, trend, dfgls: true);
        var crit = ArchMacKinnonCrit(trend, finalRows, dfgls: true)[1];
        return new UrRow("DF-GLS", "raíz unitaria (no estacionaria)", stat, p, crit, bestLag, p < alpha);
    }

    /// <summary>Filas [nivel, Δy_{t-1}…Δy_{t-lags}] sin términos deterministas.</summary>
    private static double[][] BuildNoTrendRows(double[] y, double[] differences, int maxLag, int? lags = null)
    {
        var used = lags ?? maxLag;
        var rows = differences.Length - maxLag;
        var result = new double[rows][];
        for (var index = 0; index < rows; index++)
        {
            var row = new double[1 + used];
            row[0] = y[maxLag + index];
            for (var lag = 1; lag <= used; lag++) row[lag] = differences[maxLag + index - lag];
            result[index] = row;
        }
        return result;
    }

    private static double[] SolveLeastSquares(double[][] x, double[] y)
    {
        var rows = x.Length;
        var columns = x[0].Length;
        var xtx = new double[columns][];
        var xty = new double[columns];
        for (var i = 0; i < columns; i++)
        {
            xtx[i] = new double[columns];
            for (var j = 0; j < columns; j++)
            {
                var sum = 0.0;
                for (var row = 0; row < rows; row++) sum += x[row][i] * x[row][j];
                xtx[i][j] = sum;
            }
            var sumY = 0.0;
            for (var row = 0; row < rows; row++) sumY += x[row][i] * y[row];
            xty[i] = sumY;
        }
        var solution = LinearAlgebra.SolveLinear(xtx, xty);
        if (solution.IsError) throw new InvalidOperationException(solution.Error.Message);
        return solution.Value;
    }

    // ------------------------------------------------------------------ KPSS (statsmodels)

    private static UrRow Kpss(double[] y, string regression, double alpha)
    {
        var n = y.Length;
        double[] residuals;
        double[] crit;
        if (regression == "ct")
        {
            var design = new double[n][];
            for (var index = 0; index < n; index++) design[index] = [1.0, index + 1.0];
            residuals = Fit(design, y).Residuals;
            crit = [0.119, 0.146, 0.176, 0.216];
        }
        else
        {
            var mean = y.Average();
            residuals = y.Select(value => value - mean).ToArray();
            crit = [0.347, 0.463, 0.574, 0.739];
        }

        var covLags = (int)Math.Pow(n, 2.0 / 9.0);
        var s0 = residuals.Sum(value => value * value) / n;
        var s1 = 0.0;
        for (var lag = 1; lag <= covLags; lag++)
        {
            var cross = 0.0;
            for (var index = lag; index < n; index++) cross += residuals[index] * residuals[index - lag];
            var normalised = cross / (n / 2.0);
            s0 += normalised;
            s1 += lag * normalised;
        }
        var sHat = s1 / s0;
        var gamma = 1.1447 * Math.Pow(sHat * sHat, 1.0 / 3.0);
        var lags = Math.Min((int)(gamma * Math.Pow(n, 1.0 / 3.0)), n - 1);

        var cumulative = 0.0;
        var eta = 0.0;
        foreach (var value in residuals)
        {
            cumulative += value;
            eta += cumulative * cumulative;
        }
        eta /= n * (double)n;

        var variance = residuals.Sum(value => value * value);
        for (var lag = 1; lag <= lags; lag++)
        {
            var cross = 0.0;
            for (var index = lag; index < n; index++) cross += residuals[index] * residuals[index - lag];
            variance += 2 * (1.0 - lag / (lags + 1.0)) * cross;
        }
        variance /= n;

        var stat = eta / variance;
        var p = Interpolate(stat, crit, [0.10, 0.05, 0.025, 0.01]);
        return new UrRow("KPSS", "estacionariedad", stat, p, crit[1], lags, p >= alpha);
    }

    // ------------------------------------------------------------------ Zivot-Andrews (statsmodels)

    private static UrRow ZivotAndrews(double[] y, string regression, double alpha)
    {
        var n = y.Length;
        var model = regression == "ct" ? "ct" : "c";
        var baseline = Adf(y, "ct", alpha, maxLags: null);
        var lags = baseline.Lags;
        var trimCount = (int)(n * 0.15);
        var startPeriod = trimCount;
        var endPeriod = n - trimCount;
        var baseColumns = model == "ct" ? 5 : 4;

        var cConst = 1.0 / Math.Sqrt(n);
        var tConst = new double[n + 1];
        var tScale = Math.Sqrt(3) / Math.Pow(n, 3.0 / 2.0);
        for (var index = 0; index <= n; index++) tConst[index] = (index + 1) * tScale;

        var endogRaw = Difference(y, 1);
        var endogNorm = Math.Sqrt(endogRaw.Sum(value => value * value));
        var endog = endogRaw.Select(value => value / endogNorm).ToArray();
        var seriesNorm = Math.Sqrt(y.Sum(value => value * value));
        var standardised = y.Select(value => value / seriesNorm).ToArray();

        var rows = endog.Length - lags;
        var exog = new double[rows][];
        for (var index = 0; index < rows; index++)
        {
            exog[index] = new double[baseColumns + lags];
            exog[index][0] = cConst;
            exog[index][baseColumns - 1] = standardised[lags + index];
        }
        // lagmat(trim="none") recortado: columna j = endog[lags + i - j - 1].
        for (var index = 0; index < rows; index++)
        {
            for (var lag = 0; lag < lags; lag++)
            {
                var source = lags + index - lag - 1;
                exog[index][baseColumns + lag] = source >= 0 && source < endog.Length ? endog[source] : 0.0;
            }
        }

        var response = endog[lags..];
        var statistics = Enumerable.Repeat(double.PositiveInfinity, endPeriod + 1).ToArray();
        for (var breakPoint = startPeriod + 1; breakPoint <= endPeriod; breakPoint++)
        {
            var cutoff = breakPoint - (lags + 1);
            for (var row = 0; row < rows; row++)
            {
                var inSecond = row >= cutoff;
                exog[row][1] = inSecond ? cConst : 0.0;
                exog[row][2] = tConst[lags + 2 + row];
                if (model == "ct")
                {
                    exog[row][3] = inSecond ? tConst[row - cutoff + 1] : 0.0;
                }
            }
            var fit = Fit(exog, response);
            statistics[breakPoint] = fit.TValues[baseColumns - 1];
        }

        var bestIndex = 0;
        var bestStat = double.PositiveInfinity;
        for (var index = 0; index < statistics.Length; index++)
        {
            if (statistics[index] < bestStat)
            {
                bestStat = statistics[index];
                bestIndex = index;
            }
        }
        var breakObservation = bestIndex - 1;
        var (p, critical) = ZaCrit(bestStat, model);
        return new UrRow(
            $"Zivot-Andrews (quiebre en la obs. {breakObservation + 1})",
            "raíz unitaria (no estacionaria)",
            bestStat,
            p,
            critical[1],
            lags,
            p < alpha);
    }

    private static readonly (double Percent, double Stat)[] ZaC =
    [
        (0.001, -6.78442), (0.100, -5.83192), (0.200, -5.68139), (0.300, -5.58461),
        (0.400, -5.51308), (0.500, -5.45043), (0.600, -5.39924), (0.700, -5.36023),
        (0.800, -5.33219), (0.900, -5.30294), (1.000, -5.27644), (2.500, -5.03340),
        (5.000, -4.81067), (7.500, -4.67636), (10.000, -4.56618), (12.500, -4.48130),
        (15.000, -4.40507), (17.500, -4.33947), (20.000, -4.28155), (22.500, -4.22683),
        (25.000, -4.17830), (27.500, -4.13101), (30.000, -4.08586), (32.500, -4.04455),
        (35.000, -4.00380), (37.500, -3.96144), (40.000, -3.92078), (42.500, -3.88178),
        (45.000, -3.84503), (47.500, -3.80549), (50.000, -3.77031), (52.500, -3.73209),
        (55.000, -3.69600), (57.500, -3.65985), (60.000, -3.62126), (65.000, -3.54580),
        (70.000, -3.46848), (75.000, -3.38533), (80.000, -3.29112), (85.000, -3.17832),
        (90.000, -3.04165), (92.500, -2.95146), (95.000, -2.83179), (96.000, -2.76465),
        (97.000, -2.68624), (98.000, -2.57884), (99.000, -2.40044), (99.900, -1.88932)
    ];

    private static readonly (double Percent, double Stat)[] ZaCt =
    [
        (0.001, -38.17800), (0.100, -6.43107), (0.200, -6.07279), (0.300, -5.95496),
        (0.400, -5.86254), (0.500, -5.77081), (0.600, -5.72541), (0.700, -5.68406),
        (0.800, -5.65163), (0.900, -5.60419), (1.000, -5.57556), (2.500, -5.29704),
        (5.000, -5.07332), (7.500, -4.93003), (10.000, -4.82668), (12.500, -4.73711),
        (15.000, -4.66020), (17.500, -4.58970), (20.000, -4.52855), (22.500, -4.47100),
        (25.000, -4.42011), (27.500, -4.37387), (30.000, -4.32705), (32.500, -4.28126),
        (35.000, -4.23793), (37.500, -4.19822), (40.000, -4.15800), (42.500, -4.11946),
        (45.000, -4.08064), (47.500, -4.04286), (50.000, -4.00489), (52.500, -3.96837),
        (55.000, -3.93200), (57.500, -3.89496), (60.000, -3.85577), (65.000, -3.77795),
        (70.000, -3.69794), (75.000, -3.61852), (80.000, -3.52485), (85.000, -3.41665),
        (90.000, -3.28527), (92.500, -3.19724), (95.000, -3.08769), (96.000, -3.03088),
        (97.000, -2.96091), (98.000, -2.85581), (99.000, -2.71015), (99.900, -2.28767)
    ];

    private static (double P, double[] Crit) ZaCrit(double stat, string model)
    {
        var table = model == "ct" ? ZaCt : ZaC;
        var percents = table.Select(item => item.Percent).ToArray();
        var stats = table.Select(item => item.Stat).ToArray();
        var p = Interpolate(stat, stats, percents) / 100.0;
        var critical = new[]
        {
            Interpolate(1.0, percents, stats),
            Interpolate(5.0, percents, stats),
            Interpolate(10.0, percents, stats)
        };
        return (p, critical);
    }

    // ------------------------------------------------------------------ arch MacKinnon tables (PP y DF-GLS)

    private static double ArchMacKinnonP(double stat, string regression, bool dfgls)
    {
        if (dfgls)
        {
            var (max, min, star, small, large) = regression == "ct"
                ? (8.73743383728356, -13.681153542634465, -2.1960404365401298,
                    new[] { 2.38767685, 1.57454737, 0.05754439 },
                    new[] { 2.60561421, 1.67850224, 0.0373599, -0.01017936 })
                : (13.365361509140614, -17.561302895074206, -0.4795076091714674,
                    new[] { 0.67422739, 1.25475826, 0.03572509 },
                    new[] { 0.50612497, 0.98305664, -0.05648525, 0.00140875 });
            if (stat > max) return 1.0;
            if (stat < min) return 0.0;
            return SpecialFunctions.NormalCdf(Poly(stat <= star ? small : large, stat));
        }

        var (adfMax, adfMin, adfStar, adfSmall, adfLarge) = regression switch
        {
            "n" => (double.PositiveInfinity, -19.04, -1.04,
                Scale([0.6344, 1.2378, 3.2496], [1, 1, 1e-2]),
                Scale([0.4797, 9.3557, -0.6999, 3.3066], [1, 1e-1, 1e-1, 1e-2])),
            "ct" => (0.7, -16.18, -2.89,
                Scale([3.2512, 1.6047, 4.9588], [1, 1, 1e-2]),
                Scale([2.5261, 6.1654, -3.7956, -6.0285], [1, 1e-1, 1e-1, 1e-2])),
            _ => (2.74, -18.83, -1.61,
                Scale([2.1659, 1.4412, 3.8269], [1, 1, 1e-2]),
                Scale([1.7339, 9.3202, -1.2745, -1.0368], [1, 1e-1, 1e-1, 1e-2]))
        };
        if (stat > adfMax) return 1.0;
        if (stat < adfMin) return 0.0;
        return SpecialFunctions.NormalCdf(Poly(stat <= adfStar ? adfSmall : adfLarge, stat));
    }

    private static double[] ArchMacKinnonCrit(string regression, int nobs, bool dfgls)
    {
        double[][] rows;
        if (dfgls)
        {
            rows = regression == "ct"
                ? [
                    [-3.40689134, -21.69971242, 27.26295939, -816.84404772],
                    [-2.84677178, -19.69109364, 84.7664136, -799.40722401],
                    [-2.55890707, -19.42621991, 116.53759752, -840.31342847]
                ]
                : [
                    [-2.56781793, -20.5575392, 182.727674, -1778.66664],
                    [-1.94363325, -21.7272746, 260.815068, -2269.14916],
                    [-1.61998241, -23.2734708, 306.474378, -2574.83557]
                ];
        }
        else
        {
            rows = regression switch
            {
                "n" => [
                    [-2.56574, -2.2358, -3.627, 0.0],
                    [-1.94100, -0.2686, -3.365, 31.223],
                    [-1.61682, 0.2656, -2.714, 25.364]
                ],
                "ct" => [
                    [-3.95877, -9.0531, -28.428, -134.155],
                    [-3.41049, -4.3904, -9.036, -45.374],
                    [-3.12705, -2.5856, -3.925, -22.380]
                ],
                _ => [
                    [-3.43035, -6.5393, -16.786, -79.433],
                    [-2.86154, -2.8903, -4.234, -40.040],
                    [-2.56677, -1.5384, -2.809, 0.0]
                ]
            };
        }
        var inverse = 1.0 / nobs;
        return rows.Select(row => row[0] + row[1] * inverse + row[2] * inverse * inverse + row[3] * inverse * inverse * inverse).ToArray();
    }

    // ------------------------------------------------------------------ utilidades

    private static LinearFit Fit(double[][] design, double[] response)
    {
        var fit = LinearModel.Fit(design, response);
        if (fit.IsError) throw new InvalidOperationException(fit.Error.Message);
        return fit.Value;
    }

    private static double[][] Prefix(double[][] rows, int columns) =>
        rows.Select(row => row[..columns]).ToArray();

    private static double[] Difference(double[] values, int order)
    {
        var current = values;
        for (var iteration = 0; iteration < order; iteration++)
        {
            var next = new double[current.Length - 1];
            for (var index = 0; index < next.Length; index++) next[index] = current[index + 1] - current[index];
            current = next;
        }
        return current;
    }

    private static double Poly(double[] coefficients, double value)
    {
        var acc = coefficients[^1];
        for (var index = coefficients.Length - 2; index >= 0; index--) acc = acc * value + coefficients[index];
        return acc;
    }

    private static double[] Scale(double[] values, double[] factors) =>
        values.Select((value, index) => value * factors[index]).ToArray();

    /// <summary>Interpolación lineal de <c>numpy.interp</c> (sin extrapolar, como el original).</summary>
    private static double Interpolate(double value, double[] x, double[] y)
    {
        if (value <= x[0]) return y[0];
        if (value >= x[^1]) return y[^1];
        for (var index = 1; index < x.Length; index++)
        {
            if (value > x[index]) continue;
            var fraction = (value - x[index - 1]) / (x[index] - x[index - 1]);
            return y[index - 1] + fraction * (y[index] - y[index - 1]);
        }
        return y[^1];
    }
}
