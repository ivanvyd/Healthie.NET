namespace Healthie.Abstractions.StateProviding;

/// <summary>
/// Defines a contract for asynchronously initializing a state provider.
/// </summary>
public interface IAsyncStateProviderInitializer
{
    /// <summary>
    /// Initializes the state provider asynchronously.
    /// </summary>
    /// <returns>A task that represents the asynchronous initialization operation.</returns>
    Task InitializeAsync();
}
