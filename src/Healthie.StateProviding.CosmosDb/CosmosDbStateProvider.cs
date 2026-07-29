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
/// <see cref="SetStateAsync"/> is last-write-wins, which is what a check result wants: the most
/// recent result is the interesting one, and refusing it because a setting changed in between would
/// throw away the newer truth.
/// </para>
/// <para>
/// A setting change wants the opposite, and gets it from <see cref="TrySetStateAsync"/>, which
/// passes the version through as CosmosDB's own <c>_etag</c> on an <c>If-Match</c>. A write that
/// loses is refused rather than silently overwriting, and the caller reads again and reapplies.
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

    /// <inheritdoc />
    /// <remarks>CosmosDB stamps every document with an <c>_etag</c>, so versioning costs nothing extra.</remarks>
    public bool SupportsOptimisticConcurrency => true;

    /// <inheritdoc />
    public async Task<StateEntry<TState>?> GetStateEntryAsync<TState>(
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

            EnsureStoredTypeMatches<TState>(name, response.Resource.StateType);

            return response.Resource.Value is { } value
                ? new StateEntry<TState>(value, response.ETag)
                : null;
        }
        catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TrySetStateAsync<TState>(
        string name,
        TState state,
        string? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (expectedVersion == IStateProvider.AbsentVersion)
        {
            try
            {
                // Create rather than upsert: CosmosDB refuses a second create for the same id, which
                // is the guarantee wanted and needs no ETag to express.
                await _container.CreateItemAsync(
                    new StateDocument<TState>(name, state),
                    new PartitionKey(name),
                    cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.Conflict)
            {
                return false;
            }
        }

        var options = expectedVersion is null ? null : new ItemRequestOptions { IfMatchEtag = expectedVersion };

        try
        {
            await _container.UpsertItemAsync(
                new StateDocument<TState>(name, state),
                new PartitionKey(name),
                options,
                cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.PreconditionFailed)
        {
            // 412 is CosmosDB reporting that the document moved on. Expected under contention, so
            // it is a result rather than an exception by the time it reaches the caller.
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteStateAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        try
        {
            await _container.DeleteItemAsync<object>(
                name,
                new PartitionKey(name),
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.NotFound)
        {
            // Nothing stored for this checker, which is the state the caller wanted anyway.
            return false;
        }
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
