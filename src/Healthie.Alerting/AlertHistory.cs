using Healthie.Abstractions.Insights;
using Healthie.Abstractions.StateProviding;
using Microsoft.Extensions.Logging;

namespace Healthie.Alerting;

/// <summary>
/// The alerts that have been raised, kept so the dashboard can show them.
/// </summary>
/// <remarks>
/// <para>
/// Alerting is fire-and-forget by design: an alert goes to its sinks and is gone. That is right for
/// delivery and wrong for the one screen an operator looks at, where "what fired, and did it get
/// through" is the first question -- and it is asked most often just after a restart, about what
/// happened before it.
/// </para>
/// <para>
/// So the log is written through the application's own <see cref="IStateProvider"/>: a deployment on
/// CosmosDB, Postgres or Redis keeps its alert history across a redeploy, and one left on the
/// in-memory provider does not. There is no second storage contract to configure, and no provider
/// had to learn about alerts.
/// </para>
/// <para>
/// Bounded, and written whole on each alert. Both are affordable because alerts are transitions
/// rather than checks -- a checker running every second raises nothing until its health changes -- and
/// both are deliberate: an unbounded log in a state document would grow without limit, and the record
/// of record is wherever the sinks deliver to.
/// </para>
/// </remarks>
/// <param name="capacity">How many alerts to keep before the oldest is discarded.</param>
/// <param name="stateProvider">Where to persist the log, or <c>null</c> to keep it in memory only.</param>
/// <param name="logger">An optional logger for diagnostic output.</param>
public sealed class AlertHistory(
    int capacity,
    IStateProvider? stateProvider = null,
    ILogger<AlertHistory>? logger = null) : IAlertInsights
{
    /// <summary>The key the whole log is stored under.</summary>
    /// <remarks>
    /// Deliberately not a checker's name, and prefixed so it cannot collide with one: a state
    /// provider is keyed by checker name, and this is the one entry that is not a checker.
    /// </remarks>
    private const string StorageKey = "healthie.alerts.log";

    private readonly Queue<AlertInsight> _recent = new(capacity);
    // A plain object, not System.Threading.Lock: this package targets net8.0 as well.
    private readonly object _gate = new();

    private bool _loaded;

    private readonly Dictionary<string, SinkTally> _sinks = [];

    private int _dropped;

    /// <inheritdoc />
    public int DroppedCount => Volatile.Read(ref _dropped);

    /// <inheritdoc />
    public IReadOnlyList<AlertSinkStatus> Sinks
    {
        get
        {
            lock (_gate)
            {
                return [.. _sinks.Select(entry => new AlertSinkStatus(
                    entry.Key, entry.Value.Delivered, entry.Value.Failed, entry.Value.LastError))];
            }
        }
    }

    /// <summary>Registers a sink so it appears on the board before it has done anything.</summary>
    /// <param name="name">The sink's type name.</param>
    /// <remarks>
    /// Named at startup rather than discovered on first delivery, because "no sinks configured" and
    /// "sinks configured, nothing has alerted yet" look identical otherwise and mean opposite things.
    /// </remarks>
    public void Register(string name)
    {
        lock (_gate)
        {
            _sinks.TryAdd(name, new SinkTally());
        }
    }

    /// <summary>Records the outcome of one sink's attempt at one alert.</summary>
    /// <param name="name">The sink's type name.</param>
    /// <param name="error">The failure, or <c>null</c> when it was accepted.</param>
    public void RecordDelivery(string name, string? error)
    {
        lock (_gate)
        {
            if (!_sinks.TryGetValue(name, out var tally))
            {
                tally = new SinkTally();
                _sinks[name] = tally;
            }

            if (error is null)
            {
                tally.Delivered++;

                // Cleared on success, so a sink that failed once and recovered stops being shown as
                // broken -- what matters is whether it is working now.
                tally.LastError = null;
            }
            else
            {
                tally.Failed++;
                tally.LastError = error;
            }
        }
    }

    private sealed class SinkTally
    {
        public int Delivered { get; set; }

        public int Failed { get; set; }

        public string? LastError { get; set; }
    }

    /// <summary>Records an alert and whether every sink took it.</summary>
    /// <param name="alert">The alert that was raised.</param>
    /// <param name="delivered">Whether every sink accepted it.</param>
    public void Record(Alert alert, bool delivered)
    {
        var insight = new AlertInsight(
            alert.CheckerName,
            alert.DisplayName,
            alert.PreviousHealth,
            alert.CurrentHealth,
            alert.Message,
            alert.OccurredAt,
            delivered);

        List<AlertInsight> toPersist;

        lock (_gate)
        {
            // Trims after enqueuing rather than before. Dropping the oldest first has to special-case
            // an empty queue, and a capacity of zero makes every call the empty case.
            _recent.Enqueue(insight);

            while (_recent.Count > capacity)
            {
                _recent.Dequeue();
            }

            toPersist = [.. _recent];
        }

        // Outside the lock: this is a round trip to the state store, and holding a lock across it
        // would stall every reader of the board for the duration of a database write.
        _ = PersistAsync(toPersist);
    }

    /// <summary>Records that an alert never reached the queue.</summary>
    public void RecordDropped() => Interlocked.Increment(ref _dropped);

    /// <inheritdoc />
    public async Task<AlertPage> GetAlertsAsync(
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            var newestFirst = _recent.Reverse().ToList();

            IReadOnlyList<AlertInsight> page =
                [.. newestFirst.Skip(Math.Max(skip, 0)).Take(Math.Max(take, 0))];

            return new AlertPage(page, newestFirst.Count, StoreName, capacity);
        }
    }

    /// <summary>What the board calls the place this history is kept.</summary>
    private string StoreName => stateProvider?.GetType().Name ?? "memory";

    /// <summary>
    /// Reads the stored log once, the first time anything asks for a page.
    /// </summary>
    /// <remarks>
    /// Lazily rather than at startup: the dispatcher subscribes while the host is still starting, and
    /// a state provider may not have finished initializing its container or table by then. Nothing
    /// needs the history until somebody opens the board.
    /// </remarks>
    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded || stateProvider is null)
        {
            return;
        }

        // Set before the read, not after: a failed read must not leave every later page request
        // retrying a store that is not answering.
        _loaded = true;

        try
        {
            var stored = await stateProvider
                .GetStateAsync<List<AlertInsight>>(StorageKey, cancellationToken)
                .ConfigureAwait(false);

            if (stored is null or { Count: 0 })
            {
                return;
            }

            lock (_gate)
            {
                // In front of anything raised while this was loading, then trimmed: the stored log is
                // older by definition, and a restart that raises an alert immediately must not lose it.
                var live = _recent.ToList();
                _recent.Clear();

                foreach (var insight in stored.Concat(live).TakeLast(capacity))
                {
                    _recent.Enqueue(insight);
                }
            }
        }
        catch (Exception ex)
        {
            // A history that cannot be read is not a reason to fail the board: it shows what this
            // process has seen, and says where the rest was meant to be.
            logger?.LogWarning(ex, "Could not read the stored alert history from the state provider.");
        }
    }

    private async Task PersistAsync(List<AlertInsight> log)
    {
        if (stateProvider is null)
        {
            return;
        }

        try
        {
            await stateProvider.SetStateAsync(StorageKey, log).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Never propagated. This runs on the alert-delivery path, and a state store that is down
            // must not take alerting down with it -- the alert has already reached its sinks.
            logger?.LogWarning(ex, "Could not persist the alert history to the state provider.");
        }
    }
}
