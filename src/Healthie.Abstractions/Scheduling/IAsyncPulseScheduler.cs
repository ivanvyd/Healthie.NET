using Healthie.Abstractions.Enums;

namespace Healthie.Abstractions.Scheduling;

/// <summary>
/// Defines a contract for scheduling a single asynchronous pulse check.
/// </summary>
public interface IAsyncPulseScheduler
{
    /// <summary>
    /// Schedules an asynchronous pulse check for the specified pulse checker.
    /// </summary>
    /// <param name="checker">The asynchronous pulse checker to schedule.</param>
    /// <returns>A task that represents the asynchronous scheduling operation.</returns>
    Task ScheduleAsync(IAsyncPulseChecker checker, PulseInterval interval);

    /// <summary>
    /// Unschedules an asynchronous pulse check for the specified pulse checker.
    /// </summary>
    /// <param name="checker">The asynchronous pulse checker to unschedule.</param>
    /// <returns>A task that represents the asynchronous unscheduling operation.</returns>
    Task UnscheduleAsync(IAsyncPulseChecker checker);
}
