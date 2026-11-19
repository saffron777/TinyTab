using System.Text.RegularExpressions;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Domain.Models;

/// <summary>Una columna (o bloque de columnas) del diseño y los índices de matriz que ocupa.</summary>
public sealed record DesignTerm(string Name, IReadOnlyList<int> Columns);

/// <summary>
/// Diseño de un modelo lineal: datos completos, columnas del diseño (dummies incluidas) y la
/// nomenclatura de términos de patsy. Port de <c>sigma.domain.analysis.modeling.build_design</c>.
/// </summary>
public sealed class Design
{
    internal Design(
        Frame source,
        Frame data,
        string? responseToken,
        IReadOnlyList<string> numericTokens,
        IReadOnlyList<string> factorTokens,
        IReadOnlyDictionary<string, string> names,
        IReadOnlyDictionary<string, string[]> levels,
        int[] usedRows,
        int dropped,
        IReadOnlyList<DesignTerm> terms,
        double[][] matrix,
        IReadOnlyList<string> columnNames,
        double[] response)
    {
        Source = source;
        Data = data;
        ResponseToken = responseToken;
        NumericTokens = numericTokens;
        FactorTokens = factorTokens;
        Names = names;
        Levels = levels;
        UsedRows = usedRows;
        Dropped = dropped;
        Terms = terms;
        Matrix = matrix;
        ColumnNames = columnNames;
        Response = response;
    }

    public Frame Source { get; }

    public Frame Data { get; }

    public string? ResponseToken { get; }

    public IReadOnlyList<string> NumericTokens { get; }

    public IReadOnlyList<string> FactorTokens { get; }

    public IReadOnlyDictionary<string, string> Names { get; }

    public IReadOnlyDictionary<string, string[]> Levels { get; }

    public int[] UsedRows { get; }

    public int Dropped { get; }

    public IReadOnlyList<DesignTerm> Terms { get; }

    public double[][] Matrix { get; }

    public IReadOnlyList<string> ColumnNames { get; }

    public double[] Response { get; }

    public int RowCount => Matrix.Length;

    public int ParameterCount => ColumnNames.Count;

    public double[] Column(string token) => Data.Numeric(token);

    /// <summary>Nombre legible de un término (patsy → texto de Sigma).</summary>
    public string Pretty(string term)
    {
        var text = term;
        text = Regex.Replace(text, @"C\((v\d+)\)\[T\.([^\]]*)\]", match => $"{Names[match.Groups[1].Value]} = {match.Groups[2].Value}");
        text = Regex.Replace(text, @"I\((v\d+) \*\* (\d+)\)", match => Names[match.Groups[1].Value] + Superscript(match.Groups[2].Value));
        text = Regex.Replace(text, @"\b(v\d+)\b", match => Names[match.Groups[1].Value]);
        return text.Replace(":", " × ");
    }

    private static string Superscript(string digits) =>
        string.Concat(digits.Select(digit => digit switch
        {
            '0' => '⁰',
            '1' => '¹',
            '2' => '²',
            '3' => '³',
            '4' => '⁴',
            '5' => '⁵',
            '6' => '⁶',
            '7' => '⁷',
            '8' => '⁸',
            '9' => '⁹',
            _ => digit
        }));

    /// <summary>Índices de las filas del frame original que entraron en el ajuste.</summary>
    public IReadOnlyList<int> UsedRowsList => UsedRows;
}

/// <summary>Construcción de un diseño lineal con factores, potencias e interacciones.</summary>
public static class DesignBuilder
{
    public const int MinRows = 8;

    public const int MaxLevels = 30;

    public static Result<Design> Build(
        Frame frame,
        string? response,
        IReadOnlyList<string> predictors,
        int degree = 1,
        string interactions = "none",
        IReadOnlyDictionary<string, string>? extra = null,
        bool constant = true,
        string responseKind = "numeric",
        int minRows = MinRows)
    {
        var columns = new List<string>();
        if (response is not null) columns.Add(response);
        columns.AddRange(predictors);
        if (extra is not null) columns.AddRange(extra.Values);
        columns = [.. columns.Distinct(StringComparer.Ordinal)];

        foreach (var column in columns)
        {
            if (!frame.Has(column))
            {
                return Result.Err<Design>(DomainErrorCode.InvalidInput, $"La columna «{column}» no existe en el dataset.");
            }
        }

        var completeIndices = frame.CompleteCaseIndices(columns);
        var complete = frame.SubsetRows(completeIndices);
        var tokens = columns.Select((column, index) => (column, token: $"v{index}")).ToDictionary(pair => pair.column, pair => pair.token, StringComparer.Ordinal);
        var names = tokens.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
        var numericTokens = new List<string>();
        var factorTokens = new List<string>();
        var levels = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var dataColumns = new List<DataColumn>();
        var responseToken = response is null ? null : tokens[response];

        foreach (var column in columns)
        {
            var token = tokens[column];
            var source = complete.Column(column);
            if (column == response)
            {
                var values = ParseNumeric(complete, column);
                dataColumns.Add(DataColumn.Numeric(token, values));
                continue;
            }
            if (extra is not null && extra.Values.Contains(column))
            {
                dataColumns.Add(DataColumn.Numeric(token, ParseNumeric(complete, column)));
                continue;
            }
            if (IsFactor(source))
            {
                var labels = complete.Levels(column);
                if (labels.Length > MaxLevels)
                {
                    return Result.Err<Design>(
                        DomainErrorCode.InvalidInput,
                        $"«{column}» tiene {labels.Length} categorías; un factor admite como máximo {MaxLevels}.");
                }
                if (labels.Length < 2)
                {
                    return Result.Err<Design>(DomainErrorCode.InvalidInput, $"«{column}» tiene una sola categoría: no aporta al modelo.");
                }
                var values = complete.Texts(column);
                dataColumns.Add(DataColumn.Text(token, values));
                levels[token] = labels;
                factorTokens.Add(token);
            }
            else
            {
                var values = ParseNumeric(complete, column);
                if (values.Distinct().Count(value => !double.IsNaN(value)) < 2)
                {
                    return Result.Err<Design>(DomainErrorCode.InvalidInput, $"«{column}» es constante: no aporta al modelo.");
                }
                dataColumns.Add(DataColumn.Numeric(token, values));
                numericTokens.Add(token);
            }
        }

        // Tras convertir la respuesta y los extras, pueden quedar faltantes.
        var kept = new List<int>();
        for (var row = 0; row < complete.RowCount; row++)
        {
            var valid = true;
            foreach (var column in dataColumns)
            {
                if (column.IsMissing(row))
                {
                    valid = false;
                    break;
                }
            }
            if (valid) kept.Add(row);
        }
        if (kept.Count < minRows)
        {
            return Result.Err<Design>(
                DomainErrorCode.InvalidInput,
                $"Se necesitan al menos {minRows} filas completas (hay {kept.Count}).");
        }

        var data = new Frame(dataColumns.Select(column => Slice(column, kept)), kept.Count);
        var usedRows = kept.Select(index => completeIndices[index]).ToArray();
        var responseValues = responseToken is null ? [] : data.Numeric(responseToken);

        var factorTerms = new List<DesignTerm>();
        var numericMainTerms = new List<DesignTerm>();
        var powerTerms = new List<DesignTerm>();
        var interactionTerms = new List<DesignTerm>();
        var interactionBases = new List<DesignTerm>();
        var matrixColumns = new List<double[]>();
        var columnNames = new List<string>();
        DesignTerm? interceptTerm = null;
        if (constant)
        {
            interceptTerm = AddTerm(new List<DesignTerm>(), matrixColumns, columnNames, "Intercept", Enumerable.Repeat(1.0, kept.Count).ToArray());
        }

        foreach (var token in numericTokens)
        {
            var values = data.Numeric(token);
            var main = AddTerm(new List<DesignTerm>(), matrixColumns, columnNames, token, values);
            numericMainTerms.Add(main);
            interactionBases.Add(main);
            for (var power = 2; power <= degree; power++)
            {
                powerTerms.Add(AddTerm(
                    new List<DesignTerm>(),
                    matrixColumns,
                    columnNames,
                    $"I({token} ** {power})",
                    values.Select(value => Math.Pow(value, power)).ToArray()));
            }
        }

        foreach (var token in factorTokens)
        {
            var columnsBefore = matrixColumns.Count;
            var labels = levels[token];
            var texts = data.Texts(token);
            for (var level = 1; level < labels.Length; level++)
            {
                var name = $"C({token})[T.{labels[level]}]";
                matrixColumns.Add(texts.Select(value => value == labels[level] ? 1.0 : 0.0).ToArray());
                columnNames.Add(name);
            }
            var factorTerm = new DesignTerm(
                $"C({token})",
                Enumerable.Range(columnsBefore, matrixColumns.Count - columnsBefore).ToArray());
            factorTerms.Add(factorTerm);
            interactionBases.Add(factorTerm);
        }

        if (interactions != "none" && interactionBases.Count > 1)
        {
            var depth = interactions == "two" ? 2 : interactionBases.Count;
            for (var size = 2; size <= depth; size++)
            {
                foreach (var combination in Enumerate(interactionBases, size, 0))
                {
                    var termName = string.Join(":", combination.Select(term => term.Name));
                    var indices = Cartesian(combination.Select(term => term.Columns).ToArray());
                    var termColumns = new List<int>(indices.Count);
                    foreach (var indexSet in indices)
                    {
                        var name = string.Join(":", indexSet.Select(index => columnNames[index]));
                        var values = new double[kept.Count];
                        Array.Fill(values, 1.0);
                        foreach (var index in indexSet)
                        {
                            var source = matrixColumns[index];
                            for (var row = 0; row < values.Length; row++) values[row] *= source[row];
                        }
                        termColumns.Add(matrixColumns.Count);
                        matrixColumns.Add(values);
                        columnNames.Add(name);
                    }
                    if (termColumns.Count > 0) interactionTerms.Add(new DesignTerm(termName, termColumns));
                }
            }
        }

        // Patsy ordena las columnas: intercepto, factores, efectos numéricos principales,
        // interacciones y, al final, las potencias.
        var orderedTerms = new List<DesignTerm>();
        if (interceptTerm is not null) orderedTerms.Add(interceptTerm);
        orderedTerms.AddRange(factorTerms);
        orderedTerms.AddRange(numericMainTerms);
        orderedTerms.AddRange(interactionTerms);
        orderedTerms.AddRange(powerTerms);

        var orderedIndices = new List<int>();
        foreach (var term in orderedTerms) orderedIndices.AddRange(term.Columns);
        for (var index = 0; index < matrixColumns.Count; index++)
        {
            if (!orderedIndices.Contains(index)) orderedIndices.Add(index);
        }
        var map = new Dictionary<int, int>();
        for (var index = 0; index < orderedIndices.Count; index++) map[orderedIndices[index]] = index;
        var finalTerms = orderedTerms
            .Select(term => new DesignTerm(term.Name, term.Columns.Select(index => map[index]).ToArray()))
            .ToArray();
        var orderedColumns = orderedIndices.Select(index => matrixColumns[index]).ToArray();
        var orderedNames = orderedIndices.Select(index => columnNames[index]).ToArray();

        var matrix = new double[kept.Count][];
        for (var row = 0; row < kept.Count; row++)
        {
            matrix[row] = new double[orderedColumns.Length];
            for (var column = 0; column < orderedColumns.Length; column++) matrix[row][column] = orderedColumns[column][row];
        }

        return Result.Ok(new Design(
            frame,
            data,
            responseToken,
            numericTokens,
            factorTokens,
            names,
            levels,
            usedRows,
            frame.RowCount - kept.Count,
            finalTerms,
            matrix,
            orderedNames,
            responseValues));
    }

    private static DesignTerm AddTerm(
        List<DesignTerm> terms,
        List<double[]> matrixColumns,
        List<string> columnNames,
        string name,
        double[] values)
    {
        var index = matrixColumns.Count;
        matrixColumns.Add(values);
        columnNames.Add(name);
        var term = new DesignTerm(name, [index]);
        terms.Add(term);
        return term;
    }

    private static List<int[]> Cartesian(IReadOnlyList<IReadOnlyList<int>> groups)
    {
        var result = new List<int[]> { Array.Empty<int>() };
        foreach (var group in groups)
        {
            var next = new List<int[]>(result.Count * group.Count);
            foreach (var prefix in result)
            {
                foreach (var value in group) next.Add([.. prefix, value]);
            }
            result = next;
        }
        return result;
    }

    private static IEnumerable<IReadOnlyList<T>> Enumerate<T>(IReadOnlyList<T> items, int size, int start)
    {
        if (size == 0)
        {
            yield return [];
            yield break;
        }
        for (var index = start; index <= items.Count - size; index++)
        {
            foreach (var rest in Enumerate(items, size - 1, index + 1))
            {
                yield return [items[index], .. rest];
            }
        }
    }

    private static bool IsFactor(DataColumn column) => column.Kind == DataColumnKind.Text;

    private static double[] ParseNumeric(Frame frame, string column)
    {
        var values = frame.Numeric(column);
        return values;
    }

    private static DataColumn Slice(DataColumn column, IReadOnlyList<int> rows)
    {
        switch (column.Kind)
        {
            case DataColumnKind.Numeric:
            {
                var values = new double[rows.Count];
                for (var index = 0; index < rows.Count; index++) values[index] = column.NumberAt(rows[index]);
                return DataColumn.Numeric(column.Name, values);
            }
            case DataColumnKind.Text:
            {
                var values = new string?[rows.Count];
                for (var index = 0; index < rows.Count; index++) values[index] = column.TextAt(rows[index]);
                return DataColumn.Text(column.Name, values);
            }
            default:
            {
                var values = new DateTime?[rows.Count];
                for (var index = 0; index < rows.Count; index++) values[index] = column.DateAt(rows[index]);
                return DataColumn.DateTime(column.Name, values);
            }
        }
    }
}
