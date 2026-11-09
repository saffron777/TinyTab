using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace StatisticEngine.Domain.Numerics;

/// <summary>
/// Conversión número ↔ texto con la semántica exacta de ECMAScript
/// (<c>Number::toString</c>, <c>Number.prototype.toFixed</c>, <c>toExponential</c> y <c>Number(text)</c>).
/// Es necesaria para que las tablas y etiquetas del motor coincidan carácter a carácter.
/// </summary>
public static partial class JsNumber
{
    /// <summary><c>String(number)</c> / <c>Number::toString(x, 10)</c>.</summary>
    public static string ToString(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (value == 0) return "0";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";
        if (value < 0) return $"-{ToString(-value)}";

        var digitsAndPoint = ShortestDigits(value, out var point);
        var k = digitsAndPoint.Length;
        var n = point;

        if (k <= n && n <= 21) return digitsAndPoint + new string('0', n - k);
        if (0 < n && n <= 21) return $"{digitsAndPoint[..n]}.{digitsAndPoint[n..]}";
        if (-6 < n && n <= 0) return $"0.{new string('0', -n)}{digitsAndPoint}";

        var exponent = n - 1;
        var mantissa = k == 1 ? digitsAndPoint : $"{digitsAndPoint[..1]}.{digitsAndPoint[1..]}";
        return $"{mantissa}e{(exponent >= 0 ? "+" : "-")}{Math.Abs(exponent).ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary><c>Number.prototype.toFixed(fractionDigits)</c>.</summary>
    public static string ToFixed(double value, int fractionDigits)
    {
        if (!double.IsFinite(value)) return ToString(value);
        var f = Math.Clamp(fractionDigits, 0, 100);
        var negative = value < 0;
        if (negative) value = -value;
        if (value >= 1e21) return $"{(negative ? "-" : string.Empty)}{ToString(value)}";

        var (numerator, scale) = ExactDecimal(value);
        BigInteger scaled;
        if (f >= scale)
        {
            scaled = numerator * BigInteger.Pow(10, f - scale);
        }
        else
        {
            var divisor = BigInteger.Pow(10, scale - f);
            var quotient = BigInteger.DivRem(numerator, divisor, out var remainder);
            // ECMAScript: si hay empate se elige el entero mayor (mitad hacia arriba).
            if (remainder * 2 >= divisor) quotient += 1;
            scaled = quotient;
        }

        var sign = negative ? "-" : string.Empty;
        if (scaled.IsZero) return f == 0 ? $"{sign}0" : $"{sign}0.{new string('0', f)}";

        var digits = scaled.ToString(CultureInfo.InvariantCulture);
        if (f == 0) return sign + digits;
        if (digits.Length <= f) digits = new string('0', f - digits.Length + 1) + digits;
        var pointIndex = digits.Length - f;
        return $"{sign}{digits[..pointIndex]}.{digits[pointIndex..]}";
    }

    /// <summary><c>Number.prototype.toExponential(fractionDigits)</c>.</summary>
    public static string ToExponential(double value, int fractionDigits)
    {
        if (!double.IsFinite(value)) return ToString(value);
        var f = Math.Clamp(fractionDigits, 0, 100);
        var negative = value < 0;
        if (negative) value = -value;
        var sign = negative ? "-" : string.Empty;

        if (value == 0)
        {
            var mantissaZero = f > 0 ? $"0.{new string('0', f)}" : "0";
            return $"{sign}{mantissaZero}e+0";
        }

        var (numerator, scale) = ExactDecimal(value);
        var digitCount = numerator.ToString(CultureInfo.InvariantCulture).Length;
        var exponent = digitCount - 1 - scale;
        var significant = f + 1;

        var rounded = numerator;
        if (digitCount > significant)
        {
            var divisor = BigInteger.Pow(10, digitCount - significant);
            var quotient = BigInteger.DivRem(rounded, divisor, out var remainder);
            if (remainder * 2 >= divisor) quotient += 1;
            rounded = quotient;
            if (rounded.ToString(CultureInfo.InvariantCulture).Length > significant)
            {
                rounded /= 10;
                exponent += 1;
            }
        }
        else if (digitCount < significant)
        {
            rounded *= BigInteger.Pow(10, significant - digitCount);
        }

        var digits = rounded.ToString(CultureInfo.InvariantCulture).PadLeft(significant, '0');
        var mantissa = f > 0 ? $"{digits[..1]}.{digits[1..]}" : digits;
        var exponentText = $"{(exponent >= 0 ? "e+" : "e-")}{Math.Abs(exponent).ToString(CultureInfo.InvariantCulture)}";
        return $"{sign}{mantissa}{exponentText}";
    }

    /// <summary><c>Number(text)</c> de ECMAScript (trim, decimal, exponente, hexadecimal, Infinity).</summary>
    public static double Parse(string? text)
    {
        if (text is null) return double.NaN;
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return 0;
        if (trimmed is "Infinity" or "+Infinity") return double.PositiveInfinity;
        if (trimmed == "-Infinity") return double.NegativeInfinity;

        var sign = 1.0;
        var body = trimmed;
        if (body[0] is '+' or '-')
        {
            if (body[0] == '-') sign = -1;
            body = body[1..];
        }
        if (body.Length == 0) return double.NaN;

        if (body.Length > 2 && body[0] == '0' && (body[1] is 'x' or 'X'))
        {
            var hex = body[2..];
            if (!HexRegex().IsMatch(hex)) return double.NaN;
            var magnitude = BigInteger.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return sign * (double)magnitude;
        }

        if (!DecimalRegex().IsMatch(body)) return double.NaN;
        return sign * double.Parse(body, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>Comprueba si el valor tiene representación decimal finita (para <c>Number.isInteger</c> etc.).</summary>
    public static bool IsDecimalLiteral(string text) => DecimalRegex().IsMatch(text);

    /// <summary>Representación decimal exacta de un double positivo: <c>value = numerator / 10^scale</c>.</summary>
    private static (BigInteger Numerator, int Scale) ExactDecimal(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var exponentBits = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xF_FFFF_FFFF_FFFFL;
        int exponent;
        if (exponentBits == 0)
        {
            exponent = 1 - 1023;
        }
        else
        {
            mantissa |= 1L << 52;
            exponent = exponentBits - 1023;
        }
        exponent -= 52;

        var numerator = new BigInteger(mantissa);
        if (exponent >= 0) return (numerator << exponent, 0);
        var scale = -exponent;
        return (numerator * BigInteger.Pow(5, scale), scale);
    }

    /// <summary>Dígitos significativos más cortos que reconstruyen el double, y posición del punto decimal.</summary>
    private static string ShortestDigits(double value, out int point)
    {
        var repr = value.ToString("R", CultureInfo.InvariantCulture);
        var exponentPart = 0;
        var eIndex = repr.IndexOfAny(['E', 'e']);
        var mantissa = repr;
        if (eIndex >= 0)
        {
            mantissa = repr[..eIndex];
            exponentPart = int.Parse(repr[(eIndex + 1)..], CultureInfo.InvariantCulture);
        }

        var pointIndex = mantissa.IndexOf('.');
        string intPart;
        string fracPart;
        if (pointIndex >= 0)
        {
            intPart = mantissa[..pointIndex];
            fracPart = mantissa[(pointIndex + 1)..];
        }
        else
        {
            intPart = mantissa;
            fracPart = string.Empty;
        }

        var digits = intPart + fracPart;
        var leadingZeros = 0;
        while (leadingZeros < digits.Length - 1 && digits[leadingZeros] == '0') leadingZeros++;
        digits = digits[leadingZeros..];

        var trimmed = digits.TrimEnd('0');
        if (trimmed.Length > 0) digits = trimmed;

        point = intPart.Length - leadingZeros + exponentPart;
        return digits;
    }

    [GeneratedRegex("^[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HexRegex();

    [GeneratedRegex(@"^(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalRegex();
}
