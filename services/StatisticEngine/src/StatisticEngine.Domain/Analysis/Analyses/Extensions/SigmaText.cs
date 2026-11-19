using System.Globalization;

namespace StatisticEngine.Domain.Analysis.Analyses.Extensions;

/// <summary>
/// Formato de los textos de conclusión portados desde Sigma. Se replica literalmente el formato de
/// sus f-strings (<c>.4f</c>, <c>:g</c>) para que los mensajes coincidan carácter a carácter.
/// </summary>
internal static class SigmaText
{
    /// <summary><c>f"{value:.4f}"</c> de Python (siempre cuatro decimales).</summary>
    public static string Fixed4(double value) => value.ToString("F4", CultureInfo.InvariantCulture);

    /// <summary><c>pfmt(value)</c> de Sigma: <c>&lt; 0.0001</c> o cuatro decimales.</summary>
    public static string PValue(double value) => value < 1e-4 ? "< 0.0001" : Fixed4(value);

    /// <summary><c>f"{value:g}"</c> para los valores de α del catálogo (0,001 – 0,5).</summary>
    public static string General(double value) => value.ToString("G6", CultureInfo.InvariantCulture);

    /// <summary>
    /// Alerta de decisión de Sigma: <c>p = 0,0123 &lt; α = 0,05: se rechaza H₀ (…)</c>.
    /// </summary>
    public static AlertSection Decision(double pValue, double alpha, string nullHypothesis)
    {
        var reject = pValue < alpha;
        var message =
            $"p {(pValue >= 1e-4 ? "=" : string.Empty)} {PValue(pValue)} {(reject ? "<" : "≥")} α = {General(alpha)}: "
            + $"{(reject ? "se rechaza" : "no se rechaza")} H₀ ({nullHypothesis}).";
        return new AlertSection("Conclusión", reject ? "warning" : "success", message);
    }

    /// <summary><c>need(values, name, count)</c> de Sigma: mínimo de datos válidos.</summary>
    public static string NeedMessage(string name, int count) =>
        $"«{name}» necesita al menos {count} datos válidos para este gráfico.";
}
