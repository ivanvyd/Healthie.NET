using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Extensions;
using Healthie.Abstractions.Scheduling;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Healthie.DependencyInjection;

/// <summary>
/// A built-in, zero-dependency pulse scheduler that uses
/// <see cref="PeriodicTimer"/> for scheduling pulse checks.
/// Suitable for development and simple production scenarios.
/// For advanced scheduling (persistent jobs, CRON expressions, clustering),
/// use a dedicated scheduling provider like Healthie.NET.Quartz.
/// </summary>
public sealed class TimerPulseScheduler : IPulseScheduler, IAsyncDisposable
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
    public async Task ScheduleAsync(
        IPulseChecker checker,
        PulseInterval interval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checker);

        // Cancel any existing schedule for this checker
        await UnscheduleAsync(checker, cancellationToken).ConfigureAwait(false);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _timers[checker.Name] = cts;

        var timeSpan = interval.ToTimeSpan();

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(timeSpan);

            try
            {
                while (await timer.WaitForNextTickAsync(cts.Token).ConfigureAwait(false))
                {
                    try
                    {
                        await checker.TriggerAsync(cts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger?.LogError(ex,
                            "Error triggering pulse checker '{CheckerName}'.",
                            checker.Name);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when unscheduling or shutting down
            }
        }, cts.Token);
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
    /// Disposes all active timers and cancellation token sources.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        foreach (var kvp in _timers)
        {
            kvp.Value.Cancel();
            kvp.Value.Dispose();
        }

        _timers.Clear();
        return ValueTask.CompletedTask;
    }
}
