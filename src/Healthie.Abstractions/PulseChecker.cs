using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;
using Microsoft.Extensions.Logging;

namespace Healthie.Abstractions;

/// <summary>
/// Abstract base class for implementing pulse checkers that monitor the health of a component or service.
/// </summary>
/// <remarks>
/// <para>
/// Derive from this class and override <see cref="CheckAsync"/> to implement custom health check logic.
/// The base class handles state management, threshold evaluation, concurrency control, and event notifications.
/// </para>
/// <para>
/// Concurrent calls to <see cref="TriggerAsync"/> are prevented using a <see cref="SemaphoreSlim"/>.
/// If a trigger is already executing, subsequent calls return immediately without executing.
/// </para>
/// </remarks>
public abstract class PulseChecker : IPulseChecker
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private readonly IStateProvider _stateProvider;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly PulseInterval _initialInterval;
    private readonly uint _initialUnhealthyThreshold;

    /// <summary>
    /// Initializes a new instance of the <see cref="PulseChecker"/> class with default interval and threshold.
    /// </summary>
    /// <param name="stateProvider">The state provider used to manage pulse checker state.</param>
    protected PulseChecker(IStateProvider stateProvider)
        : this(stateProvider, PulseInterval.EveryMinute, 0)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PulseChecker"/> class with a specific interval.
    /// </summary>
    /// <param name="stateProvider">The state provider used to manage pulse checker state.</param>
    /// <param name="initialInterval">The initial interval at which the pulse checker operates.</param>
    protected PulseChecker(IStateProvider stateProvider, PulseInterval initialInterval)
        : this(stateProvider, initialInterval, 0)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PulseChecker"/> class with a specific interval and unhealthy threshold.
    /// </summary>
    /// <param name="stateProvider">The state provider used to manage pulse checker state.</param>
    /// <param name="initialInterval">The initial interval at which the pulse checker operates.</param>
    /// <param name="unhealthyThreshold">The number of consecutive failures needed to consider the pulse checker unhealthy.</param>
    protected PulseChecker(IStateProvider stateProvider, PulseInterval initialInterval, uint unhealthyThreshold)
        : this(stateProvider, initialInterval, unhealthyThreshold, logger: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PulseChecker"/> class with a specific interval, unhealthy threshold, and logger.
    /// </summary>
    /// <param name="stateProvider">The state provider used to manage pulse checker state.</param>
    /// <param name="initialInterval">The initial interval at which the pulse checker operates.</param>
    /// <param name="unhealthyThreshold">The number of consecutive failures needed to consider the pulse checker unhealthy.</param>
    /// <param name="logger">An optional logger for diagnostic output.</param>
    protected PulseChecker(IStateProvider stateProvider, PulseInterval initialInterval, uint unhealthyThreshold, ILogger? logger)
    {
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        _initialInterval = initialInterval;
        _initialUnhealthyThreshold = unhealthyThreshold;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => GetType().FullName!;

    /// <inheritdoc />
    public virtual string DisplayName => Name;

    /// <summary>
    /// Gets or sets the configured maximum history length from <see cref="HealthieOptions"/>.
    /// Set by the scheduler on startup.
    /// </summary>
    internal int ConfiguredMaxHistoryLength { get; set; } = 10;

    /// <summary>
    /// Performs the pulse check asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="PulseCheckerResult"/> representing the result of the pulse check.</returns>
    public abstract Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public event EventHandler<PulseCheckerStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public async Task SetStateAsync(PulseCheckerState state, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(DefaultTimeout, cancellationToken).ConfigureAwait(false);
        try
        {
            var oldState = await _stateProvider.GetStateAsync<PulseCheckerState>(Name, cancellationToken).ConfigureAwait(false)
                ?? new(_initialInterval, _initialUnhealthyThreshold);
            await _stateProvider.SetStateAsync(Name, state, cancellationToken).ConfigureAwait(false);
            if (!Equals(oldState, state))
            {
                StateChanged?.Invoke(this, new PulseCheckerStateChangedEventArgs(oldState, state));
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<PulseCheckerState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(DefaultTimeout, cancellationToken).ConfigureAwait(false);
        try
        {
            return await _stateProvider.GetStateAsync<PulseCheckerState>(Name, cancellationToken).ConfigureAwait(false)
                ?? new(_initialInterval, _initialUnhealthyThreshold);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task SetIntervalAsync(PulseInterval interval, CancellationToken cancellationToken = default)
    {
        PulseCheckerState state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (state.Interval == interval)
            return;
        state.Interval = interval;
        await SetStateAsync(state, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetUnhealthyThresholdAsync(uint threshold, CancellationToken cancellationToken = default)
    {
        PulseCheckerState state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (state.UnhealthyThreshold == threshold)
        {
            return;
        }
        state.UnhealthyThreshold = threshold;
        await SetStateAsync(state, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        PulseCheckerState state = await GetStateAsync(cancellationToken).ConfigureAwait(false);

        state.ConsecutiveFailureCount = 0;

        state.LastResult = new PulseCheckerResult(
            PulseCheckerHealth.Healthy,
            string.Empty);

        await SetStateAsync(state, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<List<PulseCheckerHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        var state = await _stateProvider.GetStateAsync<PulseCheckerState>(Name, cancellationToken).ConfigureAwait(false)
            ?? new(_initialInterval, _initialUnhealthyThreshold);
        return state.History;
    }

    /// <inheritdoc />
    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        var state = await _stateProvider.GetStateAsync<PulseCheckerState>(Name, cancellationToken).ConfigureAwait(false)
            ?? new(_initialInterval, _initialUnhealthyThreshold);
        state.History = [];
        await _stateProvider.SetStateAsync(Name, state, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetHistoryEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        PulseCheckerState state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (state.IsHistoryEnabled == enabled)
            return;
        state.IsHistoryEnabled = enabled;
        await SetStateAsync(state, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// This method is thread-safe. If a check is already in progress,
    /// this call will return immediately to prevent concurrent execution.
    /// Inside this method, the state provider is called directly to avoid
    /// deadlocks with the semaphore used by <see cref="GetStateAsync"/> and <see cref="SetStateAsync"/>.
    /// </remarks>
    public async Task TriggerAsync(CancellationToken cancellationToken = default)
    {
        if (!await _semaphore.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            _logger?.LogDebug("Skipping trigger for '{CheckerName}' — previous execution is still running.", Name);
            return;
        }

        try
        {
            var currentDateTimeUtc = DateTime.UtcNow;

            var pulseResult = await CheckAsync(cancellationToken).ConfigureAwait(false);

            PulseCheckerState state = await _stateProvider.GetStateAsync<PulseCheckerState>(Name, cancellationToken).ConfigureAwait(false)
                ?? new(_initialInterval, _initialUnhealthyThreshold);

            var oldState = state with { };
            state.LastExecutionDateTime = currentDateTimeUtc;

            if (pulseResult.Health == PulseCheckerHealth.Healthy)
            {
                state.ConsecutiveFailureCount = 0;
            }
            else
            {
                state.ConsecutiveFailureCount++;

                if (pulseResult.Health != PulseCheckerHealth.Unhealthy &&
                    state.ConsecutiveFailureCount > state.UnhealthyThreshold)
                {
                    pulseResult = new PulseCheckerResult(
                        PulseCheckerHealth.Unhealthy,
                        $"{pulseResult.Message} (Crossed unhealthy threshold: {state.ConsecutiveFailureCount}/{state.UnhealthyThreshold})");
                }
                else if (pulseResult.Health == PulseCheckerHealth.Unhealthy &&
                         state.ConsecutiveFailureCount <= state.UnhealthyThreshold)
                {
                    pulseResult = new PulseCheckerResult(
                        PulseCheckerHealth.Suspicious,
                        $"{pulseResult.Message} (Suspicious: {state.ConsecutiveFailureCount}/{state.UnhealthyThreshold})");
                }
            }

            state.LastResult = pulseResult;

            // Append history if enabled
            if (state.IsHistoryEnabled)
            {
                state.History.Add(new PulseCheckerHistoryEntry(pulseResult.Health, pulseResult.Message, currentDateTimeUtc));
                if (state.History.Count > ConfiguredMaxHistoryLength)
                    state.History.RemoveRange(0, state.History.Count - ConfiguredMaxHistoryLength);
            }

            await _stateProvider.SetStateAsync(Name, state, cancellationToken).ConfigureAwait(false);
            if (!Equals(oldState, state))
            {
                StateChanged?.Invoke(this, new PulseCheckerStateChangedEventArgs(oldState, state));
            }
        }
        catch (Exception ex)
        {
            PulseCheckerState state = await _stateProvider.GetStateAsync<PulseCheckerState>(Name, default).ConfigureAwait(false)
                ?? new(_initialInterval, _initialUnhealthyThreshold);

            var oldState = state with { };
            state.LastExecutionDateTime = DateTime.UtcNow;
            state.ConsecutiveFailureCount++;

            string message = $"{ex.GetType()}: {ex.Message}";

            var health = state.ConsecutiveFailureCount > state.UnhealthyThreshold
                ? PulseCheckerHealth.Unhealthy
                : PulseCheckerHealth.Suspicious;

            state.LastResult = new(health, message);

            // Append history if enabled
            if (state.IsHistoryEnabled)
            {
                state.History.Add(new PulseCheckerHistoryEntry(health, message, state.LastExecutionDateTime!.Value));
                if (state.History.Count > ConfiguredMaxHistoryLength)
                    state.History.RemoveRange(0, state.History.Count - ConfiguredMaxHistoryLength);
            }

            await _stateProvider.SetStateAsync(Name, state, default).ConfigureAwait(false);
            if (!Equals(oldState, state))
            {
                StateChanged?.Invoke(this, new PulseCheckerStateChangedEventArgs(oldState, state));
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> StopAsync(CancellationToken cancellationToken = default)
    {
        PulseCheckerState state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (!state.IsActive)
            return false;
        state.IsActive = false;
        await SetStateAsync(state, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        PulseCheckerState state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (state.IsActive)
            return true;
        state.IsActive = true;
        await SetStateAsync(state, cancellationToken).ConfigureAwait(false);
        return false;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _semaphore.Dispose();
        GC.SuppressFinalize(this);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Trims the history to match <see cref="ConfiguredMaxHistoryLength"/>.
    /// Called by the scheduler on startup.
    /// </summary>
    internal async Task TrimHistoryAsync(CancellationToken cancellationToken = default)
    {
        var state = await _stateProvider.GetStateAsync<PulseCheckerState>(Name, cancellationToken).ConfigureAwait(false);
        if (state is null) return;

        if (state.History.Count > ConfiguredMaxHistoryLength)
        {
            state.History.RemoveRange(0, state.History.Count - ConfiguredMaxHistoryLength);
            await _stateProvider.SetStateAsync(Name, state, cancellationToken).ConfigureAwait(false);
        }
    }
}
