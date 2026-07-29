namespace Healthie.Alerting;

/// <summary>
/// Somewhere an alert is delivered: a webhook, a chat channel, an incident tracker, a mailbox.
/// </summary>
/// <remarks>
/// Implementations do not need to be defensive. Every call is made from the dispatcher's own loop,
/// with its own timeout, wrapped so that throwing affects nothing but that one delivery -- no check
/// is delayed by it and no component is reported unhealthy because of it. Throwing is the right way
/// to report a delivery that failed.
/// </remarks>
public interface IAlertSink
{
    /// <summary>
    /// Delivers one alert.
    /// </summary>
    /// <param name="alert">The health change to report.</param>
    /// <param name="cancellationToken">Signalled at the delivery timeout, or on shutdown.</param>
    /// <returns>A task that represents the asynchronous delivery.</returns>
    Task SendAsync(Alert alert, CancellationToken cancellationToken = default);
}
