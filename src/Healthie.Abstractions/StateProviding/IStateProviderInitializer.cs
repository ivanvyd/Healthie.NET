namespace Healthie.Abstractions.StateProviding;

/// <summary>
/// Defines a contract for initializing a state provider.
/// </summary>
public interface IStateProviderInitializer
{
    /// <summary>
    /// Initializes the state provider asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous initialization operation.</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
