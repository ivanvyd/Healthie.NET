using Healthie.Abstractions.Enums;

namespace Healthie.Alerting;

/// <summary>
/// Controls which health changes become alerts, and how hard the library tries to deliver them.
/// </summary>
public sealed class HealthieAlertOptions
{
    /// <summary>
    /// The least severe health that opens an alert. Defaults to <see cref="PulseCheckerHealth.Unhealthy"/>.
    /// </summary>
    /// <remarks>
    /// Suspicious is the state a checker passes through on its way to unhealthy, so alerting on it
    /// by default would page somebody for every blip the threshold exists to absorb. Lower it when
    /// you want the early warning -- a certificate inside its expiry window is suspicious for weeks
    /// and never becomes unhealthy until it is too late to act.
    /// </remarks>
    public PulseCheckerHealth MinimumSeverity { get; set; } = PulseCheckerHealth.Unhealthy;

    /// <summary>
    /// Whether a checker returning to healthy sends an alert. Defaults to <c>true</c>.
    /// </summary>
    /// <remarks>
    /// On by default because an alert nobody is told to stop worrying about stays open, and a
    /// person who was paged deserves to know it ended.
    /// </remarks>
    public bool SendRecoveries { get; set; } = true;

    /// <summary>
    /// How long to suppress repeat alerts for the same checker. Defaults to five minutes.
    /// </summary>
    /// <remarks>
    /// A component on the edge of working flaps, and each flip is a real health change. Without a
    /// window, a checker running every second could send a hundred alerts about one incident.
    /// Recoveries are held to the same window for the same reason.
    /// </remarks>
    public TimeSpan DeduplicationWindow { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a single sink is given to deliver one alert. Defaults to ten seconds.
    /// </summary>
    /// <remarks>
    /// A sink that hangs must not hold up every alert behind it, and an unreachable webhook hangs
    /// for as long as its socket allows rather than failing quickly.
    /// </remarks>
    public TimeSpan DeliveryTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How many alerts may be waiting for delivery before new ones are dropped. Defaults to 1024.
    /// </summary>
    /// <remarks>
    /// Bounded on purpose. If sinks are slower than checks produce alerts, something has to give,
    /// and it must not be the checks: an unbounded queue trades a delivery problem for a memory
    /// leak in the process being monitored. A dropped alert is counted and logged.
    /// </remarks>
    public int QueueCapacity { get; set; } = 1024;

    private int _historyLength = 50;

    /// <summary>
    /// How many recent alerts the dashboard can show. Defaults to 50, minimum 1.
    /// </summary>
    /// <remarks>
    /// A window onto what just happened rather than a record; the record is wherever the sinks
    /// deliver to. Kept in memory and bounded, so it costs nothing to leave on. Clamped rather than
    /// rejected, as <c>MaxHistoryLength</c> is.
    /// </remarks>
    public int HistoryLength
    {
        get => _historyLength;
        set => _historyLength = Math.Max(value, 1);
    }
}
