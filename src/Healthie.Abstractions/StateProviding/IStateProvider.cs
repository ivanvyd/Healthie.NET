namespace Healthie.Abstractions.StateProviding;

/// <summary>
/// Defines a contract for providing and managing pulse checker states.
/// </summary>
public interface IStateProvider
{
    /// <summary>
    /// The version to pass when the write should only land if nothing is stored yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without it, the very first write for a checker has no version to compare against and would
    /// go through unconditionally -- so two writers both finding nothing, both creating, and both
    /// writing would lose one of the two changes. The same lost update the version exists to
    /// prevent, at the one moment there is nothing to compare.
    /// </para>
    /// <para>
    /// <c>*</c> is the value HTTP gives this meaning in an <c>If-None-Match</c> header, and the one
    /// CosmosDB takes for the same purpose. No provider here generates a version that could collide
    /// with it.
    /// </para>
    /// </remarks>
    const string AbsentVersion = "*";

    /// <summary>
    /// Whether this provider can make a write conditional on the state not having changed.
    /// </summary>
    /// <remarks>
    /// Feature detection, so a caller can choose the conditional path rather than discovering by
    /// exception that it is unavailable. A provider that returns <c>true</c> must honour the version
    /// passed to <see cref="TrySetStateAsync"/>; one that returns <c>false</c> must refuse a
    /// versioned write rather than ignore the version.
    /// </remarks>
    bool SupportsOptimisticConcurrency => false;

    /// <summary>
    /// Gets a pulse checker's state together with the version it was read at.
    /// </summary>
    /// <typeparam name="TState">The type of state to retrieve.</typeparam>
    /// <param name="name">The unique name of the pulse checker.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The stored state and its version, or <c>null</c> if nothing is stored.</returns>
    /// <remarks>
    /// Defaulted to read without a version, so a provider written against the older interface keeps
    /// working and simply reports that its reads cannot be used for a conditional write.
    /// </remarks>
    async Task<StateEntry<TState>?> GetStateEntryAsync<TState>(
        string name,
        CancellationToken cancellationToken = default)
    {
        var state = await GetStateAsync<TState>(name, cancellationToken).ConfigureAwait(false);

        return state is null ? null : new StateEntry<TState>(state, Version: null);
    }

    /// <summary>
    /// Saves a pulse checker's state only if it has not changed since it was read.
    /// </summary>
    /// <typeparam name="TState">The type of state to save.</typeparam>
    /// <param name="name">The unique name of the pulse checker.</param>
    /// <param name="state">The state to save.</param>
    /// <param name="expectedVersion">
    /// The version the state was read at, from <see cref="GetStateEntryAsync"/>. Pass <c>null</c> to
    /// write unconditionally, which is what <see cref="SetStateAsync"/> does.
    /// </param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>true</c> if the state was written; <c>false</c> if something else had changed it since it
    /// was read, and this write was refused.
    /// </returns>
    /// <exception cref="NotSupportedException">
    /// A version was supplied and the provider cannot honour it.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Returns a result rather than throwing, unlike Orleans and EF Core, which raise
    /// <c>InconsistentStateException</c> and <c>DbUpdateConcurrencyException</c>. Under contention a
    /// conflict is the expected outcome, not an exceptional one, and the caller's answer is always
    /// the same: read again, reapply, retry. An exception on that path is noise in every log and a
    /// cost on every retry. <see cref="StateProviderExtensions.UpdateStateAsync"/> is that loop.
    /// </para>
    /// <para>
    /// The default refuses a versioned write rather than performing an unconditional one. Ignoring
    /// the version would lose exactly the update the version was passed to protect, and would do it
    /// silently -- which is worse than not offering the operation.
    /// </para>
    /// </remarks>
    async Task<bool> TrySetStateAsync<TState>(
        string name,
        TState state,
        string? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        if (expectedVersion is not null)
        {
            throw new NotSupportedException(
                $"{GetType().Name} cannot make a write conditional, so it cannot honour the version " +
                $"'{expectedVersion}'. Check {nameof(SupportsOptimisticConcurrency)} before passing one.");
        }

        await SetStateAsync(name, state, cancellationToken).ConfigureAwait(false);

        return true;
    }

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
