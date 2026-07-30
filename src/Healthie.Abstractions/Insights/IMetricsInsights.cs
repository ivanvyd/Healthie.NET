namespace Healthie.Abstractions.Insights;

/// <summary>
/// What the library's own instruments have counted, for a board to show.
/// </summary>
/// <remarks>
/// <para>
/// The <c>Healthie.NET</c> meter is the real home of this data and OpenTelemetry is the real way to
/// read it -- an APM keeps history, does percentiles properly, and alerts on them. This exists for
/// the case where there is no APM in front of the operator, which is most first runs and most small
/// deployments: the numbers are already being emitted, and nobody was looking at them.
/// </para>
/// <para>
/// So: a live window, not a time series. Nothing here is persisted or survives a restart, and the
/// board says as much beside it.
/// </para>
/// </remarks>
public interface IMetricsInsights
{
    /// <summary>What has been counted since the process started.</summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    MetricsSnapshot Snapshot(CancellationToken cancellationToken = default);
}
