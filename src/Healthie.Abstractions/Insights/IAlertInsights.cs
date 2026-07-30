namespace Healthie.Abstractions.Insights;

/// <summary>
/// The alerts that have been raised, and whether they are being delivered.
/// </summary>
/// <remarks>
/// Declared here rather than in the alerting package, so the dashboard can render the panel without
/// referencing it -- see <see cref="IUptimeInsights"/> for why that matters. Reads only.
/// </remarks>
public interface IAlertInsights
{
    /// <summary>
    /// One page of alerts, newest first.
    /// </summary>
    /// <param name="skip">How many of the newest to pass over.</param>
    /// <param name="take">How many to return.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The page, and how many there are in total.</returns>
    /// <remarks>
    /// Paged rather than capped at a handful because the history outlives the process: an operator
    /// arriving after a restart is looking for what happened before it, which is exactly the part a
    /// "last twenty" list throws away.
    /// </remarks>
    Task<AlertPage> GetAlertsAsync(
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many alerts were dropped because the queue was full.
    /// </summary>
    /// <remarks>
    /// Worth showing rather than only logging: a dropped alert is one nobody was told about, and the
    /// board is where somebody would look to find out that alerting itself is behind.
    /// </remarks>
    int DroppedCount { get; }

    /// <summary>
    /// Where alerts are being delivered, and how that is going.
    /// </summary>
    /// <remarks>
    /// Empty when nothing is registered to deliver to, which is the case worth surfacing: an
    /// application that installed alerting and never configured a sink raises alerts that reach
    /// nobody, and a board showing a healthy list of alerts looks exactly like one that is notifying
    /// people. Startup logs it once; this is what puts it where somebody is looking.
    /// </remarks>
    IReadOnlyList<AlertSinkStatus> Sinks { get; }
}
