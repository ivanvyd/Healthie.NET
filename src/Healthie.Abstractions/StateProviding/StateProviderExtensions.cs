namespace Healthie.Abstractions.StateProviding;

/// <summary>
/// Helpers over <see cref="IStateProvider"/>.
/// </summary>
public static class StateProviderExtensions
{
    /// <summary>How many times an update is reapplied before giving up.</summary>
    /// <remarks>
    /// Contention here is two writers on one checker -- a scheduled check and someone editing it --
    /// so a conflict is rare and a second conflict on the retry rarer still. A handful of attempts
    /// is generous; a number much larger would only turn a genuine livelock into a slow one.
    /// </remarks>
    public const int DefaultMaxAttempts = 5;

    /// <summary>
    /// Reads a state, applies a change to it, and writes it back only if nothing else changed it in
    /// between -- reapplying the change against the newer state if something did.
    /// </summary>
    /// <typeparam name="TState">The type of state to update.</typeparam>
    /// <param name="provider">The provider holding the state.</param>
    /// <param name="name">The unique name of the pulse checker.</param>
    /// <param name="update">
    /// Applies the change. Called with the current state, and again with a freshly read state on
    /// each retry, so it must be safe to run more than once and must not depend on what it saw
    /// before.
    /// </param>
    /// <param name="create">Builds the state to store when nothing is stored yet.</param>
    /// <param name="maxAttempts">How many times to try. Defaults to <see cref="DefaultMaxAttempts"/>.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The state as written.</returns>
    /// <exception cref="InvalidOperationException">
    /// The update kept losing to another writer, <paramref name="maxAttempts"/> times.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This is the read-modify-write loop the Azure SDK guidance describes for conditional requests,
    /// in the one place that has to get it right rather than in every caller.
    /// </para>
    /// <para>
    /// Against a provider that does not version, this degrades to exactly what the library did
    /// before -- read, change, write, last writer wins. That is not a silent downgrade: it is what
    /// an unversioned store can offer, and <see cref="IStateProvider.SupportsOptimisticConcurrency"/>
    /// says which one you have.
    /// </para>
    /// </remarks>
    public static async Task<TState> UpdateStateAsync<TState>(
        this IStateProvider provider,
        string name,
        Action<TState> update,
        Func<TState> create,
        int maxAttempts = DefaultMaxAttempts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        for (var attempt = 1; ; attempt++)
        {
            var entry = await provider.GetStateEntryAsync<TState>(name, cancellationToken).ConfigureAwait(false);
            var state = entry is null ? create() : entry.Value;

            update(state);

            // A provider that cannot version reports no version, and passing null asks for the
            // unconditional write it is able to do rather than one it would have to refuse.
            // Three cases, and collapsing any two of them is a bug. Nothing stored -> ask for a
            // create that loses to whoever creates first. Stored and versioned -> the version.
            // Stored but unversioned (a row written before the provider could version) -> null, an
            // unconditional write, because there is nothing to compare and demanding a version that
            // does not exist would refuse every write for ever.
            var version = provider.SupportsOptimisticConcurrency
                ? entry is null ? IStateProvider.AbsentVersion : entry.Version
                : null;

            if (await provider.TrySetStateAsync(name, state, version, cancellationToken).ConfigureAwait(false))
            {
                return state;
            }

            if (attempt >= maxAttempts)
            {
                throw new InvalidOperationException(
                    $"Could not update the state of pulse checker '{name}' after {maxAttempts} attempts: " +
                    "another writer changed it each time.");
            }
        }
    }
}
