using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Abstractions;

public abstract class AsyncPulseChecker(IAsyncStateProvider stateProvider)
    : IAsyncPulseChecker, IAsyncDisposable
{
    private readonly IAsyncStateProvider _stateProvider = stateProvider;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(10);

    private bool _isRunning = false;

    public abstract Task<PulseCheckerResult> CheckAsync();

    public string Name => GetType().FullName!;

    public async Task SetStateAsync(PulseCheckerState state)
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

    public async Task<PulseCheckerState> GetStateAsync()
    {
        await _semaphore.WaitAsync(_defaultTimeout);
        try
        {
            return await _stateProvider.GetStateAsync<PulseCheckerState>(Name) ?? new();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SetIntervalAsync(PulseInterval interval)
    {
        PulseCheckerState state = await GetStateAsync();
        if (state.Interval == interval)
            return;
        state.Interval = interval;
        await SetStateAsync(state);
    }

    public async Task TriggerAsync()
    {
        if (_isRunning)
            return;

        var currentDateTimeUtc = DateTime.UtcNow;

        try
        {
            _isRunning = true;

            var pulseResult = await CheckAsync();

            PulseCheckerState state = await GetStateAsync();

            state.LastExecutionDateTime = currentDateTimeUtc;
            state.LastResult = pulseResult;

            await SetStateAsync(state);
        }
        catch (Exception ex)
        {
            PulseCheckerState state = await GetStateAsync();

            state.LastExecutionDateTime = currentDateTimeUtc;
            string message = $"{ex.GetType()}: {ex.Message}";
            state.LastResult = new(false, message);

            await SetStateAsync(state);
        }
        finally
        {
            _isRunning = false;
        }
    }

    public async Task<bool> StopAsync()
    {
        PulseCheckerState state = await GetStateAsync();
        if (!state.IsActive)
            return false;
        state.IsActive = false;
        await SetStateAsync(state);
        return true;
    }

    public async Task<bool> StartAsync()
    {
        PulseCheckerState state = await GetStateAsync();
        if (state.IsActive)
            return true;
        state.IsActive = true;
        await SetStateAsync(state);
        return false;
    }

    public ValueTask DisposeAsync()
    {
        _semaphore.Dispose();
        GC.SuppressFinalize(this);

        return ValueTask.CompletedTask;
    }
}
