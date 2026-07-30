using Healthie.Abstractions.Insights;

namespace Healthie.Alerting;

/// <summary>
/// The last few alerts, kept so the dashboard can show them.
/// </summary>
/// <remarks>
/// <para>
/// Alerting is fire-and-forget by design: an alert goes to its sinks and is gone. That is right for
/// delivery and wrong for the one screen an operator looks at, where "has anything fired recently,
/// and did it get through" is the first question. This keeps just enough to answer it.
/// </para>
/// <para>
/// Bounded and in memory on purpose. It is a window onto what just happened, not a record -- the
/// record is wherever the sinks deliver to. Nothing here is worth a round trip to a database or
/// worth surviving a restart.
/// </para>
/// </remarks>
/// <param name="capacity">How many alerts to keep.</param>
public sealed class AlertHistory(int capacity) : IAlertInsights
{
    private readonly Queue<AlertInsight> _recent = new(capacity);
    // A plain object, not System.Threading.Lock: this package targets net8.0 as well.
    private readonly object _gate = new();

    private int _dropped;

    /// <inheritdoc />
    public int DroppedCount => Volatile.Read(ref _dropped);

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

        lock (_gate)
        {
            // Trims after enqueuing rather than before. Dropping the oldest first has to special-case
            // an empty queue, and a capacity of zero makes every call the empty case.
            _recent.Enqueue(insight);

            while (_recent.Count > capacity)
            {
                _recent.Dequeue();
            }
        }
    }

    /// <summary>Records that an alert never reached the queue.</summary>
    public void RecordDropped() => Interlocked.Increment(ref _dropped);

    /// <inheritdoc />
    public Task<IReadOnlyList<AlertInsight>> GetRecentAlertsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<AlertInsight> newestFirst =
                [.. _recent.Reverse().Take(Math.Max(limit, 0))];

            return Task.FromResult(newestFirst);
        }
    }
}
