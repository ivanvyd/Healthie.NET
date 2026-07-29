using Healthie.Abstractions.Enums;

namespace Healthie.Uptime;

/// <summary>
/// Where a checker's health history is kept for longer than the rolling buffer holds it.
/// </summary>
/// <remarks>
/// Separate from <c>IStateProvider</c> on purpose. State is one small document per checker,
/// overwritten constantly and read on every check; this is an append-only series read occasionally
/// and kept for months. Storing them together would mean every check rewriting a document that
/// grows for ever.
/// </remarks>
public interface IUptimeStore
{
    /// <summary>
    /// Records that a checker entered a health, closing whatever segment it was in.
    /// </summary>
    /// <param name="checkerName">The checker.</param>
    /// <param name="health">The health it entered.</param>
    /// <param name="at">When, in UTC.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <remarks>
    /// Recording the same health it is already in is ignored, so a caller does not have to track
    /// what came before to avoid splitting one stretch into many.
    /// </remarks>
    Task RecordAsync(string checkerName, PulseCheckerHealth health, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the segments touching a window.
    /// </summary>
    /// <param name="checkerName">The checker.</param>
    /// <param name="from">The start of the window, in UTC.</param>
    /// <param name="to">The end of the window, in UTC.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// Every segment overlapping the window, including one that began before it and one still open.
    /// </returns>
    Task<IReadOnlyList<UptimeSegment>> GetSegmentsAsync(
        string checkerName,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);
}
