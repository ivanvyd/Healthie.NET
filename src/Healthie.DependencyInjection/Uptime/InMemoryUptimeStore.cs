using Healthie.Abstractions.Enums;

namespace Healthie.Uptime;

/// <summary>
/// Keeps uptime segments in memory, for as long as the process lives.
/// </summary>
/// <remarks>
/// The default so that uptime works out of the box, and enough for a single node that is not asked
/// about last quarter. It does not survive a restart; register a durable store when the answer has
/// to outlive the process.
/// <para>
/// Segments are bounded per checker. Transitions are rare, so the cap is generous and exists only
/// so that a badly flapping checker cannot grow this without limit inside the process it is
/// monitoring -- the same reason the alert queue is bounded.
/// </para>
/// </remarks>
public sealed class InMemoryUptimeStore : IUptimeStore
{
    /// <summary>How many segments are kept per checker before the oldest are discarded.</summary>
    public const int MaxSegmentsPerChecker = 10_000;

    private readonly Dictionary<string, List<UptimeSegment>> _segments = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <inheritdoc />
    public Task RecordAsync(
        string checkerName,
        PulseCheckerHealth health,
        DateTime at,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkerName);

        lock (_gate)
        {
            if (!_segments.TryGetValue(checkerName, out var list))
            {
                _segments[checkerName] = [new UptimeSegment(checkerName, health, at)];
                return Task.CompletedTask;
            }

            var open = list.Count > 0 ? list[^1] : null;

            // Already in this health, so there is no transition and nothing to record.
            if (open is { IsOpen: true } && open.Health == health)
            {
                return Task.CompletedTask;
            }

            if (open is { IsOpen: true })
            {
                list[^1] = open with { EndedAt = at };
            }

            list.Add(new UptimeSegment(checkerName, health, at));

            if (list.Count > MaxSegmentsPerChecker)
            {
                list.RemoveRange(0, list.Count - MaxSegmentsPerChecker);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UptimeSegment>> GetSegmentsAsync(
        string checkerName,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkerName);

        lock (_gate)
        {
            if (!_segments.TryGetValue(checkerName, out var list))
            {
                return Task.FromResult<IReadOnlyList<UptimeSegment>>([]);
            }

            IReadOnlyList<UptimeSegment> overlapping =
                [.. list.Where(segment => segment.OverlapWith(from, to) > TimeSpan.Zero)];

            return Task.FromResult(overlapping);
        }
    }
}
