using StatisticEngine.Domain;
using Xunit;

namespace StatisticEngine.Domain.Tests.Parity;

/// <summary>
/// Proyección de <see cref="Result{T}"/> al contrato JSON del motor: <c>{ok, value}</c> o
/// <c>{ok, error:{code, message}}</c>.
/// </summary>
public static class ResultDto
{
    public static object From<T>(Result<T> result) =>
        result.IsOk
            ? new OkDto<T>(true, result.Value)
            : new ErrorDto(false, new ErrorBodyDto(result.Error.WireCode, result.Error.Message));

    /// <summary>Para <c>Result&lt;void&gt;</c>: el motor serializa <c>{ok:true}</c> sin propiedad <c>value</c>.</summary>
    public static object FromVoid(Result<Unit> result) =>
        result.IsOk
            ? new OkVoidDto(true)
            : new ErrorDto(false, new ErrorBodyDto(result.Error.WireCode, result.Error.Message));
}

public sealed record OkDto<T>(bool Ok, T Value);

public sealed record OkVoidDto(bool Ok);

public sealed record ErrorBodyDto(string Code, string Message);

public sealed record ErrorDto(bool Ok, ErrorBodyDto Error);
