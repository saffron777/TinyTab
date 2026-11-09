namespace StatisticEngine.Domain.Quality;

/// <summary>
/// Reglas de Nelson/Western Electric sobre los valores de una carta con límites 3σ.
/// Paridad 1:1 con <c>evaluateRules</c> de <c>spc.ts:58-87</c>, incluido el orden de
/// inserción de los mensajes en cada punto.
/// </summary>
public static class NelsonRules
{
    public const string Rule1 = "Regla 1: punto fuera de los límites de control";
    public const string Rule2 = "Regla 2: 8 puntos consecutivos al mismo lado";
    public const string Rule3 = "Regla 3: 6 puntos consecutivos en tendencia";
    public const string Rule5 = "Regla 5: 2 de 3 puntos a más de 2σ en el mismo lado";

    /// <summary>Una lista de mensajes por observación, en el orden de las reglas (spc.ts:59-86).</summary>
    public static string[][] EvaluateRules(IReadOnlyList<double> values, double center, double sigma)
    {
        var rules = new List<string>[values.Count];
        for (var i = 0; i < values.Count; i++) rules[i] = [];

        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            // paridad: spc.ts:63 — la sigma se protege sólo para la regla 1.
            var z = sigma > 0 ? (value - center) / sigma : 0;
            if (Math.Abs(z) > 3) rules[i].Add(Rule1);

            if (i >= 2)
            {
                var beyondTwo = true;
                var allAbove = true;
                var allBelow = true;
                for (var j = i - 2; j <= i; j++)
                {
                    var item = values[j];
                    // División directa: sigma 0 produce ±∞/NaN como en JavaScript.
                    if (!(Math.Abs((item - center) / sigma) > 2)) beyondTwo = false;
                    if (!(item > center)) allAbove = false;
                    if (!(item < center)) allBelow = false;
                }
                // paridad: spc.ts:67-73 — dos comprobaciones separadas que nunca coinciden.
                if (beyondTwo && (allAbove || allBelow)) rules[i].Add(Rule5);
            }

            if (i >= 7)
            {
                var allAbove = true;
                var allBelow = true;
                for (var j = i - 7; j <= i; j++)
                {
                    if (!(values[j] > center)) allAbove = false;
                    if (!(values[j] < center)) allBelow = false;
                }
                if (allAbove || allBelow) rules[i].Add(Rule2);
            }

            if (i >= 5)
            {
                var increasing = true;
                var decreasing = true;
                for (var j = i - 4; j <= i; j++)
                {
                    if (!(values[j] > values[j - 1])) increasing = false;
                    if (!(values[j] < values[j - 1])) decreasing = false;
                }
                if (increasing || decreasing) rules[i].Add(Rule3);
            }
        }

        var output = new string[rules.Length][];
        for (var i = 0; i < rules.Length; i++) output[i] = [.. rules[i]];
        return output;
    }
}
