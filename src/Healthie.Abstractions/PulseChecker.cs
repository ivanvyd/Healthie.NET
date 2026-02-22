using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

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
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly PulseInterval _initialInterval;
    private readonly uint _initialUnhealthyThreshold;
    private string? _historyKey;

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
    {
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        _initialInterval = initialInterval;
        _initialUnhealthyThreshold = unhealthyThreshold;
    }

    /// <inheritdoc />
    public string Name => GetType().FullName!;

    /// <inheritdoc />
    public virtual string DisplayName => Name;

    /// <summary>
    /// Gets or sets the configured maximum history length from <see cref="HealthieOptions"/>.
    /// Set by the scheduler on startup.
    /// </summary>
    internal int ConfiguredMaxHistoryLength { get; set; } = 5;

    private string HistoryKey => _historyKey ??= $"{Name}:history";

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
        return await _stateProvider.GetStateAsync<List<PulseCheckerHistoryEntry>>(HistoryKey, cancellationToken).ConfigureAwait(false)
            ?? [];
    }

    /// <inheritdoc />
    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        await _stateProvider.SetStateAsync<List<PulseCheckerHistoryEntry>>(HistoryKey, [], cancellationToken).ConfigureAwait(false);
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
            return;

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

            // Save history if enabled
            if (state.IsHistoryEnabled)
            {
                await AppendHistoryEntryAsync(
                    new PulseCheckerHistoryEntry(pulseResult.Health, pulseResult.Message, currentDateTimeUtc),
                    ConfiguredMaxHistoryLength,
                    cancellationToken).ConfigureAwait(false);
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

            // Save history if enabled
            if (state.IsHistoryEnabled)
            {
                await AppendHistoryEntryAsync(
                    new PulseCheckerHistoryEntry(health, message, state.LastExecutionDateTime!.Value),
                    ConfiguredMaxHistoryLength,
                    default).ConfigureAwait(false);
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
        var history = await _stateProvider.GetStateAsync<List<PulseCheckerHistoryEntry>>(HistoryKey, cancellationToken).ConfigureAwait(false)
            ?? [];

        if (history.Count > ConfiguredMaxHistoryLength)
        {
            history.RemoveRange(0, history.Count - ConfiguredMaxHistoryLength);
            await _stateProvider.SetStateAsync(HistoryKey, history, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AppendHistoryEntryAsync(
        PulseCheckerHistoryEntry entry,
        int maxLength,
        CancellationToken cancellationToken)
    {
        var history = await _stateProvider.GetStateAsync<List<PulseCheckerHistoryEntry>>(HistoryKey, cancellationToken).ConfigureAwait(false)
            ?? [];

        history.Add(entry);

        if (history.Count > maxLength)
            history.RemoveRange(0, history.Count - maxLength);

        await _stateProvider.SetStateAsync(HistoryKey, history, cancellationToken).ConfigureAwait(false);
    }
}
