using StatisticEngine.Domain.Charts;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Models;
using StatisticEngine.Domain.Numerics;
using StatisticEngine.Domain.Statistics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>
/// Vector autorregresivo (VAR) con selección de rezagos, estabilidad, causalidad de Granger,
/// impulso-respuesta, descomposición de la varianza y pronósticos. Port de <c>econ_multi._var</c> con
/// la estimación y los diagnósticos de <c>statsmodels.tsa.api.VAR</c>.
/// </summary>
internal static class VarAnalyses
{
    private static readonly (string Value, string Label)[] Criteria =
    [
        ("aic", "AIC"),
        ("bic", "BIC"),
        ("hqic", "HQIC"),
        ("fpe", "FPE")
    ];

    public static readonly AnalysisDefinition Var = new(
        "var",
        "VAR: vectores autorregresivos",
        "Econometría",
        "Rezagos óptimos, estabilidad, causalidad de Granger, impulso-respuesta, descomposición de varianza y pronósticos de varias series a la vez.",
        [],
        [],
        [
            new(
                "criterion",
                "Criterio de selección de rezagos",
                AnalysisParamKinds.Select,
                "aic",
                [.. Criteria.Select(item => new AnalysisParamOption(item.Value, item.Label))],
                false),
            new(
                "lags",
                "Rezagos fijos (vacío = elegir con el criterio)",
                AnalysisParamKinds.Integer,
                null,
                [],
                true,
                Minimum: 1,
                Maximum: 24),
            new(
                "max_lags",
                "Rezagos máximos a probar",
                AnalysisParamKinds.Integer,
                8.0,
                [],
                false,
                Minimum: 1,
                Maximum: 24),
            new(
                "trend",
                "Términos deterministas",
                AnalysisParamKinds.Select,
                "c",
                [
                    new("c", "Constante"),
                    new("ct", "Constante y tendencia"),
                    new("n", "Ninguno"),
                    new("ctt", "Constante, tendencia y tendencia²")
                ],
                false),
            new(
                "horizon",
                "Horizonte del impulso-respuesta",
                AnalysisParamKinds.Integer,
                10.0,
                [],
                false,
                Minimum: 2,
                Maximum: 60),
            new(
                "forecast_steps",
                "Periodos a pronosticar (0 = no)",
                AnalysisParamKinds.Integer,
                8.0,
                [],
                false,
                Minimum: 0,
                Maximum: 100),
            ExtendedHelpers.Alpha()
        ],
        Run,
        Origin: AnalysisCategories.SigmaOrigin,
        Group: "Sistemas de series",
        Roles:
        [
            new ColumnRole("vars", "Series (2 a 8)", ColumnKinds.Numeric, Multiple: true, MinCount: 2, MaxCount: 8),
            new ColumnRole(
                "time",
                "Columna de tiempo (opcional)",
                ColumnKinds.DateTime,
                Optional: true,
                Help: "Sin ella se usa el orden de las filas.")
        ],
        Outputs: [new AnalysisOutputSpec("residuos", "Residuos del VAR")]);

    // ------------------------------------------------------------------ estimación

    private sealed record VarEstimate(
        int Lags,
        int K,
        int Nobs,
        int KTrend,
        double[][] Params,
        double[][] Resid,
        double[][] SigmaU,
        double[][] SigmaUMle,
        double[][] InverseZz,
        int DfResid)
    {
        public int DfModel => K * Lags + KTrend;

        public double[][][] Coefficients()
        {
            var coefs = new double[Lags][][];
            for (var lag = 0; lag < Lags; lag++)
            {
                var matrix = LinearAlgebra.Zeros(K, K);
                for (var equation = 0; equation < K; equation++)
                {
                    for (var variable = 0; variable < K; variable++)
                    {
                        matrix[equation][variable] = Params[KTrend + lag * K + variable][equation];
                    }
                }
                coefs[lag] = matrix;
            }
            return coefs;
        }
    }

    private static int TrendOrder(string trend) => trend switch
    {
        "n" => 0,
        "ct" => 2,
        "ctt" => 3,
        _ => 1
    };

    private static double[][] Design(double[][] y, int lags, string trend, int offset)
    {
        var k = y.Length;
        var n = y[0].Length;
        var kTrend = TrendOrder(trend);
        var rows = n - offset - lags;
        var design = new double[rows][];
        for (var row = 0; row < rows; row++)
        {
            var time = offset + lags + row;
            var values = new List<double>(kTrend + k * lags);
            if (kTrend > 0) values.Add(1);
            // statsmodels añade la tendencia 1..T y después la desplaza `lags` posiciones.
            if (kTrend > 1) values.Add(row + 1.0 + lags);
            if (kTrend > 2) values.Add(Math.Pow(row + 1.0 + lags, 2));
            for (var lag = 1; lag <= lags; lag++)
            {
                for (var variable = 0; variable < k; variable++) values.Add(y[variable][time - lag]);
            }
            design[row] = [.. values];
        }
        return design;
    }

    private static double[][] Response(double[][] y, int lags, int offset)
    {
        var k = y.Length;
        var n = y[0].Length;
        var rows = n - offset - lags;
        var response = new double[rows][];
        for (var row = 0; row < rows; row++)
        {
            var values = new double[k];
            for (var variable = 0; variable < k; variable++) values[variable] = y[variable][offset + lags + row];
            response[row] = values;
        }
        return response;
    }

    private static Result<VarEstimate> Estimate(double[][] y, int lags, string trend, int offset)
    {
        var k = y.Length;
        var design = Design(y, lags, trend, offset);
        var response = Response(y, lags, offset);
        var rows = design.Length;
        var parameters = design[0].Length;
        if (rows <= parameters)
        {
            return Result.Err<VarEstimate>(
                DomainErrorCode.InsufficientData,
                "Se necesitan más observaciones que parámetros.");
        }

        var xtx = LinearAlgebra.Zeros(parameters, parameters);
        var xty = LinearAlgebra.Zeros(parameters, k);
        for (var i = 0; i < parameters; i++)
        {
            for (var j = 0; j < parameters; j++)
            {
                var sum = 0.0;
                for (var row = 0; row < rows; row++) sum += design[row][i] * design[row][j];
                xtx[i][j] = sum;
            }
            for (var variable = 0; variable < k; variable++)
            {
                var sum = 0.0;
                for (var row = 0; row < rows; row++) sum += design[row][i] * response[row][variable];
                xty[i][variable] = sum;
            }
        }

        var inverse = LinearAlgebra.Inverse(xtx);
        if (inverse.IsError) return Result.Err<VarEstimate>(inverse.Error.Code, inverse.Error.Message);
        var paramsMatrix = LinearAlgebra.Multiply(inverse.Value, xty);

        var resid = new double[rows][];
        for (var row = 0; row < rows; row++)
        {
            var values = new double[k];
            for (var variable = 0; variable < k; variable++)
            {
                var fitted = 0.0;
                for (var index = 0; index < parameters; index++) fitted += design[row][index] * paramsMatrix[index][variable];
                values[variable] = response[row][variable] - fitted;
            }
            resid[row] = values;
        }

        var kTrend = TrendOrder(trend);
        var dfResid = rows - (k * lags + kTrend);
        if (dfResid <= 0)
        {
            return Result.Err<VarEstimate>(DomainErrorCode.InsufficientData, "No hay grados de libertad suficientes.");
        }

        var sse = LinearAlgebra.Zeros(k, k);
        for (var row = 0; row < rows; row++)
        {
            for (var i = 0; i < k; i++)
            {
                for (var j = 0; j < k; j++) sse[i][j] += resid[row][i] * resid[row][j];
            }
        }
        var sigmaU = LinearAlgebra.Zeros(k, k);
        var sigmaMle = LinearAlgebra.Zeros(k, k);
        for (var i = 0; i < k; i++)
        {
            for (var j = 0; j < k; j++)
            {
                sigmaU[i][j] = sse[i][j] / dfResid;
                sigmaMle[i][j] = sse[i][j] / rows;
            }
        }

        return Result.Ok(new VarEstimate(lags, k, rows, kTrend, paramsMatrix, resid, sigmaU, sigmaMle, inverse.Value, dfResid));
    }

    private static (double Aic, double Bic, double Hqic, double Fpe) InformationCriteria(VarEstimate estimate)
    {
        var logDet = CholeskyDecomposition.LogDeterminant(estimate.SigmaUMle);
        var free = estimate.Lags * estimate.K * estimate.K + estimate.K * estimate.KTrend;
        var nobs = (double)estimate.Nobs;
        var aic = logDet + 2.0 / nobs * free;
        var bic = logDet + Math.Log(nobs) / nobs * free;
        var hqic = logDet + 2 * Math.Log(Math.Log(nobs)) / nobs * free;
        var fpe = Math.Pow((estimate.Nobs + estimate.DfModel) / (double)estimate.DfResid, estimate.K) * Math.Exp(logDet);
        return (aic, bic, hqic, fpe);
    }

    private static string TermName(string term)
    {
        if (term == "const") return "Constante";
        if (term.Length > 2 && term[0] == 'L' && term.Contains('.'))
        {
            var dot = term.IndexOf('.');
            var lag = term[1..dot];
            if (lag.All(char.IsDigit)) return $"{term[(dot + 1)..]} (rezago {lag})";
        }
        return term;
    }

    private static string[] TermNames(int lags, int k, int kTrend, string[] names)
    {
        var terms = new List<string>();
        if (kTrend > 0) terms.Add("const");
        if (kTrend > 1) terms.Add("trend");
        if (kTrend > 2) terms.Add("trend**2");
        for (var lag = 1; lag <= lags; lag++)
        {
            foreach (var name in names) terms.Add($"L{lag}.{name}");
        }
        return [.. terms];
    }

    // ------------------------------------------------------------------ análisis

    private static Result<AnalysisResult> Run(AnalysisInput input)
    {
        var frame = input.AsFrame();
        var names = input.RoleNames("vars", true).ToArray();
        var timeColumn = ExtendedHelpers.FirstRole(input, "time", "Columna de tiempo");
        var alpha = input.Pnum("alpha", 0.05);
        var horizon = input.Pint("horizon", 10);
        var trend = input.Pstr("trend", "c");
        var maxLags = input.Pint("max_lags", 8);
        var criterion = input.Pstr("criterion", "aic");

        var loaded = MultiSeriesLoader.Load(frame, names, timeColumn, 30);
        if (loaded.IsError) return Result.Err<AnalysisResult>(loaded.Error.Code, loaded.Error.Message);
        var mts = loaded.Value;
        var k = mts.K;
        var n = mts.N;
        var needed = Math.Max(30, (maxLags + 2) * k + 10);
        if (n < needed)
        {
            return ExtendedHelpers.Invalid(
                $"El VAR con esos rezagos necesita al menos {needed} observaciones comunes (hay {n}).");
        }

        var criteria = new Dictionary<string, double[]>(StringComparer.Ordinal)
        {
            ["aic"] = new double[maxLags + 1],
            ["bic"] = new double[maxLags + 1],
            ["hqic"] = new double[maxLags + 1],
            ["fpe"] = new double[maxLags + 1]
        };
        for (var lag = 0; lag <= maxLags; lag++)
        {
            var selection = Estimate(mts.Values, lag, trend, maxLags - lag);
            if (selection.IsError) return Result.Err<AnalysisResult>(selection.Error.Code, selection.Error.Message);
            var (aic, bic, hqic, fpe) = InformationCriteria(selection.Value);
            criteria["aic"][lag] = aic;
            criteria["bic"][lag] = bic;
            criteria["hqic"][lag] = hqic;
            criteria["fpe"][lag] = fpe;
        }

        var selectedByCriterion = criteria.ToDictionary(
            pair => pair.Key,
            pair => Array.IndexOf(pair.Value, pair.Value.Min()),
            StringComparer.Ordinal);

        var fixedLags = input.Params.TryGetValue("lags", out var rawLags)
                        && rawLags is not null
                        && !(rawLags is string text && text.Length == 0);
        int lags;
        string chosenBy;
        if (fixedLags)
        {
            lags = input.Pint("lags", 1);
            chosenBy = "fijados por usted";
        }
        else
        {
            var index = Math.Clamp(selectedByCriterion[criterion], 0, maxLags);
            lags = index == 0 ? 1 : index;
            chosenBy = $"elegidos por {Criteria.First(item => item.Value == criterion).Label}";
        }

        var fitted = Estimate(mts.Values, lags, trend, 0);
        if (fitted.IsError)
        {
            return ExtendedHelpers.Invalid($"No se pudo ajustar el VAR: {fitted.Error.Message}");
        }
        var estimate = fitted.Value;
        var nobs = estimate.Nobs;

        var coefs = estimate.Coefficients();
        var eigenModuli = Roots(estimate, coefs);
        var stable = eigenModuli.Length == 0 || eigenModuli.All(modulus => modulus <= 1);
        var minRoot = eigenModuli.Length == 0 ? double.NaN : 1 / eigenModuli.Max();

        var sections = new List<AnalysisSection>
        {
            new TilesSection(
                "Resumen",
                [
                    new TileItem("Rezagos", JsNumber.ToString(lags)),
                    new TileItem("Observaciones efectivas", JsNumber.ToString(nobs)),
                    new TileItem("AIC", JsNumber.ToString(InformationCriteria(estimate).Aic)),
                    new TileItem("¿Estable?", stable ? "sí" : "no")
                ]),
            new TableSection(
                "Selección de rezagos",
                ["Rezagos", .. Criteria.Select(item => item.Label)],
                Enumerable.Range(0, maxLags + 1).Select(lag => (IReadOnlyList<string>)new[]
                {
                    JsNumber.ToString(lag),
                    ExtendedHelpers.N(criteria["aic"][lag]),
                    ExtendedHelpers.N(criteria["bic"][lag]),
                    ExtendedHelpers.N(criteria["hqic"][lag]),
                    ExtendedHelpers.N(criteria["fpe"][lag])
                }).ToArray(),
                Note: "El rezago que minimiza cada criterio es el sugerido: "
                      + string.Join(", ", Criteria.Select(item => $"{item.Label} {selectedByCriterion[item.Value]}"))
                      + "."),
            stable
                ? new AlertSection(
                    "Conclusión",
                    "success",
                    $"Todas las raíces del polinomio característico están fuera del círculo unitario (mínimo {minRoot.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}): el VAR es estable.")
                : new AlertSection(
                    "Conclusión",
                    "warning",
                    $"El VAR no es estable (raíz con módulo {minRoot.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} ≤ 1): las series probablemente no son estacionarias; use diferencias o un VECM.")
        };

        var terms = TermNames(lags, k, estimate.KTrend, names);
        var coefficientRows = new List<IReadOnlyList<string>>();
        for (var equation = 0; equation < k; equation++)
        {
            for (var term = 0; term < terms.Length; term++)
            {
                var coefficient = estimate.Params[term][equation];
                var error = Math.Sqrt(Math.Max(0, estimate.SigmaU[equation][equation] * estimate.InverseZz[term][term]));
                var tValue = error > 0 ? coefficient / error : double.NaN;
                coefficientRows.Add([
                    names[equation],
                    TermName(terms[term]),
                    ExtendedHelpers.N(coefficient),
                    ExtendedHelpers.N(error),
                    ExtendedHelpers.N(tValue),
                    SigmaText.PValue(ExtendedHelpers.NormalTwoSided(tValue))
                ]);
            }
        }
        sections.Add(new TableSection(
            "Coeficientes por ecuación",
            ["Ecuación (dependiente)", "Regresor", "Coeficiente", "Error est.", "t", "Valor p"],
            coefficientRows));

        var fitRows = new List<IReadOnlyList<string>>();
        for (var variable = 0; variable < k; variable++)
        {
            var mean = 0.0;
            for (var row = 0; row < nobs; row++) mean += mts.Values[variable][lags + row];
            mean /= nobs;
            var sst = 0.0;
            for (var row = 0; row < nobs; row++)
            {
                var delta = mts.Values[variable][lags + row] - mean;
                sst += delta * delta;
            }
            var sse = 0.0;
            for (var row = 0; row < nobs; row++) sse += estimate.Resid[row][variable] * estimate.Resid[row][variable];
            var r2 = sst > 0 ? 1 - sse / sst : 1.0;
            var ddof = k * lags + 1;
            var variance = nobs > ddof ? sse / (nobs - ddof) : double.NaN;
            fitRows.Add([names[variable], ExtendedHelpers.N(r2), ExtendedHelpers.N(Math.Sqrt(variance))]);
        }
        sections.Add(new TableSection(
            "Ajuste de cada ecuación",
            ["Ecuación", "R²", "Error estándar residual"],
            fitRows));

        var granger = new double[k, k];
        for (var caused = 0; caused < k; caused++)
        {
            for (var causing = 0; causing < k; causing++) granger[caused, causing] = double.NaN;
        }
        var grangerRows = new List<IReadOnlyList<string>>();
        for (var caused = 0; caused < k; caused++)
        {
            var row = new List<string> { names[caused] };
            for (var causing = 0; causing < k; causing++)
            {
                if (caused == causing)
                {
                    row.Add("*");
                    continue;
                }
                var pValue = GrangerPValue(estimate, caused, causing);
                granger[caused, causing] = pValue;
                row.Add(SigmaText.PValue(pValue));
            }
            grangerRows.Add(row);
        }
        sections.Add(new TableSection(
            "Causalidad de Granger (valor p de que la columna ayuda a predecir la fila)",
            ["Variable que se predice", .. names],
            grangerRows,
            Note: $"En rojo: p < {SigmaText.General(alpha)}, la columna «causa en el sentido de Granger» a la fila (le aporta información predictiva)."));

        var heatValues = new double[k][];
        for (var row = 0; row < k; row++)
        {
            heatValues[row] = new double[k];
            for (var column = 0; column < k; column++)
            {
                heatValues[row][column] = double.IsNaN(granger[row, column]) ? 1.0 : granger[row, column];
            }
        }
        var charts = new List<ChartSpec>
        {
            new HeatmapChart("Valores p de la causalidad de Granger", names, names, heatValues)
        };

        var whitenessLags = Math.Min(10, Math.Max(lags + 2, 4));
        var (portmanteau, portmanteauDf, portmanteauP) = Whiteness(estimate, whitenessLags);
        var (jarqueBera, jarqueBeraDf, jarqueBeraP) = Normality(estimate);
        sections.Add(new TableSection(
            "Pruebas sobre los residuos",
            ["Prueba", "Estadístico", "gl", "Valor p"],
            [
                new[]
                {
                    "Portmanteau (autocorrelación)",
                    ExtendedHelpers.N(portmanteau),
                    JsNumber.ToString(portmanteauDf),
                    SigmaText.PValue(portmanteauP)
                },
                new[]
                {
                    "Normalidad conjunta (Jarque-Bera)",
                    ExtendedHelpers.N(jarqueBera),
                    JsNumber.ToString(jarqueBeraDf),
                    SigmaText.PValue(jarqueBeraP)
                }
            ],
            Note: "Valores p pequeños: queda autocorrelación (agregue rezagos) o los residuos no son normales (afecta los intervalos)."));

        var ma = MaRepresentation(coefs, horizon);
        var chol = CholeskyDecomposition.Lower(estimate.SigmaU);
        if (chol.IsError) return ExtendedHelpers.Invalid($"No se pudo ajustar el VAR: {chol.Error.Message}");
        var factor = chol.Value;
        var orth = new double[horizon + 1][][];
        for (var step = 0; step <= horizon; step++)
        {
            orth[step] = LinearAlgebra.Multiply(ma[step], factor);
        }

        var irfCharts = new List<XYSeries>();
        for (var impulse = 0; impulse < k; impulse++)
        {
            for (var response = 0; response < k; response++)
            {
                var x = new double[horizon + 1];
                var y = new double[horizon + 1];
                for (var step = 0; step <= horizon; step++)
                {
                    x[step] = step;
                    y[step] = orth[step][response][impulse];
                }
                irfCharts.Add(new XYSeries($"{names[impulse]} → {names[response]}", x, y));
            }
        }
        if (k <= 5)
        {
            charts.Add(new SeriesChart(
                $"Funciones de impulso-respuesta ortogonalizadas (Cholesky, orden: {string.Join(", ", names)})",
                irfCharts,
                "Periodo",
                "Respuesta"));
        }

        var irfRows = new List<IReadOnlyList<string>>();
        for (var impulse = 0; impulse < k; impulse++)
        {
            var row = new List<string> { names[impulse] };
            for (var response = 0; response < k; response++)
            {
                var total = 0.0;
                for (var step = 0; step <= horizon; step++) total += orth[step][response][impulse];
                row.Add(ExtendedHelpers.N(total));
            }
            irfRows.Add(row);
        }
        sections.Add(new TableSection(
            $"Impulso-respuesta acumulada a {horizon} periodos",
            ["Impulso (choque de 1 desv. est.)", .. names.Select(name => $"Respuesta de {name}")],
            irfRows,
            Note: "Choques ortogonalizados con la descomposición de Cholesky: el orden de las series importa (la primera afecta a todas en el mismo periodo)."));

        var marks = new SortedSet<int> { 1, Math.Min(4, horizon), Math.Min(8, horizon), horizon };
        var decomposition = VarianceDecomposition(ma, estimate.SigmaU, horizon);
        var fevdRows = new List<IReadOnlyList<string>>();
        for (var variable = 0; variable < k; variable++)
        {
            foreach (var step in marks)
            {
                var row = new List<string> { names[variable], JsNumber.ToString(step) };
                for (var shock = 0; shock < k; shock++) row.Add(ExtendedHelpers.N(100 * decomposition[variable][step - 1][shock]));
                fevdRows.Add(row);
            }
        }
        sections.Add(new TableSection(
            "Descomposición de la varianza del error de pronóstico (%)",
            ["Variable", "Horizonte", .. names.Select(name => $"Choque de {name}")],
            fevdRows));

        var stepsForecast = input.Pint("forecast_steps", 8);
        if (stepsForecast > 0)
        {
            // El informe publica los pronósticos puntuales; las bandas se reservan para el cliente.
            var (point, _, _) = Forecast(estimate, coefs, mts.Values, stepsForecast, alpha);
            var future = mts.FutureLabels(stepsForecast);
            var forecastRows = new List<IReadOnlyList<string>>();
            for (var step = 0; step < stepsForecast; step++)
            {
                var row = new List<string> { future[step] };
                for (var variable = 0; variable < k; variable++) row.Add(ExtendedHelpers.N(point[step][variable]));
                forecastRows.Add(row);
            }
            sections.Add(new TableSection("Pronósticos", ["Periodo", .. names], forecastRows));

            var panels = new List<XYSeries>();
            var keep = Math.Min(n, 4 * stepsForecast + 12);
            var historyX = new double[keep];
            for (var index = 0; index < keep; index++) historyX[index] = index;
            for (var variable = 0; variable < k && variable < 6; variable++)
            {
                var history = new double[keep];
                for (var index = 0; index < keep; index++) history[index] = mts.Values[variable][n - keep + index];
                panels.Add(new XYSeries($"Datos: {names[variable]}", historyX, history));
                var forecastY = new double[stepsForecast];
                for (var step = 0; step < stepsForecast; step++) forecastY[step] = point[step][variable];
                var forecastX = new double[stepsForecast];
                for (var step = 0; step < stepsForecast; step++) forecastX[step] = keep + step;
                panels.Add(new XYSeries($"Pronóstico: {names[variable]}", forecastX, forecastY, "dash"));
            }
            charts.Add(new SeriesChart(
                "Pronósticos con intervalos de predicción",
                panels,
                "Observación",
                "Valor"));
        }

        var residColumns = new List<DataColumn>(k + 1);
        for (var variable = 0; variable < k; variable++)
        {
            var series = new double[nobs];
            for (var row = 0; row < nobs; row++) series[row] = estimate.Resid[row][variable];
            residColumns.Add(DataColumn.Numeric($"residuo_{names[variable]}", series));
        }
        var stamps = new string?[nobs];
        for (var row = 0; row < nobs; row++) stamps[row] = row + lags < mts.Labels.Count ? mts.Labels[row + lags] : null;
        residColumns.Add(DataColumn.Text("tiempo", stamps));

        var grangerResults = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var caused = 0; caused < k; caused++)
        {
            for (var causing = 0; causing < k; causing++)
            {
                if (caused == causing) continue;
                grangerResults[$"{names[causing]}->{names[caused]}"] = granger[caused, causing];
            }
        }

        return Result.Ok(new AnalysisResult(
            "var",
            "Vector autorregresivo (VAR)",
            sections,
            Subtitle: $"{k} series · {lags} rezago(s) {chosenBy} · n efectivo = {nobs}",
            Charts: charts,
            Datasets: new Dictionary<string, DatasetResult>(StringComparer.Ordinal)
            {
                ["residuos"] = new DatasetResult("Residuos del VAR", residColumns)
            },
            KeyResults: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["lags"] = lags,
                ["stable"] = stable,
                ["min_root"] = minRoot,
                ["granger_p"] = grangerResults,
                ["aic"] = InformationCriteria(estimate).Aic
            },
            Origin: AnalysisCategories.SigmaOrigin));
    }

    // ------------------------------------------------------------------ diagnósticos

    private static double GrangerPValue(VarEstimate estimate, int caused, int causing)
    {
        var k = estimate.K;
        var lags = estimate.Lags;
        var restrictions = new int[lags];
        for (var lag = 0; lag < lags; lag++) restrictions[lag] = estimate.KTrend + lag * k + causing;

        var variance = estimate.SigmaU[caused][caused];
        var middle = new double[lags][];
        var vector = new double[lags];
        for (var i = 0; i < lags; i++)
        {
            middle[i] = new double[lags];
            for (var j = 0; j < lags; j++)
            {
                middle[i][j] = variance * estimate.InverseZz[restrictions[i]][restrictions[j]];
            }
            vector[i] = estimate.Params[restrictions[i]][caused];
        }
        var solved = LinearAlgebra.SolveLinear(middle, vector);
        if (solved.IsError) return double.NaN;
        var wald = 0.0;
        for (var i = 0; i < lags; i++) wald += vector[i] * solved.Value[i];
        var statistic = wald / lags;
        var df2 = (double)k * estimate.DfResid;
        return ExtendedStatistics.FSf(statistic, lags, df2);
    }

    private static double[] Roots(VarEstimate estimate, double[][][] coefs)
    {
        var k = estimate.K;
        var lags = estimate.Lags;
        var size = k * lags;
        var companion = LinearAlgebra.Zeros(size, size);
        for (var row = 0; row < k; row++)
        {
            for (var lag = 0; lag < lags; lag++)
            {
                for (var column = 0; column < k; column++) companion[row][lag * k + column] = coefs[lag][row][column];
            }
        }
        for (var row = k; row < size; row++) companion[row][row - k] = 1;

        var eigenvalues = ComplexEigen.Moduli(companion);
        return eigenvalues;
    }

    private static double[][][] Acov(double[][] residuals, int nlags)
    {
        var n = residuals.Length;
        var k = residuals[0].Length;
        var means = new double[k];
        for (var i = 0; i < k; i++)
        {
            for (var row = 0; row < n; row++) means[i] += residuals[row][i];
            means[i] /= n;
        }
        var centered = new double[n][];
        for (var row = 0; row < n; row++)
        {
            centered[row] = new double[k];
            for (var i = 0; i < k; i++) centered[row][i] = residuals[row][i] - means[i];
        }

        var output = new double[nlags + 1][][];
        for (var lag = 0; lag <= nlags; lag++)
        {
            var matrix = LinearAlgebra.Zeros(k, k);
            for (var row = lag; row < n; row++)
            {
                for (var i = 0; i < k; i++)
                {
                    for (var j = 0; j < k; j++) matrix[i][j] += centered[row][i] * centered[row - lag][j];
                }
            }
            for (var i = 0; i < k; i++)
            {
                for (var j = 0; j < k; j++) matrix[i][j] /= n;
            }
            output[lag] = matrix;
        }
        return output;
    }

    private static (double Statistic, int Df, double PValue) Whiteness(VarEstimate estimate, int nlags)
    {
        var k = estimate.K;
        var acov = Acov(estimate.Resid, nlags);
        var inverse = LinearAlgebra.Inverse(acov[0]);
        if (inverse.IsError) return (double.NaN, 0, double.NaN);

        var statistic = 0.0;
        for (var lag = 1; lag <= nlags; lag++)
        {
            var ct = acov[lag];
            var middle = LinearAlgebra.Multiply(LinearAlgebra.Transpose(ct), inverse.Value);
            var product = LinearAlgebra.Multiply(middle, ct);
            product = LinearAlgebra.Multiply(product, inverse.Value);
            for (var i = 0; i < k; i++) statistic += product[i][i];
        }
        statistic *= estimate.Nobs;
        var df = k * k * (nlags - estimate.Lags);
        return (statistic, df, ExtendedStatistics.ChiSquareSf(statistic, df));
    }

    private static (double Statistic, int Df, double PValue) Normality(VarEstimate estimate)
    {
        var k = estimate.K;
        var nobs = estimate.Nobs;
        var means = new double[k];
        for (var i = 0; i < k; i++)
        {
            for (var row = 0; row < nobs; row++) means[i] += estimate.Resid[row][i];
            means[i] /= nobs;
        }
        var centered = new double[k][];
        for (var i = 0; i < k; i++)
        {
            centered[i] = new double[nobs];
            for (var row = 0; row < nobs; row++) centered[i][row] = estimate.Resid[row][i] - means[i];
        }
        var sigma = LinearAlgebra.Zeros(k, k);
        for (var i = 0; i < k; i++)
        {
            for (var j = 0; j < k; j++)
            {
                var sum = 0.0;
                for (var row = 0; row < nobs; row++) sum += centered[i][row] * centered[j][row];
                sigma[i][j] = sum / nobs;
            }
        }
        var lower = CholeskyDecomposition.Lower(sigma);
        if (lower.IsError) return (double.NaN, k * 2, double.NaN);
        var inverse = CholeskyDecomposition.InverseLower(lower.Value);
        if (inverse.IsError) return (double.NaN, k * 2, double.NaN);
        var pinv = inverse.Value;

        var skew = 0.0;
        var kurt = 0.0;
        for (var i = 0; i < k; i++)
        {
            var third = 0.0;
            var fourth = 0.0;
            for (var row = 0; row < nobs; row++)
            {
                var value = 0.0;
                for (var j = 0; j < k; j++) value += pinv[i][j] * centered[j][row];
                third += Math.Pow(value, 3);
                fourth += Math.Pow(value, 4);
            }
            var b1 = third / nobs;
            var b2 = fourth / nobs - 3;
            skew += b1 * b1;
            kurt += b2 * b2;
        }
        var statistic = nobs * skew / 6 + nobs * kurt / 24;
        var df = k * 2;
        return (statistic, df, ExtendedStatistics.ChiSquareSf(statistic, df));
    }

    // ------------------------------------------------------------------ impulso-respuesta y pronóstico

    private static double[][][] MaRepresentation(double[][][] coefs, int steps)
    {
        var k = coefs[0].Length;
        var lags = coefs.Length;
        var phis = new double[steps + 1][][];
        phis[0] = LinearAlgebra.Identity(k);
        for (var i = 1; i <= steps; i++)
        {
            var matrix = LinearAlgebra.Zeros(k, k);
            for (var j = 1; j <= i && j <= lags; j++)
            {
                var product = LinearAlgebra.Multiply(phis[i - j], coefs[j - 1]);
                for (var row = 0; row < k; row++)
                {
                    for (var column = 0; column < k; column++) matrix[row][column] += product[row][column];
                }
            }
            phis[i] = matrix;
        }
        return phis;
    }

    private static double[][][] VarianceDecomposition(double[][][] ma, double[][] sigma, int horizon)
    {
        var k = sigma.Length;
        var cumulative = new double[k][][];
        for (var variable = 0; variable < k; variable++)
        {
            cumulative[variable] = new double[horizon][];
            for (var step = 0; step < horizon; step++) cumulative[variable][step] = new double[k];
        }

        var chol = CholeskyDecomposition.Lower(sigma);
        var factor = chol.IsError ? LinearAlgebra.Identity(k) : chol.Value;
        var prior = LinearAlgebra.Zeros(k, k);
        var running = LinearAlgebra.Zeros(k, k);
        for (var step = 0; step < horizon; step++)
        {
            var phi = ma[step];
            var left = LinearAlgebra.Multiply(phi, sigma);
            var contribution = LinearAlgebra.Multiply(left, LinearAlgebra.Transpose(phi));
            for (var i = 0; i < k; i++)
            {
                for (var j = 0; j < k; j++) prior[i][j] += contribution[i][j];
            }
            var orth = LinearAlgebra.Multiply(phi, factor);
            for (var i = 0; i < k; i++)
            {
                for (var shock = 0; shock < k; shock++) running[i][shock] += orth[i][shock] * orth[i][shock];
            }
            for (var i = 0; i < k; i++)
            {
                var total = prior[i][i];
                for (var shock = 0; shock < k; shock++)
                {
                    cumulative[i][step][shock] = total > 0 ? running[i][shock] / total : double.NaN;
                }
            }
        }
        return cumulative;
    }

    private static (double[][] Point, double[][] Lower, double[][] Upper) Forecast(
        VarEstimate estimate,
        double[][][] coefs,
        double[][] values,
        int steps,
        double alpha)
    {
        var k = estimate.K;
        var lags = estimate.Lags;
        var n = values[0].Length;
        var initial = new double[lags][];
        for (var lag = 0; lag < lags; lag++)
        {
            initial[lag] = new double[k];
            for (var variable = 0; variable < k; variable++) initial[lag][variable] = values[variable][n - lags + lag];
        }

        var point = new double[steps][];
        for (var h = 1; h <= steps; h++)
        {
            var row = new double[k];
            for (var variable = 0; variable < k; variable++)
            {
                row[variable] = estimate.KTrend > 0 ? estimate.Params[0][variable] : 0.0;
            }
            for (var lag = 1; lag <= lags; lag++)
            {
                // y_t(h) = α + Σ A_i·y_t(h−i), con y_t(≤0) igual a los últimos valores observados.
                var source = h - lag <= 0 ? initial[lags + (h - lag) - 1] : point[h - lag - 1];
                for (var equation = 0; equation < k; equation++)
                {
                    var acc = 0.0;
                    for (var variable = 0; variable < k; variable++) acc += coefs[lag - 1][equation][variable] * source[variable];
                    row[equation] += acc;
                }
            }
            point[h - 1] = row;
        }

        // Varianza del error de pronóstico: suma de Phi_i·Σ·Phi_iᵀ.
        var ma = MaRepresentation(coefs, steps);
        var variances = new double[steps][];
        var priorCov = LinearAlgebra.Zeros(k, k);
        for (var step = 0; step < steps; step++)
        {
            var phi = ma[step];
            var left = LinearAlgebra.Multiply(phi, estimate.SigmaU);
            var contribution = LinearAlgebra.Multiply(left, LinearAlgebra.Transpose(phi));
            for (var i = 0; i < k; i++)
            {
                for (var j = 0; j < k; j++) priorCov[i][j] += contribution[i][j];
            }
            variances[step] = new double[k];
            for (var i = 0; i < k; i++) variances[step][i] = priorCov[i][i];
        }

        var q = SpecialFunctions.NormalInv(1 - alpha / 2);
        var lower = new double[steps][];
        var upper = new double[steps][];
        for (var step = 0; step < steps; step++)
        {
            lower[step] = new double[k];
            upper[step] = new double[k];
            for (var variable = 0; variable < k; variable++)
            {
                var sigma = Math.Sqrt(variances[step][variable]);
                lower[step][variable] = point[step][variable] - q * sigma;
                upper[step][variable] = point[step][variable] + q * sigma;
            }
        }
        return (point, lower, upper);
    }
}
