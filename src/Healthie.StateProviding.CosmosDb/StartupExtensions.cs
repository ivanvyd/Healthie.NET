using Healthie.Abstractions.StateProviding;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.StateProviding.CosmosDb;

public static class StartupExtensions
{
    public static IServiceCollection AddHealthieCosmosDb(
        this IServiceCollection services,
        Container container)
    {
        if (container is null)
        {
            throw new ArgumentNullException(nameof(container));
        }

        CosmosDbStateProvider cosmosDbStateProvider = new(container);
        services.AddSingleton<IStateProvider>(cosmosDbStateProvider);
        services.AddSingleton<IAsyncStateProvider>(cosmosDbStateProvider);

        return services;
    }
}
