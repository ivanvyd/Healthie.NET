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
public abstract class PulseChecker : IPulseChecker, IDisposable
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
    /// <remarks>
    /// Defaults to the checker's full type name. Override this to give a checker an identity of its
    /// own, which a checker that wraps something else needs: several instances of one adapter type
    /// would otherwise share a single name, and names identify checkers in storage, in the API, and
    /// on the dashboard.
    /// </remarks>
    public virtual string Name => GetType().FullName!;

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
        await AcquireAsync(cancellationToken).ConfigureAwait(false);
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
        await AcquireAsync(cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Takes the state lock, or throws if it cannot be taken within <see cref="DefaultTimeout"/>.
    /// </summary>
    /// <remarks>
    /// Throwing here rather than returning keeps callers from entering their <c>try</c>/<c>finally</c>
    /// without the lock, which would release a semaphore they never took.
    /// </remarks>
    /// <exception cref="TimeoutException">The lock could not be taken before the timeout elapsed.</exception>
    private async Task AcquireAsync(CancellationToken cancellationToken)
    {
        if (!await _semaphore.WaitAsync(DefaultTimeout, cancellationToken).ConfigureAwait(false))
        {
            throw new TimeoutException(
                $"Timed out after {DefaultTimeout.TotalSeconds:0.#}s waiting to access the state of pulse checker '{Name}'.");
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
        return [.. state.History];
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
    /// <para>
    /// This method is thread-safe. If a check is already in progress, this call returns immediately
    /// to prevent concurrent execution. Inside this method the state provider is called directly to
    /// avoid deadlocks with the semaphore used by <see cref="GetStateAsync"/> and
    /// <see cref="SetStateAsync"/>.
    /// </para>
    /// <para>
    /// A check that throws is recorded as a failed check: the monitored component is what failed.
    /// A state provider that throws is not, and the exception propagates instead. That failure is
    /// this library's own, and reporting it as a health result would tell operators that a healthy
    /// component is down.
    /// </para>
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
            var executedAt = DateTime.UtcNow;
            var result = await RunCheckAsync(cancellationToken).ConfigureAwait(false);

            PulseCheckerState state = await _stateProvider.GetStateAsync<PulseCheckerState>(Name, cancellationToken).ConfigureAwait(false)
                ?? new(_initialInterval, _initialUnhealthyThreshold);

            // History is a mutable list, so `with` alone would hand out a snapshot that still
            // shares it and would appear to change as this trigger appends to it.
            var oldState = state with { History = [.. state.History] };

            RecordResult(state, result, executedAt);

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

    /// <summary>
    /// Runs the derived class's check, turning a thrown exception into a failed result.
    /// </summary>
    /// <remarks>
    /// Cancellation is not a health signal: the check is being torn down, not failing. It is
    /// rethrown so the caller can tell the two apart. An exception raised by the check itself --
    /// including a timeout of its own -- is a failure of the component being monitored.
    /// </remarks>
    private async Task<PulseCheckerResult> RunCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await CheckAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Pulse check for '{CheckerName}' threw.", Name);

            return new PulseCheckerResult(PulseCheckerHealth.Unhealthy, $"{ex.GetType()}: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies a check result to the state: failure counting, threshold evaluation, and history.
    /// </summary>
    private void RecordResult(PulseCheckerState state, PulseCheckerResult result, DateTime executedAt)
    {
        state.LastExecutionDateTime = executedAt;

        if (result.Health == PulseCheckerHealth.Healthy)
        {
            state.ConsecutiveFailureCount = 0;
        }
        else
        {
            state.ConsecutiveFailureCount++;
            result = ApplyThreshold(result, state);
        }

        state.LastResult = result;

        if (!state.IsHistoryEnabled)
        {
            return;
        }

        state.History.Add(new PulseCheckerHistoryEntry(result.Health, result.Message, executedAt));

        if (state.History.Count > ConfiguredMaxHistoryLength)
        {
            state.History.RemoveRange(0, state.History.Count - ConfiguredMaxHistoryLength);
        }
    }

    /// <summary>
    /// Promotes a failure to unhealthy once consecutive failures pass the threshold, and holds it at
    /// suspicious until they do.
    /// </summary>
    private static PulseCheckerResult ApplyThreshold(PulseCheckerResult result, PulseCheckerState state)
    {
        if (result.Health != PulseCheckerHealth.Unhealthy &&
            state.ConsecutiveFailureCount > state.UnhealthyThreshold)
        {
            return new PulseCheckerResult(
                PulseCheckerHealth.Unhealthy,
                $"{result.Message} (Crossed unhealthy threshold: {state.ConsecutiveFailureCount}/{state.UnhealthyThreshold})");
        }

        if (result.Health == PulseCheckerHealth.Unhealthy &&
            state.ConsecutiveFailureCount <= state.UnhealthyThreshold)
        {
            return new PulseCheckerResult(
                PulseCheckerHealth.Suspicious,
                $"{result.Message} (Suspicious: {state.ConsecutiveFailureCount}/{state.UnhealthyThreshold})");
        }

        return result;
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
            return false;
        state.IsActive = true;
        await SetStateAsync(state, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Releases the resources used by this pulse checker.
    /// </summary>
    /// <remarks>
    /// Checkers are registered as singletons, so the container disposes them. Disposal is
    /// synchronous work, and implementing <see cref="IDisposable"/> alongside
    /// <see cref="IAsyncDisposable"/> keeps that working for containers disposed synchronously
    /// (for example <c>BuildServiceProvider()</c> in a <c>using</c> block), which reject
    /// services that are only asynchronously disposable.
    /// </remarks>
    public void Dispose()
    {
        _semaphore.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();

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
