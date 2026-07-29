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

    /// <summary>
    /// Removes a pulse checker's stored state.
    /// </summary>
    /// <param name="name">The unique name of the pulse checker.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns><c>true</c> if there was state to remove; <c>false</c> if there was none.</returns>
    /// <exception cref="NotSupportedException">The provider cannot remove state.</exception>
    /// <remarks>
    /// <para>
    /// A checker that is renamed or deleted leaves its state behind for ever, and a scheduler whose
    /// jobs outlive the process -- Hangfire, Temporal -- leaves a job pointing at it. Both log that
    /// the leftovers can be cleaned up, which until now was advice with no way to take it.
    /// </para>
    /// <para>
    /// Defaulted to refuse rather than to pretend. A provider written against the older interface
    /// has no way to delete, and a default that quietly did nothing would report a successful
    /// cleanup that never happened -- so it throws, and names itself.
    /// </para>
    /// </remarks>
    Task<bool> DeleteStateAsync(string name, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            $"{GetType().Name} cannot remove stored state. Implement " +
            $"{nameof(DeleteStateAsync)} on it to allow cleaning up checkers that no longer exist.");

    /// <summary>
    /// Gets the state of several pulse checkers at once.
    /// </summary>
    /// <typeparam name="TState">The type of state to retrieve.</typeparam>
    /// <param name="names">The names to read. May be empty.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// The states that were found, keyed by name. A name with nothing stored for it is absent from
    /// the result rather than present with a <c>default</c> value.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Every load of the dashboard and every call to the REST API's list endpoint reads the state of
    /// every checker. Done one at a time that is a round trip per checker on every page load, which
    /// a store measured in milliseconds turns into a page measured in seconds.
    /// </para>
    /// <para>
    /// Defaulted rather than abstract, so a provider written against the older interface keeps
    /// compiling and keeps working: the default does exactly what the caller used to do, one read
    /// per name. Override it where the store can answer in one query -- a single <c>SELECT ... IN</c>
    /// or one CosmosDB query -- and the saving is real.
    /// </para>
    /// </remarks>
    async Task<IReadOnlyDictionary<string, TState>> GetStatesAsync<TState>(
        IEnumerable<string> names,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);

        var states = new Dictionary<string, TState>(StringComparer.Ordinal);

        foreach (var name in names)
        {
            var state = await GetStateAsync<TState>(name, cancellationToken).ConfigureAwait(false);

            if (state is not null)
            {
                states[name] = state;
            }
        }

        return states;
    }
}
