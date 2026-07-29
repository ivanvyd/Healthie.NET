using Healthie.Abstractions;
using Healthie.Abstractions.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Healthie.Uptime;

/// <summary>
/// Watches every registered checker and records each health change as a segment.
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
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
            },
            // TryWrite returns true whether the item was queued or discarded under DropWrite, so
            // the count has to come from the channel rather than from its result.
            _ => Interlocked.Increment(ref _dropped));
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
        // Only a change of health starts a new segment. StateChanged fires on every check, because
        // state equality includes the last execution time, so recording every one of them would
        // turn a day into 86,400 segments that all say the same thing.
        var previous = args.OldState.LastResult?.Health;
        var current = args.NewState.LastResult?.Health;

        if (current is not { } health || previous == health)
        {
            return;
        }

        _queue.Writer.TryWrite(new UptimeSegment(checker.Name, health, DateTime.UtcNow));
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        Unsubscribe();
        base.Dispose();
    }
}
