using Healthie.Abstractions.Enums;

namespace Healthie.Abstractions.Insights;

/// <summary>
/// What the library's instruments have counted since the process started.
/// </summary>
/// <param name="Checks">Total checks completed.</param>
/// <param name="ResultsByHealth">Checks completed, by the health each reported.</param>
/// <param name="Transitions">Checks that produced a state different from the stored one.</param>
/// <param name="OverlappedTriggers">Triggers that returned immediately because the previous check had not finished.</param>
/// <param name="MeanDuration">Mean check duration, or <c>null</c> before anything has run.</param>
/// <param name="SlowestDuration">The slowest single check, or <c>null</c> before anything has run.</param>
/// <param name="Since">When collection started, which is when the process did.</param>
public sealed record MetricsSnapshot(
    long Checks,
    IReadOnlyDictionary<PulseCheckerHealth, long> ResultsByHealth,
    long Transitions,
    long OverlappedTriggers,
    TimeSpan? MeanDuration,
    TimeSpan? SlowestDuration,
    DateTime Since)
{
    /// <summary>
    /// The share of completed checks that reported healthy, or <c>null</c> before anything has run.
    /// </summary>
    public double? HealthyShare => Checks == 0
        ? null
        : 100d * ResultsByHealth.GetValueOrDefault(PulseCheckerHealth.Healthy) / Checks;

    /// <summary>
    /// Whether any trigger has been skipped for overlapping the previous one.
    /// </summary>
    /// <remarks>
    /// Called out separately because it is the one number here that means something is wrong rather
    /// than something happened: a checker whose check outlasts its own interval looks healthy and is
    /// quietly running at a fraction of the rate it was asked to. This is the only place it shows.
    /// </remarks>
    public bool HasOverlaps => OverlappedTriggers > 0;
}
