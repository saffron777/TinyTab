using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>Carga y ordenación de una serie temporal con el criterio de Sigma (recorte de extremos y periodos).</summary>
internal sealed record LoadedSeries(
    string Name,
    double[] Values,
    string[] Labels,
    int Period,
    int Dropped,
    bool Dated,
    bool Irregular,
    DateTime? LastDate,
    string Frequency);

internal static class SeriesLoader
{
    public static Result<LoadedSeries> Load(
        Frame frame,
        string seriesColumn,
        string? timeColumn,
        int requestedPeriod,
        int minimum = 10)
    {
        var values = frame.Numeric(seriesColumn);
        var order = Enumerable.Range(0, frame.RowCount).ToArray();
        var dated = false;
        var irregular = false;
        DateTime?[]? dates = null;
        if (!string.IsNullOrEmpty(timeColumn))
        {
            var column = frame.Column(timeColumn);
            if (column.Kind == DataColumnKind.DateTime)
            {
                dates = column.Dates;
                order = order.OrderBy(index => dates[index] ?? DateTime.MaxValue).ToArray();
                dated = true;
            }
            else
            {
                order = order.OrderBy(index => column.TextAt(index), StringComparer.Ordinal).ToArray();
            }
            var sorted = order.Select(index => dates?[index]).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
            if (dated && sorted.Length >= 3)
            {
                var gaps = sorted.Zip(sorted.Skip(1), (first, second) => (second - first).TotalDays).OrderBy(value => value).ToArray();
                var median = gaps[gaps.Length / 2];
                irregular = gaps.Any(gap => Math.Abs(gap - median) > Math.Max(1, median * 0.1));
            }
        }
        var sortedValues = order.Select(index => values[index]).ToArray();
        var first = 0;
        while (first < sortedValues.Length && double.IsNaN(sortedValues[first])) first++;
        var last = sortedValues.Length - 1;
        while (last >= first && double.IsNaN(sortedValues[last])) last--;
        var dropped = sortedValues.Length - (last - first + 1);
        if (first > last) return Result.Err<LoadedSeries>(DomainErrorCode.InvalidInput, $"«{seriesColumn}» no tiene valores numéricos.");
        var trimmed = sortedValues[first..(last + 1)];
        if (trimmed.Any(double.IsNaN))
        {
            return Result.Err<LoadedSeries>(DomainErrorCode.InvalidInput, $"«{seriesColumn}» tiene valores faltantes dentro de la serie. Reemplácelos antes de analizar.");
        }
        if (trimmed.Length < minimum)
        {
            return Result.Err<LoadedSeries>(DomainErrorCode.InvalidInput, $"La serie necesita al menos {minimum} observaciones válidas (tiene {trimmed.Length}).");
        }
        var labels = new string[trimmed.Length];
        for (var index = 0; index < trimmed.Length; index++)
        {
            var source = order[first + index];
            labels[index] = dates is not null && dates[source] is { } date
                ? date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                : JsNumber.ToString(index + 1);
        }
        var period = requestedPeriod;
        var frequency = "none";
        DateTime? lastDate = null;
        if (dates is not null)
        {
            var validDates = dates.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
            if (validDates.Length > 0) lastDate = validDates[^1];
            if (validDates.Length >= 3)
            {
                var gaps = validDates.Zip(validDates.Skip(1), (first, second) => (second - first).TotalDays).OrderBy(value => value).ToArray();
                var median = gaps[gaps.Length / 2];
                frequency = median switch
                {
                    >= 300 => "yearly",
                    >= 80 => "quarterly",
                    >= 25 => "monthly",
                    >= 6 => "weekly",
                    >= 0.9 => "daily",
                    _ => "hourly"
                };
            }
        }
        if (period <= 1 && dates is not null)
        {
            period = InferPeriod(dates.Where(value => value.HasValue).Select(value => value!.Value).ToArray());
        }
        return Result.Ok(new LoadedSeries(seriesColumn, trimmed, labels, period, dropped, dated, irregular, lastDate, frequency));
    }

    private static int InferPeriod(DateTime[] dates)
    {
        if (dates.Length < 3) return 1;
        var gaps = dates.Zip(dates.Skip(1), (first, second) => (second - first).TotalDays).OrderBy(value => value).ToArray();
        var median = gaps[gaps.Length / 2];
        return median switch
        {
            >= 300 => 1,
            >= 80 => 4,
            >= 25 => 12,
            >= 6 => 7,
            >= 0.9 => 7,
            _ => 24
        };
    }
}
