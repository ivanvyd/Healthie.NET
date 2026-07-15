using Healthie.Abstractions.StateProviding;
using Microsoft.Azure.Cosmos;

namespace Healthie.StateProviding.CosmosDb;

/// <summary>
/// Creates the CosmosDB container backing <see cref="CosmosDbStateProvider"/> on startup if it does
/// not already exist, and verifies that an existing container is partitioned as the provider requires.
/// </summary>
/// <remarks>
/// The database itself must already exist; only the container is created.
/// </remarks>
/// <param name="container">The CosmosDB container that stores state documents.</param>
/// <param name="throughput">
/// The throughput to provision when the container is created, in request units per second, or
/// <c>null</c> to let the container share the throughput of its database.
/// </param>
public sealed class CosmosDbStateProviderInitializer(
    Container container,
    int? throughput = null) : IStateProviderInitializer
{
    /// <summary>
    /// The partition key path the container must use. <see cref="CosmosDbStateProvider"/> stores each
    /// pulse checker's state as a document whose <c>id</c> and partition key are the checker's name.
    /// </summary>
    public const string PartitionKeyPath = "/id";

    private readonly Container _container = container
        ?? throw new ArgumentNullException(nameof(container));

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Thrown when the container already exists with a partition key path other than
    /// <see cref="PartitionKeyPath"/>.
    /// </exception>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ContainerResponse response = await _container.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(_container.Id, PartitionKeyPath),
            throughput,
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // CreateContainerIfNotExistsAsync matches on the container id alone and does not validate the
        // partition key of a container that already exists, so an inherited container may be
        // partitioned by anything.
        var actualPartitionKeyPath = response.Resource.PartitionKeyPath;

        if (!string.Equals(actualPartitionKeyPath, PartitionKeyPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"CosmosDB container '{_container.Id}' is partitioned by '{actualPartitionKeyPath}', but " +
                $"Healthie.NET requires '{PartitionKeyPath}'. Recreate the container with the required " +
                "partition key path, or point Healthie.NET at a different container.");
        }
    }
}
