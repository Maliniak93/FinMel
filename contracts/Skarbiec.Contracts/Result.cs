namespace Skarbiec.Contracts;

/// <summary>
/// Outcome of an operation that can fail in an expected way. Handlers and validating factories
/// return this instead of throwing; the endpoint maps a failure to ProblemDetails (ADR-017).
/// </summary>
public readonly struct Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    private Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>Same as <see cref="Result"/>, carrying a value on success (ADR-017).</summary>
public readonly struct Result<TValue>
{
    private readonly TValue? _value;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access the value of a failed result.");

    private Result(bool isSuccess, TValue? value, Error error)
    {
        IsSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    public static Result<TValue> Success(TValue value) => new(true, value, Error.None);
    public static Result<TValue> Failure(Error error) => new(false, default, error);

    public static implicit operator Result<TValue>(TValue value) => Success(value);
    public static implicit operator Result<TValue>(Error error) => Failure(error);
}
