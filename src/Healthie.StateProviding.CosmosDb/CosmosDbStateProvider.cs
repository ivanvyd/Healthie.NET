using Healthie.Abstractions.StateProviding;
using Healthie.StateProviding.CosmosDb.Documents;
using Microsoft.Azure.Cosmos;
using System.Net;

namespace Healthie.StateProviding.CosmosDb;

/// <summary>
/// Provides state persistence for pulse checkers using Azure CosmosDB.
/// </summary>
/// <remarks>
/// <para>
/// Writes are last-write-wins. <see cref="IStateProvider"/> hands this provider a complete state
/// snapshot and gives it no way to report a conflict back, so when two writers read the same state
/// and write it back concurrently -- a scheduled check and a dashboard-initiated setting change,
/// say -- whichever writes last is kept and the other's change is lost.
/// </para>
/// <para>
/// For check results that is the wanted behavior, since the most recent result is the interesting
/// one. Resolving it for setting changes needs a concurrency token on <see cref="IStateProvider"/>
/// itself: guarding the write with an ETag underneath the current interface can only turn a lost
/// update into a failed write, and a failed write is recorded as a failed health check.
/// </para>
/// </remarks>
/// <param name="container">The CosmosDB container to store state documents in.</param>
public class CosmosDbStateProvider(Container container) : IStateProvider
{
    private readonly Container _container = container
        ?? throw new ArgumentNullException(nameof(container));

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Thrown when the stored document records a state type other than <typeparamref name="TState"/>.
    /// </exception>
    public async Task<TState?> GetStateAsync<TState>(
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        try
        {
            ItemResponse<StateDocument<TState>> response =
                await _container.ReadItemAsync<StateDocument<TState>>(
                    name,
                    new PartitionKey(name),
                    cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            StateDocument<TState> stateDocument = response.Resource;
            EnsureStoredTypeMatches<TState>(name, stateDocument.StateType);

            return stateDocument.Value;
        }
        catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.NotFound)
        {
            return default;
        }
    }

    /// <inheritdoc />
    public async Task SetStateAsync<TState>(
        string name,
        TState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var stateDocument = new StateDocument<TState>(name, state);

        await _container.UpsertItemAsync(
            stateDocument,
            new PartitionKey(name),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies that the type recorded when the state was written is the type it is being read as.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The comparison deliberately ignores assembly version. This library's assembly version tracks
    /// its release version, so comparing assembly-qualified names would reject every document
    /// written by a previous release, and a pulse checker reports a failed read as a failed health
    /// check -- an upgrade would take every checker unhealthy on data that is perfectly valid.
    /// </para>
    /// <para>
    /// Releases up to 2.3.0 recorded the assembly-qualified name, which begins with the full name
    /// followed by a comma, so those documents are still accepted.
    /// </para>
    /// </remarks>
    internal static void EnsureStoredTypeMatches<TState>(string name, string? storedStateType)
    {
        // Documents written before the state type was recorded carry no type to compare against.
        if (string.IsNullOrWhiteSpace(storedStateType))
        {
            return;
        }

        // FullName is null only for types that cannot be named, such as open generic parameters,
        // which cannot reach this method as a concrete TState.
        var expectedStateType = typeof(TState).FullName;

        if (expectedStateType is null
            || string.Equals(storedStateType, expectedStateType, StringComparison.Ordinal)
            || storedStateType.StartsWith(expectedStateType + ",", StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(
            $"State stored for pulse checker '{name}' was written as '{storedStateType}' but is being " +
            $"read as '{expectedStateType}'. Migrate or delete the stored document before reading it " +
            "as a different type.");
    }
}
