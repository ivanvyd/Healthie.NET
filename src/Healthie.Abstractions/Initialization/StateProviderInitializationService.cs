using Healthie.Abstractions.StateProviding;
using Microsoft.Extensions.Hosting;

namespace Healthie.Abstractions.Initialization;

/// <summary>
/// Hosted service that initializes all registered state providers on application startup.
/// </summary>
public sealed class StateProviderInitializationService(
    IEnumerable<IStateProviderInitializer> initializers) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(initializers.Select(
            initializer => initializer.InitializeAsync(cancellationToken)))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
