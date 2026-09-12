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
/// Bounded, and persisted as whole-log snapshots. This is affordable because alerts are transitions
/// rather than checks -- a checker running every second raises nothing until its health changes --
/// and changes during a write are coalesced into one latest-state follow-up. An unbounded log in a
/// state document would grow without limit, and the record of record is wherever the sinks deliver to.
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

    private bool _persistenceRequested;

    private bool _persistenceRunning;

    private Task<bool>? _loadTask;

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

        var startPersistence = false;

        lock (_gate)
        {
            // Trims after enqueuing rather than before. Dropping the oldest first has to special-case
            // an empty queue, and a capacity of zero makes every call the empty case.
            _recent.Enqueue(insight);

            while (_recent.Count > capacity)
            {
                _recent.Dequeue();
            }

            if (stateProvider is not null)
            {
                _persistenceRequested = true;
                if (!_persistenceRunning)
                {
                    _persistenceRunning = true;
                    startPersistence = true;
                }
            }
        }

        // Outside the lock: a round trip to the state store held under it would stall every reader of
        // the board for the duration of a database write.
        if (startPersistence)
        {
            _ = PersistLoopAsync();
        }
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
    /// Loads the stored log before the first page read or persistence snapshot.
    /// </summary>
    /// <remarks>
    /// Lazily rather than at startup: the dispatcher subscribes while the host is still starting, and
    /// a state provider may not have finished initializing its container or table by then. Page reads
    /// and persistence share the same load so a first alert after restart cannot replace older data.
    /// </remarks>
    private async Task<bool> EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (stateProvider is null)
        {
            return true;
        }

        Task<bool> loadTask;

        lock (_gate)
        {
            // Page reads and the first persistence pass share one load. A first alert after restart
            // must not write its live-only snapshot over the durable history before that load ends.
            loadTask = _loadTask ??= LoadStoredAsync();
        }

        var loaded = await loadTask.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (!loaded)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_loadTask, loadTask))
                {
                    _loadTask = null;
                }
            }
        }

        return loaded;
    }

    private async Task<bool> LoadStoredAsync()
    {
        try
        {
            var stored = await stateProvider!
                .GetStateAsync<List<AlertInsight>>(StorageKey)
                .ConfigureAwait(false);

            if (stored is null or { Count: 0 })
            {
                return true;
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

            return true;
        }
        catch (Exception ex)
        {
            // A history that cannot be read is not a reason to fail the board: it shows what this
            // process has seen, and says where the rest was meant to be.
            logger?.LogWarning(ex, "Could not read the stored alert history from the state provider.");
            return false;
        }
    }

    /// <summary>
    /// Writes the whole log and coalesces changes raised while a write is in progress.
    /// </summary>
    /// <remarks>
    /// At most one loop runs. Alerts raised during a state-provider round trip set one pending flag,
    /// so the loop follows the current write with one snapshot of the latest history instead of
    /// allocating a waiting task and another full-log write for every alert in a burst.
    /// </remarks>
    private async Task PersistLoopAsync()
    {
        while (true)
        {
            lock (_gate)
            {
                if (!_persistenceRequested)
                {
                    _persistenceRunning = false;
                    return;
                }

                _persistenceRequested = false;
            }

            // A failed read must not be followed by a write that replaces unknown durable history.
            // A later alert requests another attempt; callers can still read this process's log.
            if (!await EnsureLoadedAsync(CancellationToken.None).ConfigureAwait(false))
            {
                continue;
            }

            List<AlertInsight> log;

            lock (_gate)
            {
                // The snapshot includes alerts recorded during the initial load, so their pending
                // flag is already satisfied by this write.
                _persistenceRequested = false;
                log = [.. _recent];
            }

            try
            {
                await stateProvider!.SetStateAsync(StorageKey, log).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Never propagated. This runs on the alert-delivery path, and a state store that is
                // down must not take alerting down with it -- the alert has already reached its sinks.
                logger?.LogWarning(ex, "Could not persist the alert history to the state provider.");
            }
        }
    }
}
