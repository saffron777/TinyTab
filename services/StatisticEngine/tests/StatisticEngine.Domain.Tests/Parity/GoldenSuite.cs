using Xunit;

namespace StatisticEngine.Domain.Tests.Parity;

/// <summary>Ejecuta todos los casos de un vector dorado contra un registro de funciones.</summary>
public static class GoldenSuite
{
    public static void Run(
        string module,
        IReadOnlyDictionary<string, Func<GoldenCase, object?>> registry,
        double tolerance,
        params string[] excluded)
    {
        var document = GoldenDocument.Load(module);
        var names = document.FunctionNames
            .Where(name => !excluded.Contains(name, StringComparer.Ordinal))
            .ToArray();
        RunNames(document, registry, tolerance, names, module);
    }

    /// <summary>Ejecuta sólo un subconjunto de funciones (útil para piezas compartidas).</summary>
    public static void RunSelected(
        string module,
        IReadOnlyDictionary<string, Func<GoldenCase, object?>> registry,
        double tolerance,
        params string[] names)
    {
        var document = GoldenDocument.Load(module);
        RunNames(document, registry, tolerance, names, module);
    }

    private static void RunNames(
        GoldenDocument document,
        IReadOnlyDictionary<string, Func<GoldenCase, object?>> registry,
        double tolerance,
        IReadOnlyList<string> names,
        string module)
    {
        var failures = new List<string>();

        foreach (var name in names.OrderBy(item => item, StringComparer.Ordinal))
        {
            if (!registry.TryGetValue(name, out var runner))
            {
                failures.Add($"{name}: sin implementación registrada en la prueba de paridad");
                continue;
            }

            var cases = document.Cases(name);
            for (var index = 0; index < cases.Count; index++)
            {
                var goldenCase = cases[index];
                object? actual;
                try
                {
                    actual = runner(goldenCase);
                }
                catch (Exception exception)
                {
                    failures.Add($"{name}[{index}] lanzó {exception.GetType().Name}: {exception.Message}");
                    continue;
                }

                try
                {
                    ParityAssert.Matches(goldenCase.Expected, actual, tolerance, $"{name}[{index}]");
                }
                catch (Exception exception)
                {
                    failures.Add(exception.Message);
                }
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail($"{module}: {failures.Count} discrepancia(s):\n{string.Join('\n', failures.Take(40))}");
        }
    }
}
