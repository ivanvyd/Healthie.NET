using Cronos;
using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Scheduling;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Healthie.Scheduling.Coravel;

/// <summary>
/// An <see cref="IPulseScheduler"/> that runs pulse checks on Coravel's scheduler.
/// </summary>
/// <remarks>
/// <para>
/// Coravel's scheduler is configured once, at startup, and has no API for removing a scheduled job
/// -- verified against Coravel 6.0.2, whose <c>IScheduler</c> exposes only <c>Schedule</c> methods.
/// <see cref="IPulseScheduler"/> requires the opposite: checkers are scheduled, rescheduled and
/// unscheduled at runtime, from the dashboard and the REST API.
/// </para>
/// <para>
/// So this does not register a Coravel job per checker. It registers one job that runs every second
/// and asks this scheduler which checkers are due, and the due times live here. That is an honest
/// description of what you get: Coravel supplies the tick and its host lifetime, and Healthie
/// decides what runs. If you are not already using Coravel, the built-in timer scheduler does the
/// same thing without the dependency.
/// </para>
/// </remarks>
public sealed class CoravelPulseScheduler(
    ILogger<CoravelPulseScheduler>? logger = null,
    TimeProvider? timeProvider = null) : IPulseScheduler
{
    /// <summary>
    /// Where "now" comes from.
    /// </summary>
    /// <remarks>
    /// Optional, so adding it does not change how this is constructed, and
    /// <see cref="TimeProvider.System"/> is the wall clock this always used. A test can supply its
    /// own instead of sleeping: due times are the whole of this class's behaviour, and a test that
    /// waits for real milliseconds to pass is testing the machine's load as much as the scheduler.
    /// </remarks>
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    private sealed record Entry(IPulseChecker Checker, PulseSchedule Schedule, CronExpression? Cron, DateTime DueAt);

    private readonly ConcurrentDictionary<string, Entry> _scheduled = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task ScheduleAsync(
        IPulseChecker checker,
        PulseInterval interval,
        CancellationToken cancellationToken = default) =>
        ScheduleAsync(checker, PulseSchedule.FromInterval(interval), cancellationToken);

    /// <inheritdoc />
    /// <exception cref="ArgumentException">The cron expression could not be parsed.</exception>
    public Task ScheduleAsync(
        IPulseChecker checker,
        PulseSchedule schedule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(schedule);

        // Parsed before anything is replaced, so a malformed expression cannot stop a checker that
        // is already running on a good one.
        var cron = schedule.IsCron ? ParseCron(schedule.CronExpression!, checker.Name) : null;

        var due = NextDueAt(schedule, cron, _time.GetUtcNow().UtcDateTime);

        if (due is null)
        {
            logger?.LogWarning(
                "Cron schedule for '{CheckerName}' has no further occurrences; it will not run.",
                checker.Name);

            _scheduled.TryRemove(checker.Name, out _);
            return Task.CompletedTask;
        }

        _scheduled[checker.Name] = new Entry(checker, schedule, cron, due.Value);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnscheduleAsync(IPulseChecker checker, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checker);

        _scheduled.TryRemove(checker.Name, out _);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs every checker that is due, and works out when each is next.
    /// </summary>
    /// <remarks>
    /// Called from the single Coravel job this package registers. Checkers run one after another
    /// rather than in parallel: a tick that fanned out would let a slow check delay the next tick
    /// for every other checker, and each checker already refuses to overlap itself.
    /// </remarks>
    internal async Task TickAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        foreach (var entry in _scheduled.Values)
        {
            if (entry.DueAt > now || cancellationToken.IsCancellationRequested)
            {
                continue;
            }

            var next = NextDueAt(entry.Schedule, entry.Cron, now);

            if (next is null)
            {
                _scheduled.TryRemove(entry.Checker.Name, out _);
            }
            else
            {
                // Replaced before triggering, so a check that outlives the tick is not started again
                // by the next one.
                _scheduled.TryUpdate(entry.Checker.Name, entry with { DueAt = next.Value }, entry);
            }

            try
            {
                await entry.Checker.TriggerAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One checker throwing must not stop the rest of this tick.
                logger?.LogError(ex, "Error triggering pulse checker '{CheckerName}'.", entry.Checker.Name);
            }
        }
    }

    /// <summary>When a schedule next comes round after a moment, or <c>null</c> if it never does.</summary>
    private static DateTime? NextDueAt(PulseSchedule schedule, CronExpression? cron, DateTime after) =>
        cron is not null ? cron.GetNextOccurrence(after) : after + schedule.Period!.Value;

    /// <summary>Parses a standard Unix cron expression, in five fields or six with leading seconds.</summary>
    private static CronExpression ParseCron(string expression, string checkerName)
    {
        var format = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 6
            ? CronFormat.IncludeSeconds
            : CronFormat.Standard;

        try
        {
            return CronExpression.Parse(expression, format);
        }
        catch (CronFormatException ex)
        {
            throw new ArgumentException(
                $"Cron expression '{expression}' for pulse checker '{checkerName}' could not be parsed. " +
                "Expected standard Unix cron: five fields, or six with a leading seconds field.",
                nameof(expression),
                ex);
        }
    }
}
