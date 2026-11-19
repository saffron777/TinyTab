using StatisticEngine.Api.Contracts;

namespace StatisticEngine.Api.Infrastructure;

/// <summary>Comprobaciones de tamaño de la carga útil antes de ejecutar el motor.</summary>
public static class RequestLimits
{
    public static string? CheckAnalysis(AnalysisRunRequest? request, LimitsOptions limits)
    {
        if (request?.Numeric is { } numeric)
        {
            var total = 0;
            foreach (var (key, values) in numeric)
            {
                if (values.Length > limits.MaxColumnLength)
                {
                    return $"La columna \"{key}\" supera el máximo de {limits.MaxColumnLength} valores.";
                }
                total += values.Length;
                if (total > limits.MaxNumericValues)
                {
                    return $"El total de valores numéricos supera el máximo de {limits.MaxNumericValues}.";
                }
            }
        }

        if (request?.Text is { } text)
        {
            var characters = 0;
            foreach (var values in text.Values)
            {
                if (values.Length > limits.MaxColumnLength)
                {
                    return $"Una columna de texto supera el máximo de {limits.MaxColumnLength} valores.";
                }
                characters += values.Sum(value => value?.Length ?? 0);
                if (characters > limits.MaxTextCharacters)
                {
                    return $"El total de caracteres de texto supera el máximo de {limits.MaxTextCharacters}.";
                }
            }
        }

        if (request?.Datetime is { } datetime)
        {
            foreach (var (key, values) in datetime)
            {
                if (values.Length > limits.MaxColumnLength)
                {
                    return $"La columna de fecha \"{key}\" supera el máximo de {limits.MaxColumnLength} valores.";
                }
            }
        }

        if (request?.Columns is { } columns)
        {
            var error = CheckColumns(columns, limits);
            if (error is not null) return error;
        }

        if (request?.Params is { } parameters && parameters.Count > limits.MaxParameters)
        {
            return $"La petición declara más de {limits.MaxParameters} parámetros.";
        }

        return null;
    }

    public static string? CheckData(DataRunRequest? request, LimitsOptions limits)
    {
        if (request?.Columns is { } columns)
        {
            var error = CheckColumns(columns, limits);
            if (error is not null) return error;
        }

        if (request?.Datasets is { } datasets)
        {
            foreach (var (key, dataset) in datasets)
            {
                if (dataset?.Columns is not { } frames) continue;
                var error = CheckColumns(frames, limits);
                if (error is not null) return $"Dataset «{key}»: {error}";
            }
        }

        if (request?.Params is { } parameters && parameters.Count > limits.MaxParameters)
        {
            return $"La petición declara más de {limits.MaxParameters} parámetros.";
        }

        return null;
    }

    private static string? CheckColumns(IReadOnlyList<DataColumnDto> columns, LimitsOptions limits)
    {
        if (columns.Count > limits.MaxParameters)
        {
            return $"La petición declara más de {limits.MaxParameters} columnas.";
        }

        var total = 0L;
        foreach (var column in columns)
        {
            var length = column.Values?.Length ?? 0;
            if (length > limits.MaxDataRows)
            {
                return $"La columna \"{column.Name}\" supera el máximo de {limits.MaxDataRows} filas.";
            }
            total += length;
            if (total > limits.MaxDatasetValues)
            {
                return $"El total de valores del frame supera el máximo de {limits.MaxDatasetValues}.";
            }
        }
        return null;
    }

    public static string? CheckDistributionPoints(int count, LimitsOptions limits) =>
        count > limits.MaxDistributionPoints
            ? $"Se solicitaron {count} puntos y el máximo es {limits.MaxDistributionPoints}."
            : null;

    public static string? CheckSampleCount(int count, LimitsOptions limits) =>
        count > limits.MaxSampleCount
            ? $"Se solicitaron {count} muestras y el máximo es {limits.MaxSampleCount}."
            : null;
}
