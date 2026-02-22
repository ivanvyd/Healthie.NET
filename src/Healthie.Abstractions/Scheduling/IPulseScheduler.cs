using Healthie.Abstractions.Enums;

namespace Healthie.Abstractions.Scheduling;

/// <summary>
/// Defines a contract for scheduling and unscheduling individual pulse checkers.
/// </summary>
public interface IPulseScheduler
{
    /// <summary>
    /// Schedules a pulse checker for periodic execution at the specified interval.
    /// </summary>
    /// <param name="checker">The pulse checker to schedule.</param>
    /// <param name="interval">The interval at which to execute the pulse check.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous scheduling operation.</returns>
    Task ScheduleAsync(IPulseChecker checker, PulseInterval interval, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unschedules a previously scheduled pulse checker.
    /// </summary>
    /// <param name="checker">The pulse checker to unschedule.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous unscheduling operation.</returns>
    Task UnscheduleAsync(IPulseChecker checker, CancellationToken cancellationToken = default);
}
