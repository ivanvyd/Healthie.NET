using Healthie.Abstractions.Enums;

namespace Healthie.Abstractions.Scheduling;

/// <summary>
/// Defines a contract for scheduling pulse checks.
/// </summary>
public interface IPulseScheduler
{
    /// <summary>
    /// Schedules a pulse check for the specified pulse checker with the given interval.
    /// </summary>
    /// <param name="checker">The pulse checker to schedule.</param>
    /// <param name="interval">The interval at which the pulse check should occur.</param>
    void Schedule(IPulseChecker checker, PulseInterval interval);

    /// <summary>
    /// Unschedules a previously scheduled pulse check for the specified pulse checker.
    /// </summary>
    /// <param name="checker">The pulse checker to unschedule.</param>
    void Unschedule(IPulseChecker checker);
}
