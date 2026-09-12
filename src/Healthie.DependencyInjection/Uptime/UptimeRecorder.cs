using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Healthie.Uptime;

/// <summary>
/// Watches every registered checker and records its first fresh result and each health change as a
/// segment.
/// </summary>
/// <remarks>
/// Subscribes rather than sitting inside the check, for the same reason alerting does: a store that
/// is slow, remote or briefly unavailable must not delay a check or hold its semaphore, and must
/// never make a healthy component look unhealthy. The handler writes to a bounded channel and
/// returns; the store is only ever touched from this service's own loop.
/// </remarks>
public sealed class UptimeRecorder : BackgroundService
{
    private readonly IReadOnlyList<IPulseChecker> _checkers;
    private readonly IUptimeStore _store;
    private readonly ILogger<UptimeRecorder>? _logger;

    private readonly Channel<UptimeSegment> _queue;
    private readonly List<(IPulseChecker Checker, EventHandler<PulseCheckerStateChangedEventArgs> Handler)> _subscriptions = [];
    private readonly Dictionary<string, PulseCheckerHealth> _recordedHealth = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UptimeSegment> _pending = new(StringComparer.Ordinal);
    private readonly object _observationLock = new();

    private long _dropped;

    /// <summary>Initializes a new instance of the <see cref="UptimeRecorder"/> class.</summary>
    /// <param name="checkers">Every registered pulse checker.</param>
    /// <param name="store">Where segments are kept.</param>
    /// <param name="logger">An optional logger for diagnostic output.</param>
    public UptimeRecorder(
        IEnumerable<IPulseChecker> checkers,
        IUptimeStore store,
        ILogger<UptimeRecorder>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(checkers);

        _checkers = [.. checkers];
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger;

        _queue = Channel.CreateBounded<UptimeSegment>(
            new BoundedChannelOptions(1024)
            {
                // The event handler still uses TryWrite and never waits. Wait mode makes TryWrite
                // report a full queue instead of claiming a DropWrite was accepted, so the same
                // health can be retried by the next fresh check.
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
            });
    }

    /// <summary>Transitions discarded because the queue was full since the process started.</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    /// <inheritdoc />
    /// <remarks>
    /// Subscribes here rather than in <see cref="ExecuteAsync"/>: the background loop is started
    /// rather than awaited, and a transition happening before it ran would be lost.
    /// </remarks>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var checker in _checkers)
        {
            EventHandler<PulseCheckerStateChangedEventArgs> handler = (_, args) => OnStateChanged(checker, args);
            checker.StateChanged += handler;
            _subscriptions.Add((checker, handler));
        }

        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var segment in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await _store
                        .RecordAsync(segment.CheckerName, segment.Health, segment.StartedAt, stoppingToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A store that cannot be written to loses a transition. It must not stop the
                    // recorder, or one failure would end uptime recording for the whole process.
                    _logger?.LogError(
                        ex,
                        "Could not record the uptime transition for '{CheckerName}'.",
                        segment.CheckerName);
                }
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
        if (args.CurrentHealth is not { } health)
        {
            return;
        }

        // The first fresh result observed by this process starts its own segment even when it has
        // the same health as persisted state. This leaves the time while the process was stopped
        // unknown without waiting for a future health transition to resume uptime recording.
        var newExecution = args.NewState.LastExecutionDateTime;
        var isFreshResult = newExecution.HasValue
            && newExecution != args.OldState.LastExecutionDateTime;
        lock (_observationLock)
        {
            if (_recordedHealth.TryGetValue(checker.Name, out var recorded))
            {
                if (recorded == health)
                {
                    // A transition that overflowed the queue was superseded before it could be
                    // retried. Keeping it would later stretch that transient state across the gap.
                    _pending.Remove(checker.Name);
                    return;
                }
            }
            else if (!args.HealthChanged && !isFreshResult)
            {
                return;
            }

            // A check began at LastExecutionDateTime; a slow state write must not shift the outage
            // boundary forward. Administrative health changes such as Reset have no new execution
            // time, so they begin when this process observes the change.
            var startedAt = isFreshResult
                ? newExecution.GetValueOrDefault()
                : DateTime.UtcNow;
            var segment = new UptimeSegment(checker.Name, health, startedAt);

            if (_pending.TryGetValue(checker.Name, out var pending) && pending.Health == health)
            {
                // The queue rejected the transition itself. A later observation can retry it, but
                // must keep the original boundary rather than moving the outage forward in time.
                segment = pending;
            }

            if (_queue.Writer.TryWrite(segment))
            {
                _recordedHealth[checker.Name] = health;
                _pending.Remove(checker.Name);
            }
            else
            {
                _pending[checker.Name] = segment;
                Interlocked.Increment(ref _dropped);
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
