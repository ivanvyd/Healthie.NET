namespace Healthie.Abstractions.Insights;

/// <summary>
/// What a checker's uptime looked like over a window.
/// </summary>
/// <param name="Percentage">The share of the window spent healthy, from 0 to 100.</param>
/// <param name="Window">The window measured.</param>
/// <param name="LongestOutage">The longest unbroken unhealthy stretch, or <c>null</c> if there was none.</param>
/// <remarks>
/// The longest outage is carried beside the percentage because a percentage alone cannot tell a
/// hundred one-second blips from one long outage, and those are very different mornings.
/// </remarks>
public sealed record UptimeInsight(double Percentage, TimeSpan Window, TimeSpan? LongestOutage);
