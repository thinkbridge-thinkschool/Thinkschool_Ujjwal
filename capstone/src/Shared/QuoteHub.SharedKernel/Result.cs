namespace QuoteHub.SharedKernel;

// Aggregates in this codebase report invariant violations as values, not
// exceptions - a caller (a command handler, a test) checks IsSuccess
// instead of wrapping every call in try/catch. This is a deliberate
// departure from day-5/QuotesApi's Collection aggregate, which throws;
// this capstone's brief calls for Result<T> specifically, so the
// exception-based style wasn't carried over even though the invariants
// and general shape (private setters, factory construction) were.
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string Error { get; }

    protected Result(bool isSuccess, string error)
    {
        if (isSuccess && !string.IsNullOrEmpty(error))
            throw new InvalidOperationException("A successful result cannot carry an error message.");
        if (!isSuccess && string.IsNullOrEmpty(error))
            throw new InvalidOperationException("A failed result must carry an error message.");

        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, string.Empty);
    public static Result Failure(string error) => new(false, error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    // Throws deliberately, not another Result - reading .Value on a
    // failed result is a programming error at the call site (it should
    // have checked IsSuccess first), not a recoverable domain outcome.
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot access the value of a failed result. Error: {Error}");

    private Result(T value) : base(true, string.Empty)
    {
        _value = value;
    }

    private Result(string error) : base(false, error)
    {
        _value = default;
    }

    public static Result<T> Success(T value) => new(value);
    public static new Result<T> Failure(string error) => new(error);
}
