using Healthie.Abstractions.Enums;

namespace Healthie.Abstractions.Insights;

/// <summary>
/// The alerting settings that can be changed while the application is running.
/// </summary>
/// <remarks>
/// <para>
/// Only the ones that take effect. The dispatcher reads these four on every alert, so a change here
/// applies to the next one; its queue capacity and history length are fixed when it is built and
/// cannot be moved without a restart, so they are not offered. Showing a control that quietly does
/// nothing is the failure this whole release has been fixing.
/// </para>
/// <para>
/// In memory and not persisted: this is the same object the host configured at startup, so a
/// restart returns to whatever <c>AddHealthieAlerts</c> was given. The board says so.
/// </para>
/// </remarks>
public interface IAlertConfiguration
{
    /// <summary>What the dispatcher is using now.</summary>
    AlertSettings Current { get; }

    /// <summary>Applies new settings, from the next alert onwards.</summary>
    /// <param name="settings">The settings to apply.</param>
    void Apply(AlertSettings settings);

    /// <summary>
    /// Sends a test alert to every registered sink and reports what happened.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>Each sink, and whether it took the alert.</returns>
    /// <remarks>
    /// The only way to find out that a webhook URL is wrong is to use it. Deduplication and the
    /// severity threshold are bypassed, because the point is to exercise delivery rather than to
    /// decide whether this alert is worth sending.
    /// </remarks>
    Task<IReadOnlyList<AlertSinkStatus>> SendTestAlertAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The alerting settings a running application will honour a change to.
/// </summary>
/// <param name="MinimumSeverity">The least severe health that raises an alert.</param>
/// <param name="SendRecoveries">Whether returning to healthy raises one too.</param>
/// <param name="DeduplicationWindow">How long the same checker is quiet for after alerting.</param>
/// <param name="DeliveryTimeout">How long a single sink gets before it is abandoned.</param>
public sealed record AlertSettings(
    PulseCheckerHealth MinimumSeverity,
    bool SendRecoveries,
    TimeSpan DeduplicationWindow,
    TimeSpan DeliveryTimeout);
