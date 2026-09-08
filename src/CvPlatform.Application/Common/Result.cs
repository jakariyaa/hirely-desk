namespace CvPlatform.Application.Common;

/// <summary>A domain error: a stable code plus a non-localized developer message.</summary>
public readonly record struct Error(string Code, string Message);

/// <summary>Result of an operation that does not return a value.</summary>
public class Result
{
    private Result(bool succeeded, Error error)
    {
        Succeeded = succeeded;
        Error = error;
    }

    public bool Succeeded { get; }

    public Error Error { get; }

    public static Result Success() => new(true, default);

    public static Result Failure(string code, string message) => new(false, new Error(code, message));
}

/// <summary>Result of an operation that returns a value on success.</summary>
public class Result<T>
{
    private Result(T value)
    {
        Succeeded = true;
        Value = value;
        Error = default;
    }

    private Result(Error error)
    {
        Succeeded = false;
        Value = default;
        Error = error;
    }

    public bool Succeeded { get; }

    public T? Value { get; }

    public Error Error { get; }

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(string code, string message) => new(new Error(code, message));
}
