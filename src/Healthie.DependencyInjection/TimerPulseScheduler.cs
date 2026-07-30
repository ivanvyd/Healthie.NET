using Cronos;
using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Scheduling;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Healthie.DependencyInjection;

/// <summary>
/// A built-in pulse scheduler that uses <see cref="PeriodicTimer"/> for fixed periods and cron
/// expressions for everything a period cannot say. Suitable for development and simple production
/// scenarios. For persistent jobs surviving a restart, or clustering, use a dedicated scheduling
/// provider such as Healthie.NET.Quartz.
/// </summary>
public sealed class TimerPulseScheduler : IPulseScheduler, IAsyncDisposable, IDisposable
{
    /// <summary>The longest this scheduler waits in one go before re-checking the clock.</summary>
    /// <remarks>
    /// Well inside the roughly fifty days <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
    /// accepts, and short enough that a clock change is noticed within a day rather than slept
    /// through to the far side of it.
    /// </remarks>
    private static readonly TimeSpan MaxDelay = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _timers = new();

    /// <summary>
    /// Serialises scheduling, so a checker is never left running under a timer nobody holds.
    /// </summary>
    /// <remarks>
    /// The dictionary is concurrent, but installing a schedule is "stop the old one, then start the
    /// new one" -- two operations, which a second caller can interleave with. A lock is the whole
    /// fix: scheduling happens at startup and when somebody changes an interval, so it is never on
    /// a hot path.
    /// </remarks>
    private readonly SemaphoreSlim _scheduling = new(1, 1);

    /// <summary>Set once the host has stopped this scheduler. Read and written under the lock.</summary>
    private bool _disposed;
    private readonly ILogger<TimerPulseScheduler>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimerPulseScheduler"/> class.
    /// </summary>
    /// <param name="logger">An optional logger for diagnostic output.</param>
    public TimerPulseScheduler(ILogger<TimerPulseScheduler>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task ScheduleAsync(
        IPulseChecker checker,
        PulseInterval interval,
        CancellationToken cancellationToken = default) =>
        ScheduleAsync(checker, PulseSchedule.FromInterval(interval), cancellationToken);

    /// <inheritdoc />
    /// <exception cref="ArgumentException">The cron expression could not be parsed.</exception>
    /// <remarks>
    /// The cron expression is parsed here rather than where the schedule was built, so a malformed
    /// one is refused in front of whoever tried to schedule it, instead of surfacing later from a
    /// timer thread with nothing to attribute it to.
    /// </remarks>
    public async Task ScheduleAsync(
        IPulseChecker checker,
        PulseSchedule schedule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(schedule);

        // Parsed before the existing schedule is cancelled: a checker already running on a good
        // schedule should not be stopped by a request carrying a bad one.
        var cron = schedule.IsCron ? ParseCron(schedule.CronExpression!, checker.Name) : null;

        // Stopping the old schedule and installing the new one is two steps, and two callers
        // scheduling one checker at once -- an interval changed from the dashboard while the
        // scheduler starts it, say -- could each install a timer. Only the last would be in the
        // dictionary; the other kept running with nothing able to reach it, and its linked
        // CancellationTokenSource was never disposed, so its registration on the parent token
        // outlived it too.
        await _scheduling.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // A request can outlive the decision to shut down -- an interval changed from the API
            // while the host stops. Installing a timer now would leave one running that Dispose has
            // already been past, so there would be nothing left to stop it. Quietly rather than by
            // throwing: the host is going away, and that is not the caller's mistake.
            if (_disposed)
            {
                return;
            }

            StopExisting(checker.Name);

            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _timers[checker.Name] = cts;

            _ = Task.Run(
                () => cron is null
                    ? RunPeriodicallyAsync(checker, schedule.Period!.Value, cts.Token)
                    : RunOnCronAsync(checker, cron, cts.Token),
                cts.Token);
        }
        finally
        {
            _scheduling.Release();
        }
    }

    /// <summary>
    /// Cancels and disposes the timer for a checker, if it has one.
    /// </summary>
    /// <remarks>
    /// Takes no lock, so it can be called from inside one. <see cref="UnscheduleAsync"/> is the
    /// same thing with the lock held.
    /// </remarks>
    private void StopExisting(string name)
    {
        if (_timers.TryRemove(name, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task UnscheduleAsync(
        IPulseChecker checker,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checker);

        await _scheduling.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            StopExisting(checker.Name);
        }
        finally
        {
            _scheduling.Release();
        }
    }

    /// <summary>
    /// Runs the checker on a fixed period.
    /// </summary>
    /// <remarks>
    /// <see cref="PeriodicTimer"/> rather than a delay loop: it times ticks from a fixed origin, so
    /// however long a check takes, the one after it is not pushed later by that.
    /// </remarks>
    private async Task RunPeriodicallyAsync(IPulseChecker checker, TimeSpan period, CancellationToken token)
    {
        using var timer = new PeriodicTimer(period);

        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                await TriggerAsync(checker, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when unscheduling or shutting down.
        }
    }

    /// <summary>
    /// Runs the checker at each occurrence of a cron expression.
    /// </summary>
    private async Task RunOnCronAsync(IPulseChecker checker, CronExpression cron, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var next = cron.GetNextOccurrence(DateTime.UtcNow);

                // An expression can describe a time that never comes round again -- 30 February, or
                // a date already past. Nothing will ever run, and rediscovering that on a loop is
                // worse than saying it once.
                if (next is null)
                {
                    _logger?.LogWarning(
                        "Cron schedule for '{CheckerName}' has no further occurrences; it will not run again.",
                        checker.Name);
                    return;
                }

                var delay = next.Value - DateTime.UtcNow;

                // Task.Delay refuses anything past uint.MaxValue milliseconds, a little under 50
                // days, and throws rather than waiting. That is not an OperationCanceledException,
                // so the catch below would not see it, and this loop runs detached -- the checker
                // would stop forever with nothing logged. A yearly certificate-expiry check is
                // exactly the case that reaches it. Waiting in bounded steps and recomputing keeps
                // the wait short enough to be legal, and re-anchors on the clock each time so a
                // system clock change or a DST shift is picked up rather than slept through.
                var wait = BoundedDelay(delay);

                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, token).ConfigureAwait(false);
                }

                // Still short of the occurrence, so go round and recompute rather than firing early.
                if (wait < delay)
                {
                    continue;
                }

                await TriggerAsync(checker, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when unscheduling or shutting down.
        }
    }

    /// <summary>
    /// Caps how long a single wait may be, so it stays inside what <see cref="Task.Delay(TimeSpan, CancellationToken)"/> accepts.
    /// </summary>
    /// <remarks>
    /// Anything longer is waited out in steps. Returning the remainder unchanged when it already
    /// fits keeps the common case exact rather than rounding every wait up to the cap.
    /// </remarks>
    internal static TimeSpan BoundedDelay(TimeSpan remaining) =>
        remaining > MaxDelay ? MaxDelay : remaining;

    /// <summary>
    /// Triggers one check, letting cancellation through and swallowing everything else.
    /// </summary>
    /// <remarks>
    /// A checker that throws must not end its own schedule: the next tick is exactly when a
    /// component that recovered would report itself healthy again.
    /// </remarks>
    private async Task TriggerAsync(IPulseChecker checker, CancellationToken token)
    {
        try
        {
            await checker.TriggerAsync(token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Error triggering pulse checker '{CheckerName}'.", checker.Name);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Cronos is what actually drives the timer, so asking Cronos is the only answer worth giving.
    /// </remarks>
    public bool TryValidateSchedule(PulseSchedule schedule, out string? error)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        error = null;

        if (schedule.CronExpression is not { } expression)
        {
            return true;
        }

        try
        {
            CronExpression.Parse(expression, CronFormatFor(expression));

            return true;
        }
        catch (CronFormatException ex)
        {
            // Cronos names the field and the range it wanted, which is more use than restating the
            // format -- the field this is shown beside already gives an example of one.
            error = ex.Message;

            return false;
        }
    }

    /// <summary>Six fields or more means the leading one is seconds.</summary>
    private static CronFormat CronFormatFor(string expression) =>
        expression.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 6
            ? CronFormat.IncludeSeconds
            : CronFormat.Standard;

    /// <summary>
    /// Parses a standard Unix cron expression, in five fields or six with a leading seconds field.
    /// </summary>
    private static CronExpression ParseCron(string expression, string checkerName)
    {
        try
        {
            return CronExpression.Parse(expression, CronFormatFor(expression));
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

    /// <summary>
    /// Disposes all active timers and cancellation token sources.
    /// </summary>
    /// <remarks>
    /// <see cref="IDisposable"/> is implemented alongside <see cref="IAsyncDisposable"/> because
    /// this scheduler is registered as a singleton and disposal is synchronous work. Containers
    /// disposed synchronously reject services that are only asynchronously disposable.
    /// </remarks>
    public void Dispose()
    {
        // Shutdown races the last requests: an interval can be changed from the API while the host
        // is stopping. Without the lock this is a third writer of _timers, and clearing the
        // dictionary between an in-flight schedule's stop and its install drops a live timer without
        // cancelling it -- the leak the lock exists to prevent, arriving by another door.
        var acquired = _scheduling.Wait(TimeSpan.FromSeconds(5));

        try
        {
            foreach (var kvp in _timers)
            {
                kvp.Value.Cancel();
                kvp.Value.Dispose();
            }

            _timers.Clear();
            _disposed = true;
        }
        finally
        {
            if (acquired)
            {
                _scheduling.Release();
            }
        }

        // The semaphore is deliberately not disposed. SemaphoreSlim only needs it once its
        // AvailableWaitHandle has been asked for, which nothing here does, and disposing it while
        // another thread sits in WaitAsync leaves that thread waiting for ever rather than throwing
        // -- a hung shutdown instead of a noisy one.
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();

        return ValueTask.CompletedTask;
    }
}
