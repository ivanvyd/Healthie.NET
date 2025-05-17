using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Abstractions;

public abstract class AsyncPulseChecker(IAsyncStateProvider stateProvider)
    : IAsyncPulseChecker, IAsyncDisposable
{
    private readonly IAsyncStateProvider _stateProvider = stateProvider;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(10);

    public abstract Task<Pulse<Result>> CheckAsync();

    public string Name => GetType().FullName!;

    public async Task SetStateAsync(State state)
    {
        await _semaphore.WaitAsync(_defaultTimeout);
        try
        {
            await _stateProvider.SetStateAsync(Name, state);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<State> GetStateAsync()
    {
        await _semaphore.WaitAsync(_defaultTimeout);
        try
        {
            return await _stateProvider.GetStateAsync<State>(Name) ?? new();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SetIntervalAsync(PulseInterval interval)
    {
        State state = await GetStateAsync();
        state.Interval = interval;
        await SetStateAsync(state);
    }

    public async Task TriggerAsync()
    {
        var currentDateTimeUtc = DateTime.UtcNow;

        var pulseResult = await CheckAsync();

        State state = await GetStateAsync();

        state.LastExecutionDateTime = currentDateTimeUtc;
        state.LastPulse = pulseResult;

        await SetStateAsync(state);
    }

    public ValueTask DisposeAsync()
    {
        _semaphore.Dispose();
        GC.SuppressFinalize(this);

        return ValueTask.CompletedTask;
    }
}
