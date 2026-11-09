using System.Diagnostics.CodeAnalysis;

namespace StatisticEngine.Domain;

/// <summary>
/// Códigos de error del dominio. Espejan <c>DomainErrorCode</c> del motor TypeScript;
/// la representación "wire" (SCREAMING_SNAKE_CASE) se conserva para paridad con el motor.
/// </summary>
public enum DomainErrorCode
{
    InsufficientData,
    InvalidParameter,
    SingularMatrix,
    NonConvergence,
    TypeMismatch,
    NotSupported,
    InvalidInput,
    Unknown
}

public static class DomainErrorCodes
{
    /// <summary>Nombre canónico del código, idéntico al del motor TypeScript.</summary>
    public static string ToWire(DomainErrorCode code) =>
        code switch
        {
            DomainErrorCode.InsufficientData => "INSUFFICIENT_DATA",
            DomainErrorCode.InvalidParameter => "INVALID_PARAMETER",
            DomainErrorCode.SingularMatrix => "SINGULAR_MATRIX",
            DomainErrorCode.NonConvergence => "NON_CONVERGENCE",
            DomainErrorCode.TypeMismatch => "TYPE_MISMATCH",
            DomainErrorCode.NotSupported => "NOT_SUPPORTED",
            DomainErrorCode.InvalidInput => "INVALID_INPUT",
            _ => "UNKNOWN"
        };
}

public sealed record DomainError(
    DomainErrorCode Code,
    string Message,
    IReadOnlyDictionary<string, object?>? Details = null)
{
    public string WireCode => DomainErrorCodes.ToWire(Code);
}

/// <summary>
/// Resultado explícito del dominio: nunca se lanzan excepciones por errores de usuario.
/// Equivale a <c>Result&lt;T, DomainError&gt;</c> del motor TypeScript.
/// </summary>
public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly DomainError? _error;
    private readonly bool _ok;

    private Result(bool ok, T? value, DomainError? error)
    {
        _ok = ok;
        _value = value;
        _error = error;
    }

    public bool IsOk => _ok;

    public bool IsError => !_ok;

    public T Value =>
        _ok
            ? _value!
            : throw new InvalidOperationException(
                $"Se accedió al valor de un resultado erróneo: {_error?.Code.ToString() ?? "unknown"}.");

    public DomainError Error =>
        !_ok
            ? _error ?? new DomainError(DomainErrorCode.Unknown, "Resultado no inicializado.")
            : throw new InvalidOperationException("Se accedió al error de un resultado correcto.");

    public static Result<T> Ok(T value) => new(true, value, null);

    public static Result<T> Err(DomainErrorCode code, string message, IReadOnlyDictionary<string, object?>? details = null) =>
        new(false, default, new DomainError(code, message, details));

    public bool TryGetValue([NotNullWhen(true)] out T? value)
    {
        value = _value;
        return _ok;
    }

    public Result<TOut> Map<TOut>(Func<T, TOut> selector) =>
        _ok ? Result<TOut>.Ok(selector(_value!)) : Result<TOut>.Err(Error.Code, Error.Message, Error.Details);

    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> binder) =>
        _ok ? binder(_value!) : Result<TOut>.Err(Error.Code, Error.Message, Error.Details);

    public override string ToString() =>
        _ok ? $"Ok({_value})" : $"Err({Error.WireCode}: {Error.Message})";
}

/// <summary>Unidad para resultados sin valor (equivalente a <c>Result&lt;void&gt;</c>).</summary>
public readonly record struct Unit
{
    public static Unit Value => default;
}

public static class Result
{
    public static Result<T> Ok<T>(T value) => Result<T>.Ok(value);

    public static Result<T> Err<T>(
        DomainErrorCode code,
        string message,
        IReadOnlyDictionary<string, object?>? details = null) => Result<T>.Err(code, message, details);
}
