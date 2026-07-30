using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Scheduling;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

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
/// <param name="inner">The scheduler that does the real work when this replica leads.</param>
/// <param name="logger">An optional logger for diagnostic output.</param>
public sealed class LeaderElectedPulseScheduler(
    IPulseScheduler inner,
    ILogger<LeaderElectedPulseScheduler>? logger = null) : IPulseScheduler
{
    private readonly IPulseScheduler _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly ConcurrentDictionary<string, (IPulseChecker Checker, PulseSchedule Schedule)> _requested = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _isLeader;

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

        // Remembered whether or not this replica leads, so that a checker scheduled while following
        // starts running the moment leadership is taken -- rather than only after something happens
        // to schedule it again.
        _requested[checker.Name] = (checker, schedule);

        if (_isLeader)
        {
            await _inner.ScheduleAsync(checker, schedule, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task UnscheduleAsync(IPulseChecker checker, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checker);

        _requested.TryRemove(checker.Name, out _);

        // Forwarded even while following: the inner scheduler may still hold it from a period when
        // this replica led, and an unschedule that only took effect on the leader would leave a
        // checker running on a node that had since been demoted.
        await _inner.UnscheduleAsync(checker, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Takes over the checks, scheduling everything that has been requested.
    /// </summary>
    internal async Task BecomeLeaderAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_isLeader)
            {
                return;
            }

            _isLeader = true;

            foreach (var (checker, schedule) in _requested.Values)
            {
                await _inner.ScheduleAsync(checker, schedule, cancellationToken).ConfigureAwait(false);
            }

            logger?.LogInformation("Took leadership; now running {CheckerCount} pulse checkers.", _requested.Count);
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
            if (!_isLeader)
            {
                return;
            }

            _isLeader = false;

            foreach (var (checker, _) in _requested.Values)
            {
                await _inner.UnscheduleAsync(checker, cancellationToken).ConfigureAwait(false);
            }

            logger?.LogInformation("Stood down; another replica is running the pulse checkers.");
        }
        finally
        {
            _gate.Release();
        }
    }
}
