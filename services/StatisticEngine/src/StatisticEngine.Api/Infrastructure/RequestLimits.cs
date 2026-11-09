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

        if (request?.Params is { } parameters && parameters.Count > limits.MaxParameters)
        {
            return $"La petición declara más de {limits.MaxParameters} parámetros.";
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
