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
    /// The most recent alerts, newest first.
    /// </summary>
    /// <param name="limit">How many to return.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The alerts, newest first.</returns>
    Task<IReadOnlyList<AlertInsight>> GetRecentAlertsAsync(
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many alerts were dropped because the queue was full.
    /// </summary>
    /// <remarks>
    /// Worth showing rather than only logging: a dropped alert is one nobody was told about, and the
    /// board is where somebody would look to find out that alerting itself is behind.
    /// </remarks>
    int DroppedCount { get; }
}
