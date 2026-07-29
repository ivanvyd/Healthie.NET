using Healthie.Abstractions.Enums;

namespace Healthie.Uptime;

/// <summary>
/// Turns recorded segments into a report about a window.
/// </summary>
/// <remarks>
/// A pure function of its inputs, deliberately: everything interesting about uptime is arithmetic
/// on overlapping intervals -- a segment that began before the window, one still open, a gap while
/// the application was down -- and all of it is easier to be sure of when there is no clock and no
/// store involved.
/// </remarks>
public static class UptimeCalculator
{
    /// <summary>
    /// Reports how a checker spent a window.
    /// </summary>
    /// <param name="checkerName">The checker being reported on.</param>
    /// <param name="segments">
    /// Its segments. May include segments wholly outside the window, which contribute nothing, and
    /// need not be sorted.
    /// </param>
    /// <param name="from">The start of the window, in UTC.</param>
    /// <param name="to">The end of the window, in UTC.</param>
    /// <exception cref="ArgumentException">The window ends before it starts.</exception>
    public static UptimeReport Calculate(
        string checkerName,
        IEnumerable<UptimeSegment> segments,
        DateTime from,
        DateTime to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkerName);
        ArgumentNullException.ThrowIfNull(segments);

        if (to < from)
        {
            throw new ArgumentException($"The window ends at {to:u}, before it starts at {from:u}.", nameof(to));
        }

        var healthy = TimeSpan.Zero;
        var suspicious = TimeSpan.Zero;
        var unhealthy = TimeSpan.Zero;

        foreach (var segment in segments)
        {
            var overlap = segment.OverlapWith(from, to);

            if (overlap == TimeSpan.Zero)
            {
                continue;
            }

            switch (segment.Health)
            {
                case PulseCheckerHealth.Healthy:
                    healthy += overlap;
                    break;
                case PulseCheckerHealth.Suspicious:
                    suspicious += overlap;
                    break;
                case PulseCheckerHealth.Unhealthy:
                    unhealthy += overlap;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(segments),
                        segment.Health,
                        $"Segment for '{segment.CheckerName}' has a health that is not a defined value.");
            }
        }

        var observed = healthy + suspicious + unhealthy;
        var window = to - from;

        // Clamped rather than trusted. Overlapping segments would otherwise account for more time
        // than the window holds and produce a negative unknown, which is not a thing.
        var unknown = window > observed ? window - observed : TimeSpan.Zero;

        return new UptimeReport(checkerName, from, to, healthy, suspicious, unhealthy, unknown);
    }
}
