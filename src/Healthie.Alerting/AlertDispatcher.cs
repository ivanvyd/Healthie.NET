using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Healthie.Alerting;

/// <summary>
/// Watches every registered checker and hands each health change to the configured sinks.
/// </summary>
/// <remarks>
/// <para>
/// A subscriber rather than a seam inside <c>PulseChecker</c>, which is what makes the safety
/// properties fall out instead of needing to be maintained. <c>StateChanged</c> is a synchronous
/// event raised on whichever thread ran the check, so anything doing real work in the handler runs
/// inside the check's own semaphore. Here the handler only writes to a bounded channel and returns;
/// every sink call happens on this service's own loop, where it cannot delay a check, cannot hold a
/// semaphore, and cannot make a healthy component look unhealthy however badly it fails.
/// </para>
/// <para>
/// Only a change of <em>health</em> raises an alert. State equality includes the last execution
/// time, so <c>StateChanged</c> fires on every single check -- subscribing to it naively would
/// alert on every tick.
/// </para>
/// </remarks>
public sealed class AlertDispatcher : BackgroundService
{
    private readonly IReadOnlyList<IPulseChecker> _checkers;
    private readonly IReadOnlyList<IAlertSink> _sinks;
    private readonly HealthieAlertOptions _options;
    private readonly ILogger<AlertDispatcher>? _logger;

    private readonly Channel<Alert> _queue;
    private readonly ConcurrentDictionary<string, DateTime> _lastAlertedAt = new(StringComparer.Ordinal);
    private readonly List<(IPulseChecker Checker, EventHandler<PulseCheckerStateChangedEventArgs> Handler)> _subscriptions = [];

    private long _dropped;

    /// <summary>Initializes a new instance of the <see cref="AlertDispatcher"/> class.</summary>
    /// <param name="checkers">Every registered pulse checker.</param>
    /// <param name="sinks">Every registered alert sink.</param>
    /// <param name="options">Which changes alert, and how hard to try.</param>
    /// <param name="logger">An optional logger for diagnostic output.</param>
    public AlertDispatcher(
        IEnumerable<IPulseChecker> checkers,
        IEnumerable<IAlertSink> sinks,
        HealthieAlertOptions options,
        ILogger<AlertDispatcher>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(checkers);
        ArgumentNullException.ThrowIfNull(sinks);

        _checkers = [.. checkers];
        _sinks = [.. sinks];
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;

        _queue = Channel.CreateBounded<Alert>(
            new BoundedChannelOptions(_options.QueueCapacity)
            {
                // Drop rather than wait. Waiting here would mean waiting on the check thread, which
                // is the one thing this design exists to avoid.
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
            },
            // Counted here rather than from TryWrite's result: with DropWrite, TryWrite returns
            // true whether the item was queued or thrown away, so a counter driven off it would
            // report that nothing was ever dropped.
            OnAlertDropped);
    }

    /// <summary>Alerts discarded because the queue was full since the process started.</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    /// <inheritdoc />
    /// <remarks>
    /// Subscribes here rather than in <see cref="ExecuteAsync"/>. A hosted service's background loop
    /// is started, not awaited, so it may not have run by the time the host considers startup
    /// finished -- and a check firing in that gap would raise its event into nothing. Subscribing
    /// while the host is still starting closes the window.
    /// </remarks>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Nothing to deliver to, so there is no reason to subscribe or to hold a queue.
        if (_sinks.Count == 0)
        {
            _logger?.LogInformation("Alerting is registered but no sink is; no alerts will be sent.");
            return Task.CompletedTask;
        }

        Subscribe();

        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var alert in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await DeliverAsync(alert, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        finally
        {
            Unsubscribe();
        }
    }

    private void Subscribe()
    {
        foreach (var checker in _checkers)
        {
            EventHandler<PulseCheckerStateChangedEventArgs> handler = (_, args) => OnStateChanged(checker, args);
            checker.StateChanged += handler;
            _subscriptions.Add((checker, handler));
        }
    }

    private void Unsubscribe()
    {
        foreach (var (checker, handler) in _subscriptions)
        {
            checker.StateChanged -= handler;
        }

        _subscriptions.Clear();
    }

    /// <summary>
    /// Runs on the check's thread, inside its semaphore, so it does as little as possible.
    /// </summary>
    private void OnStateChanged(IPulseChecker checker, PulseCheckerStateChangedEventArgs args)
    {
        var previous = args.OldState.LastResult?.Health;
        var current = args.NewState.LastResult?.Health;

        if (current is not { } health || previous == health || !ShouldAlert(previous, health))
        {
            return;
        }

        if (IsWithinDeduplicationWindow(checker.Name))
        {
            return;
        }

        var alert = new Alert(
            checker.Name,
            checker.DisplayName,
            args.NewState.Group,
            args.NewState.Tags,
            previous,
            health,
            args.NewState.LastResult?.Message ?? string.Empty,
            DateTime.UtcNow);

        _queue.Writer.TryWrite(alert);
    }

    /// <summary>
    /// Called by the channel when an alert could not be queued.
    /// </summary>
    private void OnAlertDropped(Alert alert)
    {
        var dropped = Interlocked.Increment(ref _dropped);

        _logger?.LogWarning(
            "Alert queue is full; dropped the alert for '{CheckerName}' ({DroppedCount} dropped so far).",
            alert.CheckerName,
            dropped);
    }

    private bool ShouldAlert(PulseCheckerHealth? previous, PulseCheckerHealth current)
    {
        if (current == PulseCheckerHealth.Healthy)
        {
            // A checker's very first result being healthy is not a recovery from anything.
            return _options.SendRecoveries && previous is not null;
        }

        return current >= _options.MinimumSeverity;
    }

    /// <summary>
    /// Records this checker as alerted and says whether it was alerted too recently to alert again.
    /// </summary>
    private bool IsWithinDeduplicationWindow(string checkerName)
    {
        var now = DateTime.UtcNow;
        var suppressed = false;

        _lastAlertedAt.AddOrUpdate(
            checkerName,
            now,
            (_, previous) =>
            {
                if (now - previous < _options.DeduplicationWindow)
                {
                    suppressed = true;
                    return previous;
                }

                return now;
            });

        return suppressed;
    }

    /// <summary>
    /// Offers one alert to every sink, and lets none of them stop the others.
    /// </summary>
    /// <remarks>
    /// Each sink gets its own timeout and its own try. A sink that throws is logged and skipped; a
    /// sink that hangs is abandoned at the timeout. Neither can reach the checks, because by this
    /// point the check that produced the alert finished long ago.
    /// </remarks>
    private async Task DeliverAsync(Alert alert, CancellationToken stoppingToken)
    {
        foreach (var sink in _sinks)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(_options.DeliveryTimeout);

            try
            {
                await sink.SendAsync(alert, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                _logger?.LogWarning(
                    "Alert sink {Sink} did not deliver the alert for '{CheckerName}' within {Timeout}.",
                    sink.GetType().Name,
                    alert.CheckerName,
                    _options.DeliveryTimeout);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    ex,
                    "Alert sink {Sink} failed to deliver the alert for '{CheckerName}'.",
                    sink.GetType().Name,
                    alert.CheckerName);
            }
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        Unsubscribe();
        base.Dispose();
    }
}
