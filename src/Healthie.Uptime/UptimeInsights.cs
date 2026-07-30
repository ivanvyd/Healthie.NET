using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Insights;

namespace Healthie.Uptime;

/// <summary>
/// Reports this package's uptime to the dashboard, without the dashboard having to reference it.
/// </summary>
/// <remarks>
/// The board already shows a percentage of its own: the share of the runs still in the rolling
/// history, which at a one-second interval is the last couple of minutes. This one is measured over
/// real time and survives history being trimmed, which is what makes it worth showing separately
/// rather than replacing the other.
/// </remarks>
internal sealed class UptimeInsights(IUptimeStore store, TimeProvider? timeProvider = null) : IUptimeInsights
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public async Task<UptimeInsight?> GetUptimeAsync(
        string checkerName,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkerName);

        var to = _time.GetUtcNow().UtcDateTime;
        var from = to - window;

        var segments = await store.GetSegmentsAsync(checkerName, from, to, cancellationToken)
            .ConfigureAwait(false);

        if (segments.Count == 0)
        {
            return null;
        }

        var report = UptimeCalculator.Calculate(checkerName, segments, from, to);

        // Null when nothing was observed, which is not the same as nothing being recorded: the
        // dashboard shows the difference rather than printing a confident zero.
        if (report.UptimePercentage is not { } percentage)
        {
            return null;
        }

        return new UptimeInsight(percentage, window, LongestOutage(segments, from, to));
    }

    /// <summary>
    /// The longest unbroken unhealthy stretch inside the window.
    /// </summary>
    /// <remarks>
    /// A percentage alone cannot tell a hundred one-second blips from one long outage, and those are
    /// very different mornings. Segments are clipped to the window so a stretch that started before
    /// it is counted only for the part that falls inside.
    /// </remarks>
    private static TimeSpan? LongestOutage(IReadOnlyList<UptimeSegment> segments, DateTime from, DateTime to)
    {
        TimeSpan? longest = null;

        foreach (var segment in segments.Where(s => s.Health == PulseCheckerHealth.Unhealthy))
        {
            var start = segment.StartedAt < from ? from : segment.StartedAt;
            var end = segment.EndedAt is { } ended && ended < to ? ended : to;

            if (end <= start)
            {
                continue;
            }

            var length = end - start;

            if (longest is null || length > longest)
            {
                longest = length;
            }
        }

        return longest;
    }
}
