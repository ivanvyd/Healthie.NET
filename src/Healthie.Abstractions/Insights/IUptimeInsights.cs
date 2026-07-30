namespace Healthie.Abstractions.Insights;

/// <summary>
/// How much of a window a checker spent healthy, for the dashboard to show.
/// </summary>
/// <remarks>
/// <para>
/// Declared here rather than in the package that implements it, so the dashboard can render the
/// panel without referencing that package. The dashboard is a Razor class library that references
/// this one and nothing else, and referencing alerting, uptime, leader election and the AI package
/// to display them would put all four on everyone who installs a dashboard. The board asks the
/// container what it has: a panel appears because a service is registered, not because a flag was
/// set.
/// </para>
/// <para>
/// Reads only, as every contract in this namespace does. Mutation stays on the interfaces that
/// already own it, which is what lets the dashboard show all of this in read-only mode.
/// </para>
/// </remarks>
public interface IUptimeInsights
{
    /// <summary>
    /// The share of a window a checker spent healthy.
    /// </summary>
    /// <param name="checkerName">The checker to report on.</param>
    /// <param name="window">How far back to look.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The report, or <c>null</c> if nothing is recorded for that checker.</returns>
    /// <remarks>
    /// Distinct from the percentage the board already shows, which is the share of the runs still in
    /// the rolling history -- a few minutes at a fast interval. This is measured over real time and
    /// survives history being trimmed.
    /// </remarks>
    Task<UptimeInsight?> GetUptimeAsync(
        string checkerName,
        TimeSpan window,
        CancellationToken cancellationToken = default);
}
