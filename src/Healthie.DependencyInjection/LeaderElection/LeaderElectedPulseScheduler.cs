using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Healthie.Abstractions.StateProviding;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Healthie.LeaderElection;

/// <summary>
/// Wraps a scheduler so it only runs checks while this replica is the leader.
/// </summary>
/// <remarks>
/// <para>
/// Without this, every replica of a scaled-out application runs every check. Three replicas mean a
/// database is asked three times whether it is healthy, three sets of results race to write the
/// same state document under last-write-wins, and once alerting is on, one outage pages somebody
/// three times.
/// </para>
/// <para>
/// A decorator rather than a change to the schedulers, so it works with every one of them --
/// timer, Quartz, Hangfire, Coravel, Temporal -- and so an application that does not want it is
/// unaffected. What is requested is remembered here; what is actually scheduled downstream depends
/// on whether this replica currently leads.
/// </para>
/// </remarks>
public sealed class LeaderElectedPulseScheduler : IPulseScheduler
{
    private const int MaxReconciliationConcurrency = 16;

    private readonly IPulseScheduler _inner;
    private readonly ILogger<LeaderElectedPulseScheduler>? _logger;
    private readonly IReadOnlyDictionary<string, IPulseChecker> _registered;
    private readonly IStateProvider? _stateProvider;
    private readonly Dictionary<string, (IPulseChecker Checker, PulseSchedule Schedule)> _requested = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (IPulseChecker Checker, PulseSchedule Schedule)> _running = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private volatile bool _isLeader;

    /// <summary>
    /// Initializes a scheduler that reconciles checkers it is explicitly asked to schedule.
    /// </summary>
    /// <param name="inner">The scheduler that does the real work when this replica leads.</param>
    /// <param name="logger">An optional logger for diagnostic output.</param>
    public LeaderElectedPulseScheduler(
        IPulseScheduler inner,
        ILogger<LeaderElectedPulseScheduler>? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _registered = new Dictionary<string, IPulseChecker>(StringComparer.Ordinal);
        _logger = logger;
    }

    /// <summary>
    /// Initializes the DI-managed scheduler with every checker so it can discover state changes
    /// made by another replica, including activation of one that was inactive at startup.
    /// </summary>
    internal LeaderElectedPulseScheduler(
        IPulseScheduler inner,
        IEnumerable<IPulseChecker> registered,
        IStateProvider stateProvider,
        ILogger<LeaderElectedPulseScheduler>? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        ArgumentNullException.ThrowIfNull(registered);
        _registered = registered.ToDictionary(checker => checker.Name, StringComparer.Ordinal);
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        _logger = logger;
    }

    /// <summary>Whether this replica currently runs the checks.</summary>
    public bool IsLeader => _isLeader;

    /// <inheritdoc />
    public Task ScheduleAsync(
        IPulseChecker checker,
        PulseInterval interval,
        CancellationToken cancellationToken = default) =>
        ScheduleAsync(checker, PulseSchedule.FromInterval(interval), cancellationToken);

    /// <inheritdoc />
    public async Task ScheduleAsync(
        IPulseChecker checker,
        PulseSchedule schedule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(schedule);

        if (!_inner.TryValidateSchedule(schedule, out var validationError))
        {
            throw new ArgumentException(
                $"Schedule '{schedule}' for pulse checker '{checker.Name}' cannot be used by " +
                $"{_inner.GetType().Name}. {validationError}",
                nameof(schedule));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_isLeader)
            {
                await _inner.ScheduleAsync(checker, schedule, cancellationToken).ConfigureAwait(false);
                _running[checker.Name] = (checker, schedule);
            }

            // Remembered after the wrapped scheduler accepts it. A rejected replacement must leave
            // both the live schedule and the request used on the next renewal unchanged.
            _requested[checker.Name] = (checker, schedule);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public bool TryValidateSchedule(PulseSchedule schedule, out string? error) =>
        _inner.TryValidateSchedule(schedule, out error);

    /// <inheritdoc />
    public async Task UnscheduleAsync(IPulseChecker checker, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checker);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _requested.Remove(checker.Name);

            // Forwarded even while following: the inner scheduler may still hold it from a period
            // when this replica led.
            await _inner.UnscheduleAsync(checker, cancellationToken).ConfigureAwait(false);
            _running.Remove(checker.Name);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Takes over the checks, scheduling everything that has been requested.
    /// </summary>
    internal async Task BecomeLeaderAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var wasLeader = _isLeader;
            var (desired, inactive) = await GetDesiredSchedulesAsync(cancellationToken).ConfigureAwait(false);

            // Set before touching the inner scheduler. If an adapter fails part-way through,
            // LeaderElectionService calls StandDownAsync and can see that cleanup is required.
            _isLeader = true;
            var toStop = _running.Values
                .Where(entry => !desired.ContainsKey(entry.Checker.Name))
                .ToDictionary(entry => entry.Checker.Name, entry => entry.Checker, StringComparer.Ordinal);

            // A durable scheduler can still hold work created by the previous process. On a fresh
            // takeover, explicitly remove every checker whose persisted state says it is inactive,
            // even though this wrapper has no local _running entry for that old job.
            if (!wasLeader)
            {
                foreach (var (name, entry) in inactive)
                {
                    // Treat a persisted inactive durable job as running until the adapter confirms
                    // its removal. A failed cleanup then remains visible to standdown and prevents
                    // the lease from being released over work that may still be live.
                    _running[name] = entry;
                    toStop[name] = entry.Checker;
                }
            }

            var stopped = await RunBoundedAsync(
                toStop,
                (entry, token) => _inner.UnscheduleAsync(entry.Value, token),
                cancellationToken).ConfigureAwait(false);
            foreach (var result in stopped.Where(result => result.Error is null))
            {
                _running.Remove(result.Item.Key);
            }
            ThrowFirstFailure(stopped);

            var toStart = desired
                .Where(entry => !_running.TryGetValue(entry.Key, out var current)
                    || current.Schedule != entry.Value.Schedule)
                .ToList();
            var started = await RunBoundedAsync(
                toStart,
                (entry, token) => _inner.ScheduleAsync(entry.Value.Checker, entry.Value.Schedule, token),
                cancellationToken).ConfigureAwait(false);
            foreach (var result in started.Where(result => result.Error is null))
            {
                _running[result.Item.Key] = result.Item.Value;
            }
            ThrowFirstFailure(started);

            _logger?.LogInformation("Took or renewed leadership; now running {CheckerCount} pulse checkers.", desired.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Stands down, stopping every check without forgetting what was requested.
    /// </summary>
    /// <remarks>
    /// What was requested is kept, so leadership can be taken again without the application having
    /// to re-register anything.
    /// </remarks>
    internal async Task StandDownAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!_isLeader && _running.Count == 0)
            {
                return;
            }

            _isLeader = false;
            var stopped = await RunBoundedAsync(
                _running.ToList(),
                (entry, token) => _inner.UnscheduleAsync(entry.Value.Checker, token),
                cancellationToken).ConfigureAwait(false);
            foreach (var result in stopped.Where(result => result.Error is null))
            {
                _running.Remove(result.Item.Key);
            }

            _logger?.LogInformation("Stood down; another replica is running the pulse checkers.");
            ThrowFirstFailure(stopped);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Builds the schedules this replica should run from current persisted state for every checker
    /// registered through DI. Explicitly scheduled ad-hoc checkers retain their requested schedule.
    /// </summary>
    private async Task<(
        Dictionary<string, (IPulseChecker Checker, PulseSchedule Schedule)> Desired,
        Dictionary<string, (IPulseChecker Checker, PulseSchedule Schedule)> Inactive)> GetDesiredSchedulesAsync(
        CancellationToken cancellationToken)
    {
        var desired = _requested
            .Where(entry => !_registered.ContainsKey(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        var inactive = new Dictionary<string, (IPulseChecker Checker, PulseSchedule Schedule)>(StringComparer.Ordinal);

        if (_stateProvider is null || _registered.Count == 0)
        {
            return (desired, inactive);
        }

        // One provider call lets relational, Redis and in-memory implementations use their bulk
        // paths. Renewing a short lease must not wait for one network round trip per checker.
        var states = await _stateProvider
            .GetStatesAsync<PulseCheckerState>(_registered.Keys, cancellationToken)
            .ConfigureAwait(false);

        foreach (var (name, checker) in _registered)
        {
            if (states.TryGetValue(name, out var state))
            {
                if (state.IsActive)
                {
                    desired[name] = (checker, state.EffectiveSchedule);
                }
                else
                {
                    inactive[name] = (checker, state.EffectiveSchedule);
                }
            }
            else if (_requested.TryGetValue(name, out var initial))
            {
                // A checker with no stored state uses its code defaults. PulsesScheduler has already
                // resolved those into the schedule requested here, without another provider read.
                desired[name] = initial;
            }
        }

        return (desired, inactive);
    }

    /// <summary>
    /// Runs remote scheduler mutations with enough concurrency for a large takeover to stay inside
    /// its lease, without creating one simultaneously active request per checker.
    /// </summary>
    private static async Task<IReadOnlyList<(T Item, Exception? Error)>> RunBoundedAsync<T>(
        IEnumerable<T> items,
        Func<T, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        var results = new ConcurrentQueue<(T Item, Exception? Error)>();
        try
        {
            await Parallel.ForEachAsync(
                items,
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = MaxReconciliationConcurrency,
                },
                async (item, iterationToken) =>
                {
                    try
                    {
                        await operation(item, iterationToken).ConfigureAwait(false);
                        results.Enqueue((item, null));
                    }
                    catch (OperationCanceledException ex) when (iterationToken.IsCancellationRequested)
                    {
                        // Preserve completed siblings for the caller before cancellation escapes.
                        results.Enqueue((item, ex));
                        throw;
                    }
                    catch (Exception ex)
                    {
                        results.Enqueue((item, ex));
                    }
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Successful remote mutations are still real. Return them so the caller updates its
            // tracking before ThrowFirstFailure rethrows the captured cancellation.
        }

        return results.ToList();
    }

    private static void ThrowFirstFailure<T>(IEnumerable<(T Item, Exception? Error)> results)
    {
        var failure = results.FirstOrDefault(result => result.Error is not null).Error;
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
