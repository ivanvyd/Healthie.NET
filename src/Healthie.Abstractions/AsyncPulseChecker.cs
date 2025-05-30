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
    private readonly PulseInterval _initialInterval;
    private readonly uint _initialUnhealthyThreshold;
    private bool _isRunning = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncPulseChecker"/> class.
    /// </summary>
    /// <param name="stateProvider">The state provider used to manage pulse checker state.</param>
    protected AsyncPulseChecker(IAsyncStateProvider stateProvider) 
        : this(stateProvider, PulseInterval.EveryMinute, 0)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncPulseChecker"/> class with a specific interval.
    /// </summary>
    /// <param name="stateProvider">The state provider used to manage pulse checker state.</param>
    /// <param name="initialInterval">The initial interval at which the pulse checker operates.</param>
    protected AsyncPulseChecker(IAsyncStateProvider stateProvider, PulseInterval initialInterval)
        : this(stateProvider, initialInterval, 0)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncPulseChecker"/> class with a specific interval and unhealthy threshold.
    /// </summary>
    /// <param name="stateProvider">The state provider used to manage pulse checker state.</param>
    /// <param name="initialInterval">The initial interval at which the pulse checker operates.</param>
    /// <param name="unhealthyThreshold">The number of consecutive failures needed to consider the pulse checker unhealthy.</param>
    protected AsyncPulseChecker(IAsyncStateProvider stateProvider, PulseInterval initialInterval, uint unhealthyThreshold)
    {
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        _initialInterval = initialInterval;
        _initialUnhealthyThreshold = unhealthyThreshold;
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
            return await _stateProvider.GetStateAsync<PulseCheckerState>(Name) ?? new(_initialInterval, _initialUnhealthyThreshold);
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
    /// Sets the unhealthy threshold for the pulse checker asynchronously.
    /// </summary>
    /// <param name="threshold">The threshold to set.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SetUnhealthyThresholdAsync(uint threshold)
    {
        PulseCheckerState state = await GetStateAsync();
        if (state.UnhealthyThreshold == threshold)
        {
            return;
        }
        state.UnhealthyThreshold = threshold;
        await SetStateAsync(state);
    }

    /// <summary>
    /// Resets the pulse checker state to healthy asynchronously.
    /// </summary>
    /// <remarks>
    /// This resets the consecutive failures count, sets the health status to Healthy, and clears any error messages.
    /// </remarks>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ResetAsync()
    {
        PulseCheckerState state = await GetStateAsync();
        
        // Reset the consecutive failures count
        state.ConsecutiveFailureCount = 0;
        
        // Create a new healthy result
        state.LastResult = new PulseCheckerResult(
            PulseCheckerHealth.Healthy,
            string.Empty);
            
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
            
            // Update consecutive failure count based on the check result
            if (pulseResult.Health == PulseCheckerHealth.Healthy)
            {
                // Reset the failure count on success
                state.ConsecutiveFailureCount = 0;
            }
            else
            {
                // Increment the failure count on failure
                state.ConsecutiveFailureCount++;
                
                // Adjust the pulse result based on failure count if needed
                if (pulseResult.Health != PulseCheckerHealth.Unhealthy && 
                    state.ConsecutiveFailureCount > state.UnhealthyThreshold)
                {
                    // If we've reached the threshold but the result wasn't already unhealthy,
                    // create a new result with the unhealthy status
                    pulseResult = new PulseCheckerResult(
                        PulseCheckerHealth.Unhealthy, 
                        $"{pulseResult.Message} (Crossed unhealthy threshold: {state.ConsecutiveFailureCount}/{state.UnhealthyThreshold})");
                }
                else if (pulseResult.Health == PulseCheckerHealth.Unhealthy && 
                         state.ConsecutiveFailureCount <= state.UnhealthyThreshold)
                {
                    // If result is unhealthy but we haven't crossed the threshold,
                    // downgrade to suspicious
                    pulseResult = new PulseCheckerResult(
                        PulseCheckerHealth.Suspicious, 
                        $"{pulseResult.Message} (Suspicious: {state.ConsecutiveFailureCount}/{state.UnhealthyThreshold})");
                }
            }

            state.LastResult = pulseResult;
            await SetStateAsync(state);
        }
        catch (Exception ex)
        {
            PulseCheckerState state = await GetStateAsync();

            state.LastExecutionDateTime = currentDateTimeUtc;
            state.ConsecutiveFailureCount++;
            
            string message = $"{ex.GetType()}: {ex.Message}";
            
            // Determine if this should be unhealthy based on threshold
            var health = state.ConsecutiveFailureCount > state.UnhealthyThreshold 
                ? PulseCheckerHealth.Unhealthy
                : PulseCheckerHealth.Suspicious;
                
            state.LastResult = new(health, message);

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
