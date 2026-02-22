namespace Healthie.Abstractions.StateProviding;

/// <summary>
/// Defines a contract for providing and managing pulse checker states.
/// </summary>
public interface IStateProvider
{
    /// <summary>
    /// Gets the state of a specific pulse checker asynchronously.
    /// </summary>
    /// <typeparam name="TState">The type of state to retrieve.</typeparam>
    /// <param name="name">The unique name of the pulse checker.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task that represents the asynchronous operation.
    /// The task result contains the stored state, or <c>default</c> if not found.
    /// </returns>
    Task<TState?> GetStateAsync<TState>(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the state of a pulse checker asynchronously.
    /// </summary>
    /// <typeparam name="TState">The type of state to save.</typeparam>
    /// <param name="name">The unique name of the pulse checker.</param>
    /// <param name="state">The state to save.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous save operation.</returns>
    Task SetStateAsync<TState>(string name, TState state, CancellationToken cancellationToken = default);
}
