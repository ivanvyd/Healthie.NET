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
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _timers = new();
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

        await UnscheduleAsync(checker, cancellationToken).ConfigureAwait(false);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _timers[checker.Name] = cts;

        _ = Task.Run(
            () => cron is null
                ? RunPeriodicallyAsync(checker, schedule.Period!.Value, cts.Token)
                : RunOnCronAsync(checker, cron, cts.Token),
            cts.Token);
    }

    /// <inheritdoc />
    public Task UnscheduleAsync(
        IPulseChecker checker,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checker);

        if (_timers.TryRemove(checker.Name, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        return Task.CompletedTask;
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
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, token).ConfigureAwait(false);
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

    /// <summary>
    /// Parses a standard Unix cron expression, in five fields or six with a leading seconds field.
    /// </summary>
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
        foreach (var kvp in _timers)
        {
            kvp.Value.Cancel();
            kvp.Value.Dispose();
        }

        _timers.Clear();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();

        return ValueTask.CompletedTask;
    }
}
