using Healthie.Abstractions.StateProviding;
using Healthie.StateProviding.CosmosDb.Documents;
using Microsoft.Azure.Cosmos;
using System.Net;

namespace Healthie.StateProviding.CosmosDb;

public class CosmosDbStateProvider(Container container)
    : IStateProvider,
    IAsyncStateProvider
{
    private readonly Container _container = container;

    public TState? GetState<TState>(string name)
    {
        return GetStateAsync<TState>(name).GetAwaiter().GetResult();
    }

    public async Task<TState?> GetStateAsync<TState>(string name)
    {
        try
        {
            ItemResponse<StateDocument<TState>> response = await _container.ReadItemAsync<StateDocument<TState>>(name,
                new PartitionKey(name));

            return response.Resource.Value;
        }
        catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.NotFound)
        {
            return default;
        }
    }

    public void SetState<TState>(string name, TState state)
    {
        SetStateAsync(name, state).GetAwaiter().GetResult();
    }

    public async Task SetStateAsync<TState>(string name, TState state)
    {
        var stateDocument = new StateDocument<TState>(name, state);

        await _container.UpsertItemAsync(stateDocument, new PartitionKey(name));
    }
}
