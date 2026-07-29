using Healthie.Abstractions.Enums;

namespace Healthie.Uptime;

/// <summary>
/// A stretch of time a checker spent in one health.
/// </summary>
/// <remarks>
/// Uptime is stored as the transitions rather than as the checks. A checker running every second
/// produces 86,400 results a day and perhaps four transitions, and both answer "how long was it
/// down" -- but only one of them answers it exactly, and only one of them is small enough to keep
/// for a year. This is also why the rolling history cannot do the job: it holds the last hundred
/// results, which for a one-second checker is the last hundred seconds.
/// </remarks>
/// <param name="CheckerName">The checker this describes.</param>
/// <param name="Health">The health held for the duration.</param>
/// <param name="StartedAt">When the checker entered this health, in UTC.</param>
/// <param name="EndedAt">When it left, in UTC, or <c>null</c> while it is still in it.</param>
public sealed record UptimeSegment(
    string CheckerName,
    PulseCheckerHealth Health,
    DateTime StartedAt,
    DateTime? EndedAt = null)
{
    /// <summary>Whether this is the segment the checker is currently in.</summary>
    public bool IsOpen => EndedAt is null;

    /// <summary>
    /// How much of this segment falls inside a window.
    /// </summary>
    /// <param name="from">The start of the window, in UTC.</param>
    /// <param name="to">The end of the window, in UTC.</param>
    /// <returns>The overlap, which is <see cref="TimeSpan.Zero"/> when there is none.</returns>
    /// <remarks>
    /// Clipped at both ends. A segment that began before the window contributes only the part
    /// inside it, and an open segment is measured up to the window's end rather than to now --
    /// otherwise a report about last month would keep growing.
    /// </remarks>
    public TimeSpan OverlapWith(DateTime from, DateTime to)
    {
        var start = StartedAt > from ? StartedAt : from;
        var end = (EndedAt ?? to) < to ? (EndedAt ?? to) : to;

        return end > start ? end - start : TimeSpan.Zero;
    }
}
