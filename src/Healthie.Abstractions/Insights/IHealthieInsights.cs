using Healthie.Abstractions.Enums;

namespace Healthie.Abstractions.Insights;

/// <summary>
/// The read-only views the dashboard renders when a feature package is installed.
/// </summary>
/// <remarks>
/// <para>
/// The dashboard is a Razor class library that references this package and nothing else, and that is
/// worth keeping: referencing alerting, uptime, leader election and the AI package to display them
/// would put all four on everyone who installs a dashboard.
/// </para>
/// <para>
/// So each feature package registers its own implementation of the small contract below, and the
/// dashboard asks the container what it has. A panel appears because a service is registered, not
/// because a flag was set, and an application that installed none of them sees the board it saw
/// before.
/// </para>
/// <para>
/// Reads only. Nothing here changes a checker: mutation stays on the interfaces that already own it,
/// which is what lets the dashboard show all of this in read-only mode.
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

/// <summary>
/// What a checker's uptime looked like over a window.
/// </summary>
/// <param name="Percentage">The share of the window spent healthy, from 0 to 100.</param>
/// <param name="Window">The window measured.</param>
/// <param name="LongestOutage">The longest unbroken unhealthy stretch, or <c>null</c> if there was none.</param>
public sealed record UptimeInsight(double Percentage, TimeSpan Window, TimeSpan? LongestOutage);

/// <summary>
/// The alerts that have been raised, and whether they are being delivered.
/// </summary>
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

/// <summary>
/// One alert, as the dashboard shows it.
/// </summary>
/// <param name="CheckerName">The checker the alert is about.</param>
/// <param name="DisplayName">That checker's display name.</param>
/// <param name="PreviousHealth">The health before, or <c>null</c> if it had never run.</param>
/// <param name="CurrentHealth">The health that raised the alert.</param>
/// <param name="Message">The check's own message.</param>
/// <param name="OccurredAt">When it was raised, in UTC.</param>
/// <param name="Delivered">Whether every sink accepted it.</param>
public sealed record AlertInsight(
    string CheckerName,
    string DisplayName,
    PulseCheckerHealth? PreviousHealth,
    PulseCheckerHealth CurrentHealth,
    string Message,
    DateTime OccurredAt,
    bool Delivered);

/// <summary>
/// Whether this replica is the one running the checks.
/// </summary>
/// <remarks>
/// With leader election on, only one replica runs a checker on each interval. A board that did not
/// say so would show a follower with everything idle and look broken.
/// </remarks>
public interface ILeadershipInsights
{
    /// <summary>Whether this replica currently holds the lease.</summary>
    bool IsLeader { get; }

    /// <summary>Something identifying this replica, for a board an operator is comparing across tabs.</summary>
    string ReplicaId { get; }
}

/// <summary>
/// An explanation of why a checker has been failing.
/// </summary>
public interface IDiagnosisInsights
{
    /// <summary>
    /// Explains a checker's recent failures.
    /// </summary>
    /// <param name="checkerName">The checker to explain.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The explanation.</returns>
    /// <remarks>
    /// Asked for rather than shown: this goes to a language model, which costs money and takes
    /// seconds, so nothing should call it on a board that redraws every second.
    /// </remarks>
    Task<string> ExplainAsync(string checkerName, CancellationToken cancellationToken = default);
}
