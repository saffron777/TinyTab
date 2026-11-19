using System.Globalization;
using StatisticEngine.Domain;
using StatisticEngine.Domain.Analysis;
using StatisticEngine.Domain.Data;
using StatisticEngine.Domain.Numerics;

namespace StatisticEngine.Application.UseCases;

/// <summary>
/// Validación de la entrada de los análisis extendidos (port de <c>sigma.domain.analysis.spec.prepare_inputs</c>):
/// roles, tipos de columna, número de columnas, parámetros, opciones, rangos y visibilidad condicional.
/// Los mensajes se conservan para que la paridad con Sigma sea verificable.
/// </summary>
public static class AnalysisValidator
{
    private const int MaxText = 500;

    private const int MaxCode = 4000;

    public static Result<Unit> Validate(AnalysisDefinition definition, AnalysisInput input)
    {
        if (definition.Roles is { Count: > 0 } roles)
        {
            var frameResult = ResolveFrame(input);
            if (frameResult.IsError) return frameResult.Map(_ => Unit.Value);

            var frame = frameResult.Value;
            foreach (var role in roles)
            {
                var error = ValidateRole(role, input, frame);
                if (error is not null) return error.Value;
            }
        }

        foreach (var parameter in definition.Params)
        {
            var error = ValidateParameter(parameter, input);
            if (error is not null) return error.Value;
        }

        return Result.Ok(Unit.Value);
    }

    private static Result<Frame> ResolveFrame(AnalysisInput input)
    {
        // Un frame tipado no puede tener columnas de distinto largo; los mapas numérico/texto sí
        // (el motor baseline los admite). Para los análisis extendidos se exige alineación.
        if (input.Columns is not null) return Result.Ok(input.Columns);

        var lengths = input.Numeric.Values.Select(values => values.Length)
            .Concat(input.Text.Values.Select(values => values.Length))
            .Distinct()
            .ToArray();
        if (lengths.Length > 1)
        {
            return Result.Err<Frame>(
                DomainErrorCode.InvalidInput,
                "Todas las columnas de la petición deben tener el mismo número de filas.");
        }

        return Result.Ok(input.AsFrame());
    }

    private static Result<Unit>? ValidateRole(ColumnRole role, AnalysisInput input, Frame frame)
    {
        var names = input.RoleNames(role.Key, role.Multiple);
        if (names.Count == 0)
        {
            if (role.Optional) return null;
            var what = role.Multiple ? "al menos una columna para" : "la columna de";
            return Result.Err<Unit>(DomainErrorCode.InvalidInput, $"Elija {what} «{role.Label}».");
        }

        if (role.Multiple)
        {
            if (names.Count < role.MinCount)
            {
                return Result.Err<Unit>(
                    DomainErrorCode.InvalidInput,
                    $"«{role.Label}» necesita al menos {role.MinCount} columna(s).");
            }
            if (role.MaxCount is { } max && names.Count > max)
            {
                return Result.Err<Unit>(
                    DomainErrorCode.InvalidInput,
                    $"«{role.Label}» admite como máximo {max} columna(s).");
            }
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Count)
            {
                return Result.Err<Unit>(DomainErrorCode.InvalidInput, $"«{role.Label}» repite una columna.");
            }
        }

        foreach (var name in names)
        {
            var column = frame.Find(name);
            if (column is null)
            {
                return Result.Err<Unit>(DomainErrorCode.InvalidInput, $"La columna «{name}» no existe en el dataset.");
            }
            if (!Accepts(role.Kind, column.Kind))
            {
                return Result.Err<Unit>(
                    DomainErrorCode.InvalidInput,
                    $"«{role.Label}» necesita una columna {KindName(role.Kind)}: «{name}» no lo es.");
            }
        }

        return null;
    }

    private static Result<Unit>? ValidateParameter(AnalysisParamDefinition parameter, AnalysisInput input)
    {
        if (parameter.VisibleIf is { } visible
            && !visible.Values.Contains(RawText(input, visible.Key), StringComparer.Ordinal))
        {
            return null;
        }

        var present = input.Params.TryGetValue(parameter.Key, out var raw)
                      && raw is not null
                      && !(raw is string text && text.Length == 0);

        if (!present)
        {
            if (parameter.DefaultValue is null && !parameter.Optional
                && parameter.Kind is not (AnalysisParamKinds.Boolean or AnalysisParamKinds.Multiselect))
            {
                return Result.Err<Unit>(DomainErrorCode.InvalidInput, $"Indique «{parameter.Label}».");
            }
            return null;
        }

        switch (parameter.Kind)
        {
            case AnalysisParamKinds.Number:
            case AnalysisParamKinds.Integer:
            {
                var value = ToNumber(raw, out var parsed);
                if (!value)
                {
                    return Result.Err<Unit>(DomainErrorCode.InvalidInput, $"«{parameter.Label}» debe ser un número.");
                }
                if (!double.IsFinite(parsed))
                {
                    return Result.Err<Unit>(
                        DomainErrorCode.InvalidInput,
                        $"«{parameter.Label}» debe ser un número finito.");
                }
                if (parameter.Kind == AnalysisParamKinds.Integer && parsed != Math.Floor(parsed))
                {
                    return Result.Err<Unit>(
                        DomainErrorCode.InvalidInput,
                        $"«{parameter.Label}» debe ser un número entero.");
                }
                if (parameter.Minimum is { } min && parsed < min)
                {
                    return Result.Err<Unit>(
                        DomainErrorCode.InvalidInput,
                        $"«{parameter.Label}» debe ser ≥ {FormatNumber(min)}.");
                }
                if (parameter.Maximum is { } max && parsed > max)
                {
                    return Result.Err<Unit>(
                        DomainErrorCode.InvalidInput,
                        $"«{parameter.Label}» debe ser ≤ {FormatNumber(max)}.");
                }
                break;
            }
            case AnalysisParamKinds.Select:
            {
                var value = raw as string ?? Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
                if (!parameter.Options.Any(option => option.Value == value))
                {
                    return Result.Err<Unit>(
                        DomainErrorCode.InvalidInput,
                        $"Valor no válido para «{parameter.Label}».");
                }
                break;
            }
            case AnalysisParamKinds.Multiselect:
            {
                var values = raw switch
                {
                    string[] items => items,
                    IReadOnlyList<string> items => [.. items],
                    string item => [item],
                    _ => []
                };
                if (values.Any(value => !parameter.Options.Any(option => option.Value == value)))
                {
                    return Result.Err<Unit>(
                        DomainErrorCode.InvalidInput,
                        $"Valor no válido para «{parameter.Label}».");
                }
                break;
            }
            case AnalysisParamKinds.Text:
            case AnalysisParamKinds.Code:
            {
                var value = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
                var limit = parameter.Kind == AnalysisParamKinds.Code ? MaxCode : MaxText;
                if (value.Length > limit)
                {
                    return Result.Err<Unit>(
                        DomainErrorCode.InvalidInput,
                        $"«{parameter.Label}» admite como máximo {limit} caracteres.");
                }
                break;
            }
            case AnalysisParamKinds.Dataset:
            {
                var value = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                if (value.Length == 0 || value.Length > 64)
                {
                    return Result.Err<Unit>(
                        DomainErrorCode.InvalidInput,
                        $"Elija un dataset para «{parameter.Label}».");
                }
                break;
            }
        }

        return null;
    }

    private static string RawText(AnalysisInput input, string key)
    {
        if (!input.Params.TryGetValue(key, out var value) || value is null) return string.Empty;
        return value switch
        {
            string text => text,
            bool flag => flag ? "true" : "false",
            double number => JsNumber.ToString(number),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static bool ToNumber(object? raw, out double parsed)
    {
        switch (raw)
        {
            case double number:
                parsed = number;
                return true;
            case int number:
                parsed = number;
                return true;
            case long number:
                parsed = number;
                return true;
            case bool flag:
                parsed = flag ? 1 : 0;
                return true;
            case string text:
                parsed = JsNumber.Parse(text);
                return text.Trim().Length > 0 && !double.IsNaN(parsed);
            default:
                parsed = double.NaN;
                return false;
        }
    }

    private static bool Accepts(string roleKind, DataColumnKind kind) =>
        roleKind switch
        {
            ColumnKinds.Numeric => kind == DataColumnKind.Numeric,
            ColumnKinds.Categorical => kind == DataColumnKind.Text,
            ColumnKinds.DateTime => kind == DataColumnKind.DateTime,
            _ => true
        };

    private static string KindName(string roleKind) =>
        roleKind switch
        {
            ColumnKinds.Numeric => "numérica",
            ColumnKinds.Categorical => "categórica",
            ColumnKinds.DateTime => "de fecha",
            _ => roleKind
        };

    private static string FormatNumber(double value) => value.ToString("G", CultureInfo.InvariantCulture);
}
