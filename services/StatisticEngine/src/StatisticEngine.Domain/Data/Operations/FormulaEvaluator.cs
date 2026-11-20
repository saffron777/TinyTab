namespace StatisticEngine.Domain.Data.Operations;

/// <summary>Evaluador de fórmulas sencillas para las operaciones de datos.</summary>
internal sealed class FormulaEvaluator
{
    private readonly string _text;
    private readonly Func<string, double> _resolve;
    private int _position;

    private FormulaEvaluator(string text, Func<string, double> resolve)
    {
        _text = text;
        _resolve = resolve;
    }

    public static double Evaluate(string expression, Func<string, double> resolve)
    {
        var evaluator = new FormulaEvaluator(expression, resolve);
        var value = evaluator.ParseOr();
        evaluator.SkipSpaces();
        if (evaluator._position < evaluator._text.Length) throw new FormatException("Sobra texto en la fórmula.");
        return value;
    }

    private double ParseOr()
    {
        var value = ParseAnd();
        while (true)
        {
            SkipSpaces();
            if (Match("|") || MatchWord("OR"))
            {
                var right = ParseAnd();
                value = value != 0 || right != 0 ? 1 : 0;
            }
            else return value;
        }
    }

    private double ParseAnd()
    {
        var value = ParseComparison();
        while (true)
        {
            SkipSpaces();
            if (Match("&") || MatchWord("AND"))
            {
                var right = ParseComparison();
                value = value != 0 && right != 0 ? 1 : 0;
            }
            else return value;
        }
    }

    private double ParseComparison()
    {
        var value = ParseAdditive();
        SkipSpaces();
        foreach (var (token, operation) in new (string, Func<double, double, double>)[]
                 {
                     (">=", (a, b) => a >= b ? 1 : 0), ("<=", (a, b) => a <= b ? 1 : 0),
                     ("==", (a, b) => Math.Abs(a - b) < 1e-12 ? 1 : 0), ("!=", (a, b) => Math.Abs(a - b) >= 1e-12 ? 1 : 0),
                     (">", (a, b) => a > b ? 1 : 0), ("<", (a, b) => a < b ? 1 : 0)
                 })
        {
            if (!Match(token)) continue;
            var right = ParseAdditive();
            return operation(value, right);
        }
        return value;
    }

    private double ParseAdditive()
    {
        var value = ParseMultiplicative();
        while (true)
        {
            SkipSpaces();
            if (Match("+")) value += ParseMultiplicative();
            else if (Match("-")) value -= ParseMultiplicative();
            else return value;
        }
    }

    private double ParseMultiplicative()
    {
        var value = ParsePower();
        while (true)
        {
            SkipSpaces();
            if (Match("*")) value *= ParsePower();
            else if (Match("/")) value /= ParsePower();
            else return value;
        }
    }

    private double ParsePower()
    {
        var value = ParseUnary();
        SkipSpaces();
        if (Match("^")) value = Math.Pow(value, ParsePower());
        return value;
    }

    private double ParseUnary()
    {
        SkipSpaces();
        if (Match("-")) return -ParseUnary();
        if (Match("+")) return ParseUnary();
        if (Match("!")) return ParseUnary() == 0 ? 1 : 0;
        return ParsePrimary();
    }

    private double ParsePrimary()
    {
        SkipSpaces();
        if (Match("("))
        {
            var value = ParseOr();
            if (!Match(")")) throw new FormatException("Falta un paréntesis.");
            return value;
        }
        if (_position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] == '.'))
        {
            var start = _position;
            while (_position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] == '.')) _position++;
            return double.Parse(_text[start.._position], System.Globalization.CultureInfo.InvariantCulture);
        }
        var name = ReadName();
        if (name.Length == 0) throw new FormatException("Fórmula incompleta.");
        SkipSpaces();
        if (!Match("(")) return _resolve(name);
        var arguments = new List<double>();
        SkipSpaces();
        if (!Match(")"))
        {
            do
            {
                arguments.Add(ParseOr());
                SkipSpaces();
            } while (Match(","));
            if (!Match(")")) throw new FormatException("Falta un paréntesis de cierre.");
        }
        return Call(name.ToLowerInvariant(), arguments);
    }

    private static double Call(string name, List<double> arguments) => name switch
    {
        "abs" => Math.Abs(Argument(arguments, 0)),
        "sqrt" => Math.Sqrt(Argument(arguments, 0)),
        "exp" => Math.Exp(Argument(arguments, 0)),
        "log" => Math.Log(Argument(arguments, 0)),
        "log10" => Math.Log10(Argument(arguments, 0)),
        "round" => arguments.Count > 1 ? Math.Round(Argument(arguments, 0), (int)Argument(arguments, 1)) : Math.Round(Argument(arguments, 0)),
        "floor" => Math.Floor(Argument(arguments, 0)),
        "ceil" => Math.Ceiling(Argument(arguments, 0)),
        "min" => Math.Min(Argument(arguments, 0), Argument(arguments, 1)),
        "max" => Math.Max(Argument(arguments, 0), Argument(arguments, 1)),
        "if" => Argument(arguments, 0) != 0 ? Argument(arguments, 1) : Argument(arguments, 2),
        _ => throw new FormatException($"Función desconocida: {name}.")
    };

    private static double Argument(List<double> arguments, int index) =>
        index < arguments.Count ? arguments[index] : throw new FormatException("Faltan argumentos en la función.");

    private string ReadName()
    {
        if (Match("["))
        {
            var start = _position;
            while (_position < _text.Length && _text[_position] != ']') _position++;
            var name = _text[start.._position];
            Match("]");
            return name;
        }
        var begin = _position;
        while (_position < _text.Length && (char.IsLetterOrDigit(_text[_position]) || _text[_position] == '_' || _text[_position] > 127)) _position++;
        return _text[begin.._position];
    }

    private bool Match(string token)
    {
        if (!string.Equals(_text.AsSpan(_position, Math.Min(token.Length, _text.Length - _position)).ToString(), token, StringComparison.Ordinal)) return false;
        _position += token.Length;
        return true;
    }

    private bool MatchWord(string word)
    {
        var start = _position;
        if (string.Compare(_text, start, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
        _position += word.Length;
        return true;
    }

    private void SkipSpaces()
    {
        while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++;
    }
}
