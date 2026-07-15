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
    /// The consumer is responsible for creating and configuring the CosmosDB client and the database.
    /// The container is created on startup if it does not exist, and must use
    /// <see cref="CosmosDbStateProviderInitializer.PartitionKeyPath"/> as its partition key path.
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

        return services.RegisterCosmosDbStateProvider(container, throughput: null);
    }

    /// <summary>
    /// Registers the CosmosDB state provider with the service collection, resolving the container
    /// from an existing <see cref="CosmosClient"/> and creating it on startup if it does not exist.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="client">
    /// A configured CosmosDB client. The consumer owns its lifetime and configuration.
    /// </param>
    /// <param name="databaseId">The identifier of an existing database to store state documents in.</param>
    /// <param name="containerId">
    /// The identifier of the container to store state documents in. It is created with
    /// <see cref="CosmosDbStateProviderInitializer.PartitionKeyPath"/> as its partition key path
    /// if it does not already exist.
    /// </param>
    /// <param name="throughput">
    /// The throughput to provision when the container is created, in request units per second, or
    /// <c>null</c> to let the container share the throughput of its database.
    /// </param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="client"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="databaseId"/> or <paramref name="containerId"/> is empty or whitespace.
    /// </exception>
    public static IServiceCollection AddHealthieCosmosDb(
        this IServiceCollection services,
        CosmosClient client,
        string databaseId,
        string containerId,
        int? throughput = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        return services.RegisterCosmosDbStateProvider(
            client.GetContainer(databaseId, containerId),
            throughput);
    }

    private static IServiceCollection RegisterCosmosDbStateProvider(
        this IServiceCollection services,
        Container container,
        int? throughput)
    {
        services.AddSingleton<IStateProvider>(new CosmosDbStateProvider(container));
        services.AddSingleton<IStateProviderInitializer>(
            new CosmosDbStateProviderInitializer(container, throughput));

        return services;
    }
}
