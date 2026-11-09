using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Inference;

// Port 1:1 de src/domain/stats/tests.ts. tCdf/tQuantile/pValueFromStatistic viven en
// InferenceDistributions y se reutilizan aquí (no se redefinen).

/// <summary>Resultado de una prueba t o z (campos del <c>TTestResult</c> del motor).</summary>
public sealed record TTestResult(
    string Test,
    double Statistic,
    double Df,
    double PValue,
    double Estimate,
    double NullValue,
    double StandardError,
    double Lower,
    double Upper,
    double Confidence,
    string Alternative);

/// <summary>Resultado de una prueba de proporciones.</summary>
public sealed record ProportionResult(
    double Estimate,
    double NullValue,
    double Z,
    double PValue,
    double Lower,
    double Upper,
    double Confidence);

/// <summary>Resultado de una prueba de varianzas.</summary>
public sealed record VarianceTestResult(
    double Statistic,
    double Df,
    double PValue,
    double Ratio,
    double Lower,
    double Upper,
    double Confidence);

/// <summary>Estadístico chi-cuadrada con su p-valor.</summary>
public sealed record ChiSquareResult(double Statistic, double Df, double PValue);

/// <summary>Chi-cuadrada de independencia con la tabla de frecuencias esperadas.</summary>
public sealed record ChiSquareIndependenceResult(
    double Statistic,
    double Df,
    double PValue,
    IReadOnlyList<IReadOnlyList<double>> Expected);

/// <summary>Prueba de normalidad de Anderson-Darling.</summary>
public sealed record AndersonDarlingResult(double Statistic, double PValue, double N);

/// <summary>Prueba de los signos.</summary>
public sealed record SignTestResult(double Positives, double Negatives, double PValue);

/// <summary>Prueba de rangos con signo de Wilcoxon.</summary>
public sealed record WilcoxonSignedRankResult(double Statistic, double PValue, double N);

/// <summary>Prueba U de Mann-Whitney.</summary>
public sealed record MannWhitneyResult(double Statistic, double PValue, double U1, double U2);

/// <summary>Prueba de Kruskal-Wallis.</summary>
public sealed record KruskalWallisResult(double Statistic, double Df, double PValue);

/// <summary>Prueba de rachas.</summary>
public sealed record RunsTestResult(double Runs, double Z, double PValue);

/// <summary>Correlación de Spearman.</summary>
public sealed record SpearmanCorrelationResult(double Rho, double PValue);

/// <summary>Prueba de equivalencia TOST para dos muestras independientes.</summary>
public sealed record TostTwoSampleResult(double LowerP, double UpperP, double PValue, double Estimate);

/// <summary>Pruebas de hipótesis clásicas y no paramétricas del motor.</summary>
public static class HypothesisTests
{
    /// <summary><c>oneSampleT(values, nullValue = 0, confidence = 0.95, alternative = 'two-sided')</c>.</summary>
    public static Result<TTestResult> OneSampleT(
        IReadOnlyList<double> values,
        double nullValue = 0,
        double confidence = 0.95,
        Alternative alternative = Alternative.TwoSided)
    {
        var n = (double)values.Count;
        if (n < 2) return Result<TTestResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos 2 observaciones.");
        var estimate = NumericMath.Mean(values);
        var deviation = NumericMath.Sd(values);
        if (deviation == 0) return Result<TTestResult>.Err(DomainErrorCode.InvalidInput, "La desviación estándar es cero.");
        var standardError = deviation / Math.Sqrt(n);
        var statistic = (estimate - nullValue) / standardError;
        var df = n - 1;
        var pValue = InferenceDistributions.PValueFromStatistic(
            statistic,
            x => InferenceDistributions.TCdf(x, df),
            alternative);
        var alpha = 1 - confidence;
        var critical = InferenceDistributions.TQuantile(
            alternative == Alternative.TwoSided ? 1 - alpha / 2 : 1 - alpha,
            df);
        return Result.Ok(new TTestResult(
            Test: "t",
            Statistic: statistic,
            Df: df,
            PValue: pValue,
            Estimate: estimate,
            NullValue: nullValue,
            StandardError: standardError,
            Lower: estimate - critical * standardError,
            Upper: estimate + critical * standardError,
            Confidence: confidence,
            Alternative: Alternatives.ToWire(alternative)));
    }

    /// <summary><c>oneSampleZ(values, sigma, nullValue = 0, confidence = 0.95, alternative = 'two-sided')</c>.</summary>
    public static Result<TTestResult> OneSampleZ(
        IReadOnlyList<double> values,
        double sigma,
        double nullValue = 0,
        double confidence = 0.95,
        Alternative alternative = Alternative.TwoSided)
    {
        var n = (double)values.Count;
        if (n < 1) return Result<TTestResult>.Err(DomainErrorCode.InsufficientData, "Se necesita al menos una observación.");
        if (!(sigma > 0)) return Result<TTestResult>.Err(DomainErrorCode.InvalidParameter, "σ debe ser mayor que cero.");
        var estimate = NumericMath.Mean(values);
        var standardError = sigma / Math.Sqrt(n);
        var statistic = (estimate - nullValue) / standardError;
        var pValue = InferenceDistributions.PValueFromStatistic(statistic, SpecialFunctions.NormalCdf, alternative);
        var alpha = 1 - confidence;
        var critical = SpecialFunctions.NormalInv(alternative == Alternative.TwoSided ? 1 - alpha / 2 : 1 - alpha);
        return Result.Ok(new TTestResult(
            Test: "z",
            Statistic: statistic,
            Df: double.PositiveInfinity,
            PValue: pValue,
            Estimate: estimate,
            NullValue: nullValue,
            StandardError: standardError,
            Lower: estimate - critical * standardError,
            Upper: estimate + critical * standardError,
            Confidence: confidence,
            Alternative: Alternatives.ToWire(alternative)));
    }

    /// <summary><c>twoSampleT(first, second, options?)</c>.</summary>
    public static Result<TTestResult> TwoSampleT(
        IReadOnlyList<double> first,
        IReadOnlyList<double> second,
        bool pooled = false,
        double confidence = 0.95,
        Alternative alternative = Alternative.TwoSided)
    {
        double n1 = first.Count;
        double n2 = second.Count;
        if (n1 < 2 || n2 < 2)
        {
            return Result<TTestResult>.Err(DomainErrorCode.InsufficientData, "Cada muestra necesita al menos 2 observaciones.");
        }
        var mean1 = NumericMath.Mean(first);
        var mean2 = NumericMath.Mean(second);
        var s1 = NumericMath.Sd(first);
        var s2 = NumericMath.Sd(second);
        var difference = mean1 - mean2;
        double standardError;
        double df;
        if (pooled)
        {
            df = n1 + n2 - 2;
            var pooledVariance = ((n1 - 1) * s1 * s1 + (n2 - 1) * s2 * s2) / df;
            standardError = Math.Sqrt(pooledVariance * (1 / n1 + 1 / n2));
        }
        else
        {
            var v1 = s1 * s1 / n1;
            var v2 = s2 * s2 / n2;
            standardError = Math.Sqrt(v1 + v2);
            df = Math.Pow(v1 + v2, 2) / (v1 * v1 / (n1 - 1) + v2 * v2 / (n2 - 1));
        }
        if (standardError == 0)
        {
            return Result<TTestResult>.Err(DomainErrorCode.InvalidInput, "La variabilidad es cero en ambas muestras.");
        }
        var statistic = difference / standardError;
        var pValue = InferenceDistributions.PValueFromStatistic(
            statistic,
            x => InferenceDistributions.TCdf(x, df),
            alternative);
        var alpha = 1 - confidence;
        var critical = InferenceDistributions.TQuantile(
            alternative == Alternative.TwoSided ? 1 - alpha / 2 : 1 - alpha,
            df);
        return Result.Ok(new TTestResult(
            Test: "t",
            Statistic: statistic,
            Df: df,
            PValue: pValue,
            Estimate: difference,
            NullValue: 0,
            StandardError: standardError,
            Lower: difference - critical * standardError,
            Upper: difference + critical * standardError,
            Confidence: confidence,
            Alternative: Alternatives.ToWire(alternative)));
    }

    /// <summary><c>pairedT(first, second, options?)</c>: t de una muestra sobre las diferencias.</summary>
    public static Result<TTestResult> PairedT(
        IReadOnlyList<double> first,
        IReadOnlyList<double> second,
        double confidence = 0.95,
        Alternative alternative = Alternative.TwoSided)
    {
        var n = Math.Min(first.Count, second.Count);
        if (n < 2) return Result<TTestResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos 2 pares completos.");
        var differences = new double[n];
        for (var i = 0; i < n; i++) differences[i] = first[i] - second[i];
        return OneSampleT(differences, 0, confidence, alternative);
    }

    /// <summary><c>oneProportion(successes, trials, nullValue = 0.5, confidence = 0.95, alternative)</c>.</summary>
    public static Result<ProportionResult> OneProportion(
        double successes,
        double trials,
        double nullValue = 0.5,
        double confidence = 0.95,
        Alternative alternative = Alternative.TwoSided)
    {
        if (trials <= 0 || successes < 0 || successes > trials)
        {
            return Result<ProportionResult>.Err(DomainErrorCode.InvalidParameter, "Datos de proporción inválidos.");
        }
        var p = successes / trials;
        var standardError = Math.Sqrt(nullValue * (1 - nullValue) / trials);
        if (standardError == 0)
        {
            return Result<ProportionResult>.Err(DomainErrorCode.InvalidParameter, "La proporción nula debe estar entre 0 y 1.");
        }
        var z = (p - nullValue) / standardError;
        var pValue = InferenceDistributions.PValueFromStatistic(z, SpecialFunctions.NormalCdf, alternative);
        var interval = WilsonInterval(p, trials, confidence);
        return Result.Ok(new ProportionResult(
            Estimate: p,
            NullValue: nullValue,
            Z: z,
            PValue: pValue,
            Lower: interval.Lower,
            Upper: interval.Upper,
            Confidence: confidence));
    }

    /// <summary><c>twoProportions(successes1, trials1, successes2, trials2, confidence, alternative)</c>.</summary>
    public static Result<ProportionResult> TwoProportions(
        double successes1,
        double trials1,
        double successes2,
        double trials2,
        double confidence = 0.95,
        Alternative alternative = Alternative.TwoSided)
    {
        if (trials1 <= 0 || trials2 <= 0)
        {
            return Result<ProportionResult>.Err(DomainErrorCode.InvalidParameter, "Los tamaños de muestra deben ser positivos.");
        }
        var p1 = successes1 / trials1;
        var p2 = successes2 / trials2;
        var pooled = (successes1 + successes2) / (trials1 + trials2);
        var standardError = Math.Sqrt(pooled * (1 - pooled) * (1 / trials1 + 1 / trials2));
        if (standardError == 0)
        {
            return Result<ProportionResult>.Err(DomainErrorCode.InvalidInput, "No hay variabilidad para el contraste.");
        }
        var z = (p1 - p2) / standardError;
        var pValue = InferenceDistributions.PValueFromStatistic(z, SpecialFunctions.NormalCdf, alternative);
        var alpha = 1 - confidence;
        // paridad: tests.ts:222 — el crítico es siempre el bilateral, sin mirar la alternativa.
        var critical = SpecialFunctions.NormalInv(1 - alpha / 2);
        var seDiff = Math.Sqrt(p1 * (1 - p1) / trials1 + p2 * (1 - p2) / trials2);
        return Result.Ok(new ProportionResult(
            Estimate: p1 - p2,
            NullValue: 0,
            Z: z,
            PValue: pValue,
            Lower: p1 - p2 - critical * seDiff,
            Upper: p1 - p2 + critical * seDiff,
            Confidence: confidence));
    }

    /// <summary><c>oneVariance(sampleVariance, n, nullValue = 1, confidence = 0.95, alternative)</c>.</summary>
    public static Result<VarianceTestResult> OneVariance(
        double sampleVariance,
        double n,
        double nullValue = 1,
        double confidence = 0.95,
        Alternative alternative = Alternative.TwoSided)
    {
        if (!(sampleVariance >= 0) || n < 2 || !(nullValue > 0))
        {
            return Result<VarianceTestResult>.Err(DomainErrorCode.InvalidParameter, "Parámetros de varianza inválidos.");
        }
        var df = n - 1;
        var statistic = df * sampleVariance / nullValue;
        var pValue = InferenceDistributions.PValueFromStatistic(
            statistic,
            x => x <= 0 ? 0 : SpecialFunctions.Gammp(df / 2, x / 2),
            alternative);
        var alpha = 1 - confidence;
        var chiLower = alternative == Alternative.TwoSided
            ? ChiSquareInv(alpha / 2, df)
            : ChiSquareInv(alpha, df);
        var chiUpper = alternative == Alternative.TwoSided
            ? ChiSquareInv(1 - alpha / 2, df)
            : ChiSquareInv(1 - alpha, df);
        return Result.Ok(new VarianceTestResult(
            Statistic: statistic,
            Df: df,
            PValue: pValue,
            Ratio: sampleVariance / nullValue,
            Lower: df * sampleVariance / chiUpper,
            Upper: df * sampleVariance / chiLower,
            Confidence: confidence));
    }

    /// <summary><c>twoVariances(variance1, n1, variance2, n2, confidence, alternative)</c>.</summary>
    public static Result<VarianceTestResult> TwoVariances(
        double variance1,
        double n1,
        double variance2,
        double n2,
        double confidence = 0.95,
        Alternative alternative = Alternative.TwoSided)
    {
        if (!(variance1 >= 0) || !(variance2 > 0) || n1 < 2 || n2 < 2)
        {
            return Result<VarianceTestResult>.Err(DomainErrorCode.InvalidParameter, "Parámetros de varianza inválidos.");
        }
        var df1 = n1 - 1;
        var df2 = n2 - 1;
        var statistic = variance1 / variance2;
        var pValue = InferenceDistributions.PValueFromStatistic(
            statistic,
            x => x <= 0 ? 0 : SpecialFunctions.Betai(df1 / 2, df2 / 2, df1 * x / (df1 * x + df2)),
            alternative);
        var alpha = 1 - confidence;
        // paridad: tests.ts:293 — los cuantiles F son siempre bilaterales.
        var fLower = FQuantile(alpha / 2, df1, df2);
        var fUpper = FQuantile(1 - alpha / 2, df1, df2);
        return Result.Ok(new VarianceTestResult(
            Statistic: statistic,
            Df: df1,
            PValue: pValue,
            Ratio: statistic,
            Lower: statistic / fUpper,
            Upper: statistic / fLower,
            Confidence: confidence));
    }

    /// <summary><c>chiSquareGoodnessOfFit(observed, expected)</c>.</summary>
    public static Result<ChiSquareResult> ChiSquareGoodnessOfFit(
        IReadOnlyList<double> observed,
        IReadOnlyList<double> expected)
    {
        if (observed.Count != expected.Count || observed.Count < 2)
        {
            return Result<ChiSquareResult>.Err(DomainErrorCode.InvalidInput, "Categorías incompatibles.");
        }
        foreach (var value in observed)
        {
            if (value < 0)
            {
                return Result<ChiSquareResult>.Err(
                    DomainErrorCode.InvalidInput,
                    "Las frecuencias deben ser no negativas y las esperadas positivas.");
            }
        }
        foreach (var value in expected)
        {
            if (value <= 0)
            {
                return Result<ChiSquareResult>.Err(
                    DomainErrorCode.InvalidInput,
                    "Las frecuencias deben ser no negativas y las esperadas positivas.");
            }
        }
        var statistic = 0.0;
        for (var i = 0; i < observed.Count; i++)
        {
            var difference = observed[i] - expected[i];
            statistic += difference * difference / expected[i];
        }
        var df = observed.Count - 1.0;
        return Result.Ok(new ChiSquareResult(statistic, df, SpecialFunctions.Gammq(df / 2, statistic / 2)));
    }

    /// <summary><c>chiSquareIndependence(table)</c>.</summary>
    public static Result<ChiSquareIndependenceResult> ChiSquareIndependence(
        IReadOnlyList<IReadOnlyList<double>> table)
    {
        var rows = table.Count;
        var cols = rows > 0 ? table[0].Count : 0;
        if (rows < 2 || cols < 2)
        {
            return Result<ChiSquareIndependenceResult>.Err(DomainErrorCode.InvalidInput, "La tabla debe tener al menos 2x2.");
        }

        var rowTotals = new double[rows];
        for (var i = 0; i < rows; i++)
        {
            var total = 0.0;
            for (var j = 0; j < table[i].Count; j++) total += table[i][j];
            rowTotals[i] = total;
        }
        var colTotals = new double[cols];
        for (var j = 0; j < cols; j++)
        {
            var total = 0.0;
            for (var i = 0; i < rows; i++) total += table[i][j];
            colTotals[j] = total;
        }
        var grandTotal = NumericMath.Sum(rowTotals);

        var statistic = 0.0;
        var expected = new List<IReadOnlyList<double>>(rows);
        for (var i = 0; i < rows; i++)
        {
            var row = new double[cols];
            for (var j = 0; j < cols; j++)
            {
                var value = rowTotals[i] * colTotals[j] / grandTotal;
                row[j] = value;
                if (value > 0) statistic += Math.Pow(table[i][j] - value, 2) / value;
            }
            expected.Add(row);
        }

        var df = (rows - 1.0) * (cols - 1.0);
        return Result.Ok(new ChiSquareIndependenceResult(
            statistic,
            df,
            SpecialFunctions.Gammq(df / 2, statistic / 2),
            expected));
    }

    /// <summary><c>andersonDarling(values)</c>.</summary>
    public static Result<AndersonDarlingResult> AndersonDarling(IReadOnlyList<double> values)
    {
        var n = (double)values.Count;
        if (n < 8) return Result<AndersonDarlingResult>.Err(DomainErrorCode.InsufficientData, "Se recomiendan al menos 8 observaciones.");
        var center = NumericMath.Mean(values);
        var deviation = NumericMath.Sd(values);
        if (!(deviation > 0)) return Result<AndersonDarlingResult>.Err(DomainErrorCode.InvalidInput, "Los datos no tienen variabilidad.");
        var ordered = NumericMath.Sorted(values);
        var count = values.Count;
        var sumA = 0.0;
        for (var i = 1; i <= count; i++)
        {
            var f1 = JsMath.Min(1 - 1e-15, JsMath.Max(1e-15, SpecialFunctions.NormalCdf((ordered[i - 1] - center) / deviation)));
            var f2 = JsMath.Min(1 - 1e-15, JsMath.Max(1e-15, SpecialFunctions.NormalCdf((ordered[count - i] - center) / deviation)));
            sumA += (2 * i - 1) * (Math.Log(f1) + Math.Log(1 - f2));
        }
        var statistic = -n - sumA / n;
        var adjusted = statistic * (1 + 0.75 / n + 2.25 / (n * n));
        double pValue;
        if (adjusted < 0.2)
        {
            pValue = 1 - Math.Exp(-13.436 + 101.14 * adjusted - 223.73 * adjusted * adjusted);
        }
        else if (adjusted < 0.34)
        {
            pValue = 1 - Math.Exp(-8.318 + 42.796 * adjusted - 59.938 * adjusted * adjusted);
        }
        else if (adjusted < 0.6)
        {
            pValue = Math.Exp(0.9177 - 4.279 * adjusted - 1.38 * adjusted * adjusted);
        }
        else
        {
            pValue = Math.Exp(1.2937 - 5.709 * adjusted + 0.0186 * adjusted * adjusted);
        }
        return Result.Ok(new AndersonDarlingResult(statistic, JsMath.Min(1, JsMath.Max(0, pValue)), n));
    }

    /// <summary><c>signTest(values, nullMedian = 0, alternative = 'two-sided')</c>.</summary>
    public static Result<SignTestResult> SignTest(
        IReadOnlyList<double> values,
        double nullMedian = 0,
        Alternative alternative = Alternative.TwoSided)
    {
        var positives = 0.0;
        var negatives = 0.0;
        foreach (var value in values)
        {
            if (value > nullMedian) positives += 1;
            else if (value < nullMedian) negatives += 1;
        }
        var n = positives + negatives;
        if (n == 0)
        {
            return Result<SignTestResult>.Err(
                DomainErrorCode.InsufficientData,
                "Todas las observaciones son iguales a la mediana nula.");
        }
        var k = JsMath.Min(positives, negatives);
        var cumulative = 0.0;
        for (var i = 0.0; i <= k; i += 1) cumulative += Math.Exp(LogCombinationLocal(n, i) - n * Math.Log(2));
        var pValue = alternative switch
        {
            Alternative.TwoSided => JsMath.Min(1, 2 * cumulative),
            Alternative.Less => cumulative,
            _ => 1 - cumulative + Math.Exp(LogCombinationLocal(n, 0) - n * Math.Log(2))
        };
        return Result.Ok(new SignTestResult(positives, negatives, pValue));
    }

    /// <summary><c>wilcoxonSignedRank(values, nullMedian = 0, alternative = 'two-sided')</c>.</summary>
    public static Result<WilcoxonSignedRankResult> WilcoxonSignedRank(
        IReadOnlyList<double> values,
        double nullMedian = 0,
        Alternative alternative = Alternative.TwoSided)
    {
        var differences = new List<double>(values.Count);
        foreach (var value in values)
        {
            if (value - nullMedian != 0) differences.Add(value - nullMedian);
        }
        var n = (double)differences.Count;
        if (n < 5)
        {
            return Result<WilcoxonSignedRankResult>.Err(
                DomainErrorCode.InsufficientData,
                "Se necesitan al menos 5 diferencias no nulas.");
        }
        var absolute = new double[differences.Count];
        for (var i = 0; i < differences.Count; i++) absolute[i] = Math.Abs(differences[i]);
        var ranksOf = NumericMath.Ranks(absolute);
        var wPlus = 0.0;
        for (var i = 0; i < differences.Count; i++)
        {
            if (differences[i] > 0) wPlus += ranksOf[i];
        }
        var meanW = n * (n + 1) / 4;
        var varianceW = n * (n + 1) * (2 * n + 1) / 24;
        var z = (wPlus - meanW) / Math.Sqrt(varianceW);
        var pValue = InferenceDistributions.PValueFromStatistic(z, SpecialFunctions.NormalCdf, alternative);
        return Result.Ok(new WilcoxonSignedRankResult(wPlus, pValue, n));
    }

    /// <summary><c>mannWhitney(first, second, alternative = 'two-sided')</c>.</summary>
    public static Result<MannWhitneyResult> MannWhitney(
        IReadOnlyList<double> first,
        IReadOnlyList<double> second,
        Alternative alternative = Alternative.TwoSided)
    {
        var n1 = (double)first.Count;
        var n2 = (double)second.Count;
        if (n1 < 1 || n2 < 1)
        {
            return Result<MannWhitneyResult>.Err(DomainErrorCode.InsufficientData, "Ambas muestras deben tener datos.");
        }
        var combined = new double[first.Count + second.Count];
        for (var i = 0; i < first.Count; i++) combined[i] = first[i];
        for (var i = 0; i < second.Count; i++) combined[first.Count + i] = second[i];
        var rankOf = NumericMath.Ranks(combined);
        var r1 = 0.0;
        for (var i = 0; i < first.Count; i++) r1 += rankOf[i];
        var u1 = r1 - n1 * (n1 + 1) / 2;
        var u2 = n1 * n2 - u1;
        var meanU = n1 * n2 / 2;
        var varianceU = n1 * n2 * (n1 + n2 + 1) / 12;
        var z = (u1 - meanU) / Math.Sqrt(varianceU);
        var pValue = InferenceDistributions.PValueFromStatistic(z, SpecialFunctions.NormalCdf, alternative);
        return Result.Ok(new MannWhitneyResult(JsMath.Min(u1, u2), pValue, u1, u2));
    }

    /// <summary><c>kruskalWallis(groups)</c>.</summary>
    public static Result<KruskalWallisResult> KruskalWallis(IReadOnlyList<IReadOnlyList<double>> groups)
    {
        var total = 0.0;
        foreach (var group in groups) total += group.Count;
        if (groups.Count < 2 || total < 3)
        {
            return Result<KruskalWallisResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos 2 grupos con datos.");
        }
        var combined = new List<double>();
        foreach (var group in groups) combined.AddRange(group);
        var rankOf = NumericMath.Ranks(combined);
        var offset = 0;
        var h = 0.0;
        foreach (var group in groups)
        {
            var rankSum = 0.0;
            for (var i = 0; i < group.Count; i++) rankSum += rankOf[offset + i];
            offset += group.Count;
            h += rankSum * rankSum / group.Count;
        }
        h = 12 / (total * (total + 1)) * h - 3 * (total + 1);
        var df = groups.Count - 1.0;
        return Result.Ok(new KruskalWallisResult(h, df, SpecialFunctions.Gammq(df / 2, h / 2)));
    }

    /// <summary><c>runsTest(values)</c>.</summary>
    public static Result<RunsTestResult> RunsTest(IReadOnlyList<double> values)
    {
        var n = values.Count;
        if (n < 10) return Result<RunsTestResult>.Err(DomainErrorCode.InsufficientData, "Se recomiendan al menos 10 observaciones.");
        var medianValue = NumericMath.Quantile(values, 0.5);
        var above = 0.0;
        var below = 0.0;
        foreach (var value in values)
        {
            if (value > medianValue) above += 1;
            else if (value < medianValue) below += 1;
        }
        if (above < 1 || below < 1)
        {
            return Result<RunsTestResult>.Err(
                DomainErrorCode.InvalidInput,
                "Todos los valores están por encima o por debajo de la mediana.");
        }
        var runs = 1.0;
        for (var i = 1; i < n; i++)
        {
            var previous = values[i - 1] > medianValue;
            var current = values[i] > medianValue;
            if (previous != current) runs += 1;
        }
        var expectedRuns = 2 * above * below / (above + below) + 1;
        var varianceRuns = 2 * above * below * (2 * above * below - above - below) /
                           (Math.Pow(above + below, 2) * (above + below - 1));
        var z = (runs - expectedRuns) / Math.Sqrt(varianceRuns);
        var pValue = 2 * JsMath.Min(SpecialFunctions.NormalCdf(z), 1 - SpecialFunctions.NormalCdf(z));
        return Result.Ok(new RunsTestResult(runs, z, pValue));
    }

    /// <summary><c>spearmanCorrelation(x, y)</c>.</summary>
    public static Result<SpearmanCorrelationResult> SpearmanCorrelation(
        IReadOnlyList<double> x,
        IReadOnlyList<double> y)
    {
        var n = Math.Min(x.Count, y.Count);
        if (n < 3) return Result<SpearmanCorrelationResult>.Err(DomainErrorCode.InsufficientData, "Se necesitan al menos 3 pares.");
        var rx = NumericMath.Ranks(NumericMath.Slice(x, n));
        var ry = NumericMath.Ranks(NumericMath.Slice(y, n));
        var meanRx = NumericMath.Mean(rx);
        var meanRy = NumericMath.Mean(ry);
        var numerator = 0.0;
        var denomX = 0.0;
        var denomY = 0.0;
        for (var i = 0; i < n; i++)
        {
            numerator += (rx[i] - meanRx) * (ry[i] - meanRy);
            denomX += Math.Pow(rx[i] - meanRx, 2);
            denomY += Math.Pow(ry[i] - meanRy, 2);
        }
        var rho = numerator / Math.Sqrt(denomX * denomY);
        var t = rho * Math.Sqrt((n - 2) / (1 - rho * rho));
        var pValue = 2 * JsMath.Min(
            InferenceDistributions.TCdf(t, n - 2),
            1 - InferenceDistributions.TCdf(t, n - 2));
        return Result.Ok(new SpearmanCorrelationResult(rho, pValue));
    }

    /// <summary><c>tostTwoSample(first, second, margin, confidence = 0.95)</c>.</summary>
    public static Result<TostTwoSampleResult> TostTwoSample(
        IReadOnlyList<double> first,
        IReadOnlyList<double> second,
        double margin,
        double confidence = 0.95)
    {
        if (!(margin > 0))
        {
            return Result<TostTwoSampleResult>.Err(
                DomainErrorCode.InvalidParameter,
                "El margen de equivalencia debe ser positivo.");
        }
        double n1 = first.Count;
        double n2 = second.Count;
        if (n1 < 2 || n2 < 2)
        {
            return Result<TostTwoSampleResult>.Err(
                DomainErrorCode.InsufficientData,
                "Cada muestra necesita al menos 2 observaciones.");
        }
        var difference = NumericMath.Mean(first) - NumericMath.Mean(second);
        var standardError = Math.Sqrt(Math.Pow(NumericMath.Sd(first), 2) / n1 + Math.Pow(NumericMath.Sd(second), 2) / n2);
        if (standardError == 0)
        {
            return Result<TostTwoSampleResult>.Err(DomainErrorCode.InvalidInput, "No hay variabilidad.");
        }
        var df = n1 + n2 - 2;
        var lowerP = InferenceDistributions.TCdf((difference + margin) / standardError, df);
        var upperP = 1 - InferenceDistributions.TCdf((difference - margin) / standardError, df);
        return Result.Ok(new TostTwoSampleResult(lowerP, upperP, JsMath.Max(lowerP, upperP), difference));
    }

    private static double ChiSquareInv(double p, double df) =>
        JsMath.BisectInverse(x => SpecialFunctions.Gammp(df / 2, x / 2), p, 0, 1e6);

    private static double FQuantile(double p, double df1, double df2) =>
        JsMath.BisectInverse(
            x => x <= 0 ? 0 : SpecialFunctions.Betai(df1 / 2, df2 / 2, df1 * x / (df1 * x + df2)),
            p,
            0,
            1e8);

    private static (double Lower, double Upper) WilsonInterval(double p, double n, double confidence)
    {
        var z = SpecialFunctions.NormalInv(1 - (1 - confidence) / 2);
        var denominator = 1 + z * z / n;
        var center = (p + z * z / (2 * n)) / denominator;
        var half = z * Math.Sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / denominator;
        return (JsMath.Max(0, center - half), JsMath.Min(1, center + half));
    }

    // paridad: tests.ts:398 — logCombinationLocal acumula log(n - i) - log(i + 1).
    private static double LogCombinationLocal(double n, double k)
    {
        var result = 0.0;
        for (var i = 0.0; i < k; i += 1) result += Math.Log(n - i) - Math.Log(i + 1);
        return result;
    }
}
