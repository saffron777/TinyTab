using System.Globalization;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Frecuencia regular detectada en la columna de tiempo (paridad con <c>pandas.infer_freq</c>).</summary>
internal enum SeriesFrequency
{
    None,
    Hourly,
    Daily,
    Weekly,
    Monthly,
    Quarterly,
    Semiannual,
    Yearly
}

/// <summary>Varias series observadas juntas, en orden temporal y sin faltantes interiores.</summary>
internal sealed record MultiSeries(
    IReadOnlyList<string> Names,
    double[][] Values,
    IReadOnlyList<string> Labels,
    bool Dated,
    SeriesFrequency Frequency,
    int Period,
    int Dropped)
{
    public int K => Names.Count;

    public int N => Values.Length == 0 ? 0 : Values[0].Length;

    /// <summary>Etiquetas de los periodos futuros (fechas si la serie es regular, si no 1..n+h).</summary>
    public string[] FutureLabels(int horizon)
    {
        var output = new string[horizon];
        if (Dated && Frequency != SeriesFrequency.None && Labels.Count > 0
            && DateTime.TryParseExact(
                Labels[^1],
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var last))
        {
            var current = last;
            for (var step = 0; step < horizon; step++)
            {
                current = Frequency switch
                {
                    SeriesFrequency.Hourly => current.AddHours(1),
                    SeriesFrequency.Daily => current.AddDays(1),
                    SeriesFrequency.Weekly => current.AddDays(7),
                    SeriesFrequency.Monthly => current.AddMonths(1),
                    SeriesFrequency.Quarterly => current.AddMonths(3),
                    SeriesFrequency.Semiannual => current.AddMonths(6),
                    _ => current.AddYears(1)
                };
                output[step] = current.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            return output;
        }

        for (var step = 0; step < horizon; step++)
        {
            output[step] = JsNumber.ToString(Labels.Count + step + 1);
        }
        return output;
    }
}

/// <summary>
/// Carga de varias series numéricas alineadas por la columna de tiempo (port de
/// <c>ts_core.load_multi</c>): ordena por fecha, recorta los extremos incompletos y rechaza huecos.
/// </summary>
internal static class MultiSeriesLoader
{
    public static Result<MultiSeries> Load(
        Frame frame,
        IReadOnlyList<string> names,
        string? timeColumn,
        int minimum)
    {
        if (names.Count == 0)
        {
            return Result.Err<MultiSeries>(DomainErrorCode.InvalidInput, "No hay series para analizar.");
        }

        var columns = new double[names.Count][];
        for (var index = 0; index < names.Count; index++)
        {
            if (!frame.Has(names[index]))
            {
                return Result.Err<MultiSeries>(
                    DomainErrorCode.InvalidInput,
                    $"La columna «{names[index]}» no existe en el dataset.");
            }
            columns[index] = frame.Numeric(names[index]);
        }

        var order = Enumerable.Range(0, frame.RowCount).ToList();
        DateTime?[]? dates = null;
        if (!string.IsNullOrEmpty(timeColumn))
        {
            var column = frame.Column(timeColumn);
            if (column.Kind != DataColumnKind.DateTime)
            {
                return Result.Err<MultiSeries>(
                    DomainErrorCode.InvalidInput,
                    $"«{timeColumn}» no es una columna de fecha.");
            }
            dates = column.Dates;
            order = [.. order.Where(row => dates[row] is not null).OrderBy(row => dates[row]!.Value)];
            for (var index = 1; index < order.Count; index++)
            {
                if (dates[order[index]] == dates[order[index - 1]])
                {
                    return Result.Err<MultiSeries>(
                        DomainErrorCode.InvalidInput,
                        $"La columna de tiempo «{timeColumn}» tiene fechas repetidas.");
                }
            }
        }

        var complete = new bool[order.Count];
        for (var index = 0; index < order.Count; index++)
        {
            var row = order[index];
            var ok = true;
            foreach (var column in columns)
            {
                if (double.IsNaN(column[row]))
                {
                    ok = false;
                    break;
                }
            }
            complete[index] = ok;
        }

        var first = Array.IndexOf(complete, true);
        if (first < 0)
        {
            return Result.Err<MultiSeries>(
                DomainErrorCode.InvalidInput,
                "No hay filas con todas las series completas.");
        }
        var last = Array.LastIndexOf(complete, true);
        for (var index = first; index <= last; index++)
        {
            if (!complete[index])
            {
                return Result.Err<MultiSeries>(
                    DomainErrorCode.InvalidInput,
                    "Alguna serie tiene faltantes dentro del periodo común. Reemplácelos antes (Transformar datos → Valores faltantes).");
            }
        }

        var n = last - first + 1;
        if (n < minimum)
        {
            return Result.Err<MultiSeries>(
                DomainErrorCode.InvalidInput,
                $"Las series necesitan al menos {minimum} observaciones comunes (hay {n}).");
        }

        var values = new double[names.Count][];
        for (var index = 0; index < names.Count; index++)
        {
            var series = new double[n];
            for (var row = 0; row < n; row++) series[row] = columns[index][order[first + row]];
            values[index] = series;
        }

        var labels = new List<string>(n);
        var dated = false;
        var frequency = SeriesFrequency.None;
        if (dates is not null)
        {
            var stamps = new DateTime[n];
            for (var row = 0; row < n; row++) stamps[row] = dates[order[first + row]]!.Value;
            frequency = DetectFrequency(stamps);
            if (frequency != SeriesFrequency.None)
            {
                dated = true;
                foreach (var stamp in stamps) labels.Add(stamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
        }
        if (!dated)
        {
            for (var row = 0; row < n; row++) labels.Add(JsNumber.ToString(row + 1));
        }

        var period = frequency switch
        {
            SeriesFrequency.Monthly => 12,
            SeriesFrequency.Quarterly => 4,
            SeriesFrequency.Semiannual => 2,
            SeriesFrequency.Weekly => 52,
            SeriesFrequency.Daily => 7,
            SeriesFrequency.Hourly => 24,
            _ => 1
        };

        return Result.Ok(new MultiSeries(names, values, labels, dated, frequency, period, order.Count - n));
    }

    /// <summary>Frecuencia regular de las fechas, probando avances calendario como <c>infer_freq</c>.</summary>
    private static SeriesFrequency DetectFrequency(DateTime[] stamps)
    {
        if (stamps.Length < 3) return SeriesFrequency.None;
        if (Matches(stamps, stamp => stamp.AddHours(1))) return SeriesFrequency.Hourly;
        if (Matches(stamps, stamp => stamp.AddDays(1))) return SeriesFrequency.Daily;
        if (Matches(stamps, stamp => stamp.AddDays(7))) return SeriesFrequency.Weekly;
        if (Matches(stamps, stamp => stamp.AddMonths(1))) return SeriesFrequency.Monthly;
        if (Matches(stamps, stamp => stamp.AddMonths(3))) return SeriesFrequency.Quarterly;
        if (Matches(stamps, stamp => stamp.AddMonths(6))) return SeriesFrequency.Semiannual;
        if (Matches(stamps, stamp => stamp.AddYears(1))) return SeriesFrequency.Yearly;
        return SeriesFrequency.None;
    }

    private static bool Matches(DateTime[] stamps, Func<DateTime, DateTime> advance)
    {
        for (var index = 0; index < stamps.Length - 1; index++)
        {
            if (advance(stamps[index]) != stamps[index + 1]) return false;
        }
        return true;
    }
}
