using Healthie.Abstractions.Diagnostics;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Healthie.Abstractions.StateProviding;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

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

    /// <summary>How many times a setting change is reapplied before giving up.</summary>
    /// <remarks>
    /// Contention is one check loop against one person editing, so a conflict is rare and a second
    /// one rarer. A larger number would only make a genuine livelock take longer to report.
    /// </remarks>
    private const int MaxUpdateAttempts = 5;
    private readonly IStateProvider _stateProvider;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly PulseInterval _initialInterval;
    private readonly uint _initialUnhealthyThreshold;
    private readonly PulseSchedule? _initialSchedule;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="PulseChecker"/> class on a schedule a
    /// <see cref="PulseInterval"/> may not be able to express.
    /// </summary>
    /// <param name="stateProvider">The state provider used to manage pulse checker state.</param>
    /// <param name="initialSchedule">The schedule the checker starts on.</param>
    /// <param name="unhealthyThreshold">The number of consecutive failures needed to consider the pulse checker unhealthy.</param>
    /// <param name="logger">An optional logger for diagnostic output.</param>
    /// <remarks>
    /// Seeds <see cref="PulseCheckerState.Schedule"/> the first time the checker runs, and never
    /// again -- so a schedule changed later is not reset on the next restart, exactly as an
    /// interval is not. A certificate-expiry check wants to run daily, which the interval enum
    /// stops well short of.
    /// </remarks>
    protected PulseChecker(
        IStateProvider stateProvider,
        PulseSchedule initialSchedule,
        uint unhealthyThreshold = 0,
        ILogger? logger = null)
    {
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        _initialSchedule = initialSchedule ?? throw new ArgumentNullException(nameof(initialSchedule));

        // Kept in step for anything still reading the interval, and only meaningful when the
        // schedule happens to be one the enum can name.
        _initialInterval = initialSchedule.TryToInterval(out var interval) ? interval : PulseInterval.EveryMinute;
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
    /// Gets the tags this checker starts with, used the first time it runs and never again.
    /// </summary>
    /// <remarks>
    /// Override to group checkers by whatever matters here -- the team that owns them, the tier
    /// they sit in, the region they run against. These seed <see cref="PulseCheckerState.Tags"/>
    /// only when no state has been stored yet; afterwards the stored tags win, so an edit made on
    /// the dashboard is not overwritten on the next restart.
    /// </remarks>
    public virtual IReadOnlyList<string> DefaultTags => [];

    /// <summary>
    /// Gets the group this checker starts in, used the first time it runs and never again.
    /// </summary>
    /// <remarks>
    /// A checker belongs to one group at most. Override to say where this one sits -- the
    /// subsystem it is part of, the team that owns it -- and the dashboard can then show a list
    /// where every checker appears exactly once, under exactly one heading. Use
    /// <see cref="DefaultTags"/> instead for labels that are allowed to overlap.
    /// </remarks>
    public virtual string? DefaultGroup => null;

    /// <summary>
    /// Gets or sets the configured maximum history length from <see cref="HealthieOptions"/>.
    /// Set by the scheduler on startup.
    /// </summary>
    internal int ConfiguredMaxHistoryLength { get; set; } = 10;

    /// <summary>
    /// Builds the state a checker starts from when the store holds nothing for it yet.
    /// </summary>
    private PulseCheckerState CreateInitialState() =>
        new(_initialInterval, _initialUnhealthyThreshold)
        {
            Schedule = _initialSchedule,
            Tags = NormalizeTags(DefaultTags),
            Group = NormalizeGroup(DefaultGroup),
        };

    /// <summary>
    /// Trims, drops blanks, de-duplicates case-insensitively, and orders a set of tags.
    /// </summary>
    /// <remarks>
    /// Used for both the tags declared in code and the tags set later, so that a checker seeded
    /// from <see cref="DefaultTags"/> and one tagged by hand hold their tags the same way. Ordering
    /// is what lets two states be compared for equality without the order they were typed in
    /// counting as a difference.
    /// </remarks>
    private static List<string> NormalizeTags(IEnumerable<string> tags) =>
    [
        .. tags
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
    ];

    /// <summary>Blank, whitespace and null all mean the same thing: no group.</summary>
    private static string? NormalizeGroup(string? group) =>
        string.IsNullOrWhiteSpace(group) ? null : group.Trim();

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
                ?? CreateInitialState();
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
                ?? CreateInitialState();
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

    /// <summary>
    /// Applies a change to this checker's stored state, and reapplies it if something else wrote
    /// first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This closes the gap every setting change used to have. Reading the state, changing it and
    /// writing it back is three steps, and a check finishing in between wrote its result over the
    /// change -- or had its own result written over. Against a provider that versions its writes,
    /// the write now only lands if nothing moved, and the change is reapplied to the newer state if
    /// something did.
    /// </para>
    /// <para>
    /// The change runs once per attempt, against freshly read state each time, so it must not
    /// depend on having seen the previous value.
    /// </para>
    /// <para>
    /// <c>StateChanged</c> is raised once, after the write that landed, comparing against the state
    /// that write was made from rather than whatever was read on the first attempt.
    /// </para>
    /// </remarks>
    /// <returns><c>true</c> if anything was written; <c>false</c> if the change was a no-op.</returns>
    private async Task<bool> UpdateStateAsync(Action<PulseCheckerState> apply, CancellationToken cancellationToken)
    {
        await AcquireAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var (oldState, newState, changed) = await ApplyAsync(apply, cancellationToken).ConfigureAwait(false);

            if (changed)
            {
                StateChanged?.Invoke(this, new PulseCheckerStateChangedEventArgs(oldState, newState));
            }

            return changed;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// The read-modify-write loop itself, without the lock and without the event.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="UpdateStateAsync"/> because <see cref="TriggerAsync"/> needs the
    /// same conditional write but already holds the semaphore and has its own telemetry to record
    /// between the write and the event. A checker's own result is state like any other: read,
    /// changed and written back over three steps, and a setting change landing in that gap used to
    /// be reverted by it -- the direction the semaphore hides within one process and cannot touch
    /// across two.
    /// </remarks>
    /// <returns>The state before and after, and whether anything was written.</returns>
    private async Task<(PulseCheckerState OldState, PulseCheckerState NewState, bool Changed)> ApplyAsync(
        Action<PulseCheckerState> apply,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var entry = await _stateProvider
                .GetStateEntryAsync<PulseCheckerState>(Name, cancellationToken)
                .ConfigureAwait(false);

            var state = entry?.Value ?? CreateInitialState();

            // History is a mutable list, so `with` alone would hand out a snapshot sharing it.
            var oldState = state with { History = [.. state.History] };

            apply(state);

            // Nothing to write, and nothing to tell anyone about.
            if (Equals(oldState, state))
            {
                return (oldState, state, false);
            }

            // Three cases, and collapsing any two of them is a bug. Nothing stored -> ask for a
            // create that loses to whoever creates first. Stored and versioned -> the version.
            // Stored but unversioned (a row written before the provider could version) -> null, an
            // unconditional write, because there is nothing to compare and demanding a version that
            // does not exist would refuse every write for ever.
            var version = _stateProvider.SupportsOptimisticConcurrency
                ? entry is null ? IStateProvider.AbsentVersion : entry.Version
                : null;

            if (await _stateProvider
                    .TrySetStateAsync(Name, state, version, cancellationToken)
                    .ConfigureAwait(false))
            {
                return (oldState, state, true);
            }

            if (attempt >= MaxUpdateAttempts)
            {
                throw new InvalidOperationException(
                    $"Could not update the state of pulse checker '{Name}' after {MaxUpdateAttempts} " +
                    "attempts: another writer changed it each time.");
            }
        }
    }

    /// <inheritdoc />
    public async Task SetIntervalAsync(PulseInterval interval, CancellationToken cancellationToken = default)
    {
        await UpdateStateAsync(state => state.Interval = interval, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetUnhealthyThresholdAsync(uint threshold, CancellationToken cancellationToken = default)
    {
        await UpdateStateAsync(state => state.UnhealthyThreshold = threshold, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetTagsAsync(IReadOnlyList<string> tags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var normalized = NormalizeTags(tags);

        await UpdateStateAsync(state => state.Tags = [.. normalized], cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetGroupAsync(string? group, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeGroup(group);

        await UpdateStateAsync(state => state.Group = normalized, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetPinnedAsync(bool pinned, CancellationToken cancellationToken = default)
    {
        await UpdateStateAsync(state => state.IsPinned = pinned, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await UpdateStateAsync(
            state =>
            {
                state.ConsecutiveFailureCount = 0;
                state.LastResult = new PulseCheckerResult(PulseCheckerHealth.Healthy, string.Empty);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<List<PulseCheckerHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        var state = await _stateProvider.GetStateAsync<PulseCheckerState>(Name, cancellationToken).ConfigureAwait(false)
            ?? CreateInitialState();
        return [.. state.History];
    }

    /// <inheritdoc />
    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        var state = await _stateProvider.GetStateAsync<PulseCheckerState>(Name, cancellationToken).ConfigureAwait(false)
            ?? CreateInitialState();
        state.History = [];
        await _stateProvider.SetStateAsync(Name, state, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetHistoryEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await UpdateStateAsync(state => state.IsHistoryEnabled = enabled, cancellationToken).ConfigureAwait(false);
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
            if (_logger is not null)
            {
                Log.OverlappingTriggerSkipped(_logger, Name);
            }

            // A checker whose check outlasts its interval surfaces here and nowhere else: it keeps
            // reporting healthy while quietly running at a fraction of the rate it was asked to.
            HealthieDiagnostics.OverlappedTriggers.Add(
                1,
                new KeyValuePair<string, object?>(HealthieDiagnostics.CheckerNameTag, Name));

            return;
        }

        using var activity = HealthieDiagnostics.ActivitySource.StartActivity(
            "Healthie.Check",
            ActivityKind.Internal);

        activity?.SetTag(HealthieDiagnostics.CheckerNameTag, Name);

        try
        {
            var executedAt = DateTime.UtcNow;

            // Times the user's check alone. Reading and writing state is this library's own work,
            // and folding it in would report a component as slower than it is.
            var startedAt = Stopwatch.GetTimestamp();
            var result = await RunCheckAsync(cancellationToken).ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(startedAt);

            var (oldState, state, changed) = await ApplyAsync(
                current => RecordResult(current, result, executedAt),
                cancellationToken).ConfigureAwait(false);

            // Recorded after the write, so a check whose state could not be stored is not counted
            // as one that ran -- the same reason a storage failure is not a health result.
            RecordTelemetry(activity, state, oldState, elapsed);

            if (changed)
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
            if (_logger is not null)
            {
                Log.CheckThrew(_logger, ex, Name);
            }

            return new PulseCheckerResult(PulseCheckerHealth.Unhealthy, $"{ex.GetType()}: {ex.Message}");
        }
    }

    /// <summary>
    /// Records what this check did, for metrics, tracing and the log.
    /// </summary>
    /// <remarks>
    /// Only the checker's name and group become tags. Its tags are user-defined, editable from the
    /// dashboard and unbounded, and every distinct value multiplies the series a backend stores.
    /// </remarks>
    private void RecordTelemetry(
        Activity? activity,
        PulseCheckerState state,
        PulseCheckerState previous,
        TimeSpan elapsed)
    {
        var health = state.LastResult?.Health ?? PulseCheckerHealth.Healthy;
        var previousHealth = previous.LastResult?.Health;

        var name = new KeyValuePair<string, object?>(HealthieDiagnostics.CheckerNameTag, Name);
        var group = new KeyValuePair<string, object?>(HealthieDiagnostics.CheckerGroupTag, state.Group);
        var result = new KeyValuePair<string, object?>(HealthieDiagnostics.ResultTag, health.ToString());

        HealthieDiagnostics.CheckDuration.Record(elapsed.TotalSeconds, name, group, result);
        HealthieDiagnostics.CheckResults.Add(1, name, group, result);

        activity?.SetTag(HealthieDiagnostics.CheckerGroupTag, state.Group);
        activity?.SetTag(HealthieDiagnostics.ResultTag, health.ToString());

        // Deliberately not `changed`. State equality includes LastExecutionDateTime, which moves on
        // every tick, so "the state differs" is true of every check and would make this a second
        // copy of the results counter. What is worth alerting on is the health itself moving.
        if (previousHealth == health)
        {
            return;
        }

        HealthieDiagnostics.StateTransitions.Add(1, name, group, result);

        if (_logger is not null)
        {
            Log.StateChanged(
                _logger,
                Name,
                previousHealth?.ToString() ?? "none",
                health.ToString());
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
        return await UpdateStateAsync(state => state.IsActive = false, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        return await UpdateStateAsync(state => state.IsActive = true, cancellationToken).ConfigureAwait(false);
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
