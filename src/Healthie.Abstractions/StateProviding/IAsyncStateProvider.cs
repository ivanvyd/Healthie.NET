namespace Healthie.Abstractions.StateProviding;

/// <summary>
/// Defines a contract for asynchronously providing and managing pulse checker states.
/// </summary>
public interface IAsyncStateProvider
{
    /// <summary>
    /// Gets the state of a specific pulse checker asynchronously.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <returns>
    /// A task that represents the asynchronous operation.
    /// The task result contains the <see cref="IState"/> of the pulse checker, or <c>null</c> if not found.
    /// </returns>
    Task<TState?> GetStateAsync<TState>(string name);

    /// <summary>
    /// Saves the state of a pulse checker asynchronously.
    /// </summary>
    /// <param name="state">The state to save.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous save operation.</returns>
    Task SetStateAsync<TState>(string name, TState state);
}
