namespace StatisticEngine.Domain.Numerics;

/// <summary>
/// Formato de números del motor (paridad con <c>fmt</c> y <c>fmtP</c> de <c>numeric.ts</c>).
/// </summary>
public static class NumberFormat
{
    /// <summary>
    /// <c>fmt(value, digits = 4)</c>: exponencial para magnitudes ≥ 1e7 o &lt; 1e-4,
    /// decimal con ceros finales recortados en el resto; <c>∞</c>, <c>-∞</c> y <c>*</c> para no finitos.
    /// </summary>
    public static string Fmt(double value, int digits = 4)
    {
        if (!double.IsFinite(value))
        {
            if (double.IsNaN(value)) return "*";
            return value > 0 ? "∞" : "-∞";
        }
        if (value == 0) return "0";

        var magnitude = Math.Abs(value);
        if (magnitude >= 1e7 || magnitude < 1e-4) return JsNumber.ToExponential(value, digits);

        var text = JsNumber.ToFixed(value, Math.Max(0, digits));
        return TrimFractionZeros(text);
    }

    /// <summary><c>fmtP(value)</c>: <c>&lt;0.001</c> para valores pequeños, tres decimales en el resto.</summary>
    public static string FmtP(double value)
    {
        if (!double.IsFinite(value)) return "*";
        if (value < 0.001) return "<0.001";
        return Fmt(value, 3);
    }

    /// <summary>Equivalente a <c>n(value, digits)</c> de los helpers de análisis.</summary>
    public static string FormatOrMissing(double value, int digits = 4) =>
        double.IsFinite(value) ? Fmt(value, digits) : "*";

    /// <summary>Equivalente a <c>p(value)</c> de los helpers de análisis.</summary>
    public static string FormatPValue(double value) => double.IsFinite(value) ? FmtP(value) : "*";

    private static string TrimFractionZeros(string text)
    {
        var dot = text.IndexOf('.');
        if (dot < 0) return text;
        var end = text.Length;
        while (end > dot + 1 && text[end - 1] == '0') end--;
        var trimmed = text[..end];
        return trimmed[^1] == '.' ? trimmed[..^1] : trimmed;
    }
}
