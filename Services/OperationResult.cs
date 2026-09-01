namespace BusTicketing.Services;

/// <summary>Outcome of a command that can fail for a business reason (not an exception).</summary>
public readonly record struct OperationResult(bool Succeeded, string? Error)
{
    public static OperationResult Ok() => new(true, null);

    public static OperationResult Fail(string error) => new(false, error);
}

public readonly record struct OperationResult<T>(bool Succeeded, T? Value, string? Error)
{
    public static OperationResult<T> Ok(T value) => new(true, value, null);

    public static OperationResult<T> Fail(string error) => new(false, default, error);
}
