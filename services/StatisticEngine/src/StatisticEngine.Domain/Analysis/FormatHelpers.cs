using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Analysis;

/// <summary>
/// Helpers de formato de los análisis (espeja <c>analysis/helpers.ts</c>): números con
/// <c>fmt</c>, valores p con <c>fmtP</c> y construcción de secciones.
/// </summary>
public static class FormatHelpers
{
    /// <summary><c>n(value, digits = 4)</c>: formatea o devuelve <c>*</c> si no es finito.</summary>
    public static string N(double value, int digits = 4) => NumberFormat.FormatOrMissing(value, digits);

    /// <summary><c>p(value)</c>: valor p con <c>fmtP</c>.</summary>
    public static string P(double value) => NumberFormat.FormatPValue(value);

    public static TableSection Table(
        string title,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<object?>> rows,
        IReadOnlyList<string>? align = null)
    {
        var formatted = rows.Select(row => (IReadOnlyList<string>)row.Select(Cell).ToArray()).ToArray();
        return new TableSection(title, headers, formatted, align);
    }

    public static TextSection Text(string title, string content) => new(title, content);

    /// <summary>Descarta secciones nulas, como <c>sections(...)</c> en el motor.</summary>
    public static IReadOnlyList<AnalysisSection> Sections(params AnalysisSection?[] items) =>
        items.Where(item => item is not null).Select(item => item!).ToArray();

    /// <summary>Equivalente a la conversión de celda de <c>table()</c>: números con <c>fmt</c>, resto con <c>String()</c>.</summary>
    private static string Cell(object? value) =>
        value switch
        {
            null => "*",
            string text => text,
            double number => N(number),
            float number => N(number),
            int number => N(number),
            long number => N(number),
            decimal number => N((double)number),
            bool flag => flag ? "true" : "false",
            _ => value.ToString() ?? "*"
        };
}
