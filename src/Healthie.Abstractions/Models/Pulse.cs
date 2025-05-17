namespace Healthie.Abstractions.Models;

public class Pulse<TResult> where TResult : Result
{
    public TResult? Result { get; private set; }
    public Exception? Error { get; private set; }
    public bool IsSuccess => Error == null;

    private Pulse() { }

    public static Pulse<TResult> Success(TResult result)
    {
        return new Pulse<TResult> { Result = result };
    }

    public static Pulse<TResult> Failure(Exception error)
    {
        return new Pulse<TResult> { Error = error };
    }

    public static implicit operator Pulse<TResult>(TResult result)
    {
        return Success(result);
    }

    public static implicit operator Pulse<TResult>(Exception error)
    {
        return Failure(error);
    }

    public override string ToString()
    {
        if (!IsSuccess)
        {
            return $"Failure: {Error?.Message}";
        }

        if (Result is null)
        {
            return "Unknown";
        }

        return Result.IsHealthy
            ? $"Healthy: {Result.Message}"
            : $"Unhealthy: {Result.Message}";
    }
}