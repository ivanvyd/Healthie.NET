using Healthie.Abstractions.Enums;

namespace Healthie.Uptime;

/// <summary>
/// How a checker spent a window of time.
/// </summary>
/// <param name="CheckerName">The checker this describes.</param>
/// <param name="From">The start of the window, in UTC.</param>
/// <param name="To">The end of the window, in UTC.</param>
/// <param name="Healthy">Time spent healthy.</param>
/// <param name="Suspicious">Time spent suspicious.</param>
/// <param name="Unhealthy">Time spent unhealthy.</param>
/// <param name="Unknown">
/// Time not covered by any recorded segment -- before the checker first ran, or while the
/// application was not running.
/// </param>
public sealed record UptimeReport(
    string CheckerName,
    DateTime From,
    DateTime To,
    TimeSpan Healthy,
    TimeSpan Suspicious,
    TimeSpan Unhealthy,
    TimeSpan Unknown)
{
    /// <summary>The window's full length.</summary>
    public TimeSpan Window => To - From;

    /// <summary>The time actually accounted for, which is the window less <see cref="Unknown"/>.</summary>
    public TimeSpan Observed => Healthy + Suspicious + Unhealthy;

    /// <summary>
    /// The percentage of observed time spent healthy, or <c>null</c> when nothing was observed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured against observed time rather than the window. Time the application was not running
    /// is time nothing was watching, and counting it as downtime would report an outage whenever a
    /// deployment restarted the process -- while counting it as uptime would claim a component was
    /// fine over a period nobody looked at it. It is neither, so it is excluded and reported
    /// separately as <see cref="Unknown"/>.
    /// </para>
    /// <para>
    /// Null rather than zero or a hundred when nothing was observed, because a checker that never
    /// ran has no uptime -- and either number would be a claim about a period with no evidence.
    /// </para>
    /// </remarks>
    public double? UptimePercentage => Observed > TimeSpan.Zero
        ? Healthy.TotalSeconds / Observed.TotalSeconds * 100d
        : null;

    /// <summary>
    /// Whether the window met a target, or <c>null</c> when nothing was observed.
    /// </summary>
    /// <param name="targetPercentage">The target, such as <c>99.9</c>.</param>
    public bool? Met(double targetPercentage) => UptimePercentage is { } actual ? actual >= targetPercentage : null;

    /// <summary>Time spent in a particular health.</summary>
    /// <param name="health">The health to look up.</param>
    /// <exception cref="ArgumentOutOfRangeException">The health is not a defined value.</exception>
    public TimeSpan TimeIn(PulseCheckerHealth health) => health switch
    {
        PulseCheckerHealth.Healthy => Healthy,
        PulseCheckerHealth.Suspicious => Suspicious,
        PulseCheckerHealth.Unhealthy => Unhealthy,
        _ => throw new ArgumentOutOfRangeException(nameof(health), health, "Not a defined health."),
    };
}
