using Healthie.Abstractions.StateProviding;
using Healthie.StateProviding.CosmosDb.Documents;
using Microsoft.Azure.Cosmos;
using System.Net;

namespace Healthie.StateProviding.CosmosDb;

/// <summary>
/// Provides state persistence for pulse checkers using Azure CosmosDB.
/// </summary>
/// <param name="container">The CosmosDB container to store state documents in.</param>
public class CosmosDbStateProvider(Container container) : IStateProvider
{
    private readonly Container _container = container
        ?? throw new ArgumentNullException(nameof(container));

    /// <inheritdoc />
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

            return response.Resource.Value;
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
}
