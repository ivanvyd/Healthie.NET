namespace Healthie.Abstractions;

/// <summary>
/// Defines a contract for triggering a pulse check asynchronously.
/// </summary>
public interface IPulse
{
    /// <summary>
    /// Triggers the pulse check asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous trigger operation.</returns>
    Task TriggerAsync(CancellationToken cancellationToken = default);
}
