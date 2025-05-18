using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Abstractions;

/// <summary>
/// Base class for implementing asynchronous pulse checkers.
/// </summary>
public abstract class AsyncPulseChecker : IAsyncPulseChecker, IAsyncDisposable
{
    private readonly IAsyncStateProvider _stateProvider;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(10);

    private bool _isRunning = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncPulseChecker"/> class.
    /// </summary>
    /// <param name="stateProvider">The state provider used to manage pulse checker state.</param>
    protected AsyncPulseChecker(IAsyncStateProvider stateProvider)
    {
        _stateProvider = stateProvider;
    }

    /// <summary>
    /// Gets the name of the pulse checker.
    /// </summary>
    public string Name => GetType().FullName!;

    /// <summary>
    /// Performs the pulse check asynchronously.
    /// </summary>
    /// <returns>A <see cref="PulseCheckerResult"/> representing the result of the pulse check.</returns>
    public abstract Task<PulseCheckerResult> CheckAsync();

    /// <summary>
    /// Sets the state of the pulse checker asynchronously.
    /// </summary>
    /// <param name="state">The state to set.</param>
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

    /// <summary>
    /// Gets the current state of the pulse checker asynchronously.
    /// </summary>
    /// <returns>The current <see cref="PulseCheckerState"/>.</returns>
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

    /// <summary>
    /// Sets the interval for the pulse checker asynchronously.
    /// </summary>
    /// <param name="interval">The interval to set.</param>
    public async Task SetIntervalAsync(PulseInterval interval)
    {
        PulseCheckerState state = await GetStateAsync();
        if (state.Interval == interval)
            return;
        state.Interval = interval;
        await SetStateAsync(state);
    }

    /// <summary>
    /// Triggers the pulse check asynchronously.
    /// </summary>
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

    /// <summary>
    /// Stops the pulse checker asynchronously.
    /// </summary>
    /// <returns><c>true</c> if the pulse checker was active and is now stopped; otherwise, <c>false</c>.</returns>
    public async Task<bool> StopAsync()
    {
        PulseCheckerState state = await GetStateAsync();
        if (!state.IsActive)
            return false;
        state.IsActive = false;
        await SetStateAsync(state);
        return true;
    }

    /// <summary>
    /// Starts the pulse checker asynchronously.
    /// </summary>
    /// <returns><c>true</c> if the pulse checker was already active; otherwise, <c>false</c>.</returns>
    public async Task<bool> StartAsync()
    {
        PulseCheckerState state = await GetStateAsync();
        if (state.IsActive)
            return true;
        state.IsActive = true;
        await SetStateAsync(state);
        return false;
    }

    /// <summary>
    /// Disposes the resources used by the pulse checker asynchronously.
    /// </summary>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    public ValueTask DisposeAsync()
    {
        _semaphore.Dispose();
        GC.SuppressFinalize(this);

        return ValueTask.CompletedTask;
    }
}
