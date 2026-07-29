using Microsoft.Extensions.Logging;

namespace Healthie.Abstractions.Diagnostics;

/// <summary>
/// The log messages <see cref="PulseChecker"/> writes.
/// </summary>
/// <remarks>
/// Source-generated rather than written as interpolated calls: the generator produces a strongly
/// typed method per message that does no formatting and allocates nothing when the level is
/// disabled. These sit on the path every check takes, so that matters more here than it would in a
/// startup path.
/// <para>
/// Event ids are stable and grouped by what they are about -- 1000s for a check running, 2000s for
/// state -- so a filter written against them keeps meaning what it meant.
/// </para>
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Debug,
        Message = "Skipping trigger for '{CheckerName}': the previous check is still running.")]
    public static partial void OverlappingTriggerSkipped(ILogger logger, string checkerName);

    /// <remarks>
    /// Debug, not Warning. The check itself throwing is a normal way for a monitored component to
    /// be down, and it is already reported as an unhealthy result -- logging it louder would make
    /// every outage look like a fault in the monitoring.
    /// </remarks>
    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Debug,
        Message = "Pulse check for '{CheckerName}' threw.")]
    public static partial void CheckThrew(ILogger logger, Exception exception, string checkerName);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Pulse checker '{CheckerName}' changed from {PreviousHealth} to {CurrentHealth}.")]
    public static partial void StateChanged(
        ILogger logger,
        string checkerName,
        string previousHealth,
        string currentHealth);
}
