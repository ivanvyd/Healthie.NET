using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;

namespace Healthie.AI;

/// <summary>
/// Reports whether a checker's recent failure rate stands out against the rest of its history.
/// </summary>
/// <remarks>
/// This is deliberately arithmetic rather than a model call: it is cheap, gives the same answer
/// every time, and can be tested. Reserve the language model for explaining a problem, not for
/// counting.
/// </remarks>
public sealed class FailureRateAnomalyDetector
{
    /// <summary>
    /// Compares the failure rate of the most recent runs against the runs before them.
    /// </summary>
    /// <param name="history">The checker's history, oldest entry first.</param>
    /// <param name="recentCount">How many of the most recent runs count as "recent". Defaults to 5.</param>
    /// <param name="rateIncrease">
    /// How much higher the recent failure rate must be, as a proportion of all runs, before it is
    /// reported. Defaults to 0.3, meaning 30 percentage points.
    /// </param>
    /// <returns>What the comparison found.</returns>
    /// <remarks>
    /// Reports nothing when there is too little history to compare against, rather than calling the
    /// first couple of failures an anomaly.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="history"/> is <c>null</c>.</exception>
    public AnomalyReport Detect(
        IReadOnlyList<PulseCheckerHistoryEntry> history,
        int recentCount = 5,
        double rateIncrease = 0.3)
    {
        ArgumentNullException.ThrowIfNull(history);

        recentCount = Math.Max(recentCount, 1);

        // Both windows need something in them for a comparison to mean anything.
        if (history.Count < recentCount + 2)
        {
            return AnomalyReport.NotEnoughHistory;
        }

        var recent = history.Skip(history.Count - recentCount).ToList();
        var earlier = history.Take(history.Count - recentCount).ToList();

        var recentRate = FailureRateOf(recent);
        var earlierRate = FailureRateOf(earlier);

        return recentRate - earlierRate >= rateIncrease
            ? new AnomalyReport(true, recentRate, earlierRate)
            : new AnomalyReport(false, recentRate, earlierRate);
    }

    private static double FailureRateOf(IReadOnlyCollection<PulseCheckerHistoryEntry> entries)
        => entries.Count == 0
            ? 0
            : (double)entries.Count(entry => entry.Health != PulseCheckerHealth.Healthy) / entries.Count;
}

/// <summary>What comparing a checker's recent failure rate against its earlier history found.</summary>
/// <param name="IsAnomalous">Whether the recent failure rate stands out.</param>
/// <param name="RecentFailureRate">The proportion of recent runs that failed.</param>
/// <param name="EarlierFailureRate">The proportion of earlier runs that failed.</param>
public record AnomalyReport(bool IsAnomalous, double RecentFailureRate, double EarlierFailureRate)
{
    /// <summary>There was not enough history to compare against.</summary>
    public static readonly AnomalyReport NotEnoughHistory = new(false, 0, 0);
}
