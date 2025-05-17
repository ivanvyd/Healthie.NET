using Healthie.Abstractions.StateProviding;
using Microsoft.Extensions.Hosting;

namespace Healthie.Abstractions.Initialization;

public sealed class StateProviderInitializationService : IHostedService
{
    private readonly IEnumerable<IStateProviderInitializer> _stateProviderInitializers;
    private readonly IEnumerable<IAsyncStateProviderInitializer> _asyncStateProviderInitializers;

    public StateProviderInitializationService(
        IEnumerable<IStateProviderInitializer> stateProviderInitializers,
        IEnumerable<IAsyncStateProviderInitializer> asyncStateProviderInitializers)
    {
        _stateProviderInitializers = stateProviderInitializers;
        _asyncStateProviderInitializers = asyncStateProviderInitializers;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var initializer in _stateProviderInitializers)
        {
            initializer.Initialize();
        }

        await Task.WhenAll(_asyncStateProviderInitializers.Select(initializer => initializer.InitializeAsync()));
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // No-op for this service
        return Task.CompletedTask;
    }
}
