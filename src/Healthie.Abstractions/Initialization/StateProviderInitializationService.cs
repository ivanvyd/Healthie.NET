using Healthie.Abstractions.StateProviding;
using Microsoft.Extensions.Hosting;

namespace Healthie.Abstractions.Initialization;

/// <summary>
/// Service responsible for initializing state providers.
/// </summary>
public sealed class StateProviderInitializationService : IHostedService
{
    private readonly IEnumerable<IStateProviderInitializer> _stateProviderInitializers;
    private readonly IEnumerable<IAsyncStateProviderInitializer> _asyncStateProviderInitializers;

    /// <summary>
    /// Initializes a new instance of the <see cref="StateProviderInitializationService"/> class.
    /// </summary>
    /// <param name="stateProviderInitializers">A collection of synchronous state provider initializers.</param>
    /// <param name="asyncStateProviderInitializers">A collection of asynchronous state provider initializers.</param>
    public StateProviderInitializationService(
        IEnumerable<IStateProviderInitializer> stateProviderInitializers,
        IEnumerable<IAsyncStateProviderInitializer> asyncStateProviderInitializers)
    {
        _stateProviderInitializers = stateProviderInitializers;
        _asyncStateProviderInitializers = asyncStateProviderInitializers;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var initializer in _stateProviderInitializers)
        {
            initializer.Initialize();
        }

        await Task.WhenAll(_asyncStateProviderInitializers.Select(initializer => initializer.InitializeAsync()));
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
