using Healthie.Abstractions.StateProviding;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.StateProviding.CosmosDb;

/// <summary>
/// Extension methods for registering the CosmosDB state provider with dependency injection.
/// </summary>
public static class StartupExtensions
{
    /// <summary>
    /// Registers the CosmosDB state provider with the service collection.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="container">
    /// A pre-configured CosmosDB <see cref="Container"/> instance.
    /// The consumer is responsible for creating and configuring the CosmosDB client and container.
    /// The container must use <c>/id</c> as the partition key path.
    /// </param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="container"/> is <c>null</c>.
    /// </exception>
    public static IServiceCollection AddHealthieCosmosDb(
        this IServiceCollection services,
        Container container)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(container);

        var cosmosDbStateProvider = new CosmosDbStateProvider(container);
        services.AddSingleton<IStateProvider>(cosmosDbStateProvider);

        return services;
    }
}
