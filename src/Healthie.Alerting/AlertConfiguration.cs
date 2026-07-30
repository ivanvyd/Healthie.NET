using Healthie.Abstractions.Insights;

namespace Healthie.Alerting;

/// <summary>
/// Lets the dashboard change the alerting settings that a running dispatcher honours.
/// </summary>
/// <remarks>
/// Writes straight to the options object the dispatcher holds, which is the same singleton the host
/// configured: the dispatcher reads these four on every alert rather than snapshotting them, so a
/// change applies to the next one with nothing to restart or re-register.
/// </remarks>
/// <param name="options">The options the dispatcher is reading.</param>
/// <param name="dispatcher">The dispatcher, for sending a test alert through the real sinks.</param>
internal sealed class AlertConfiguration(HealthieAlertOptions options, AlertDispatcher dispatcher)
    : IAlertConfiguration
{
    /// <inheritdoc />
    public AlertSettings Current => new(
        options.MinimumSeverity,
        options.SendRecoveries,
        options.DeduplicationWindow,
        options.DeliveryTimeout);

    /// <inheritdoc />
    public void Apply(AlertSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.DeduplicationWindow < TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A deduplication window cannot be negative. Zero means alert on every change.",
                nameof(settings));
        }

        if (settings.DeliveryTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A delivery timeout must be positive, or no sink would ever get long enough to answer.",
                nameof(settings));
        }

        options.MinimumSeverity = settings.MinimumSeverity;
        options.SendRecoveries = settings.SendRecoveries;
        options.DeduplicationWindow = settings.DeduplicationWindow;
        options.DeliveryTimeout = settings.DeliveryTimeout;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AlertSinkStatus>> SendTestAlertAsync(CancellationToken cancellationToken = default) =>
        dispatcher.SendTestAlertAsync(cancellationToken);
}
