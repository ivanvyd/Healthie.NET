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
    /// Schedules a pulse checker on a schedule that a <see cref="PulseInterval"/> may not be able
    /// to express -- a longer period, or a cron expression.
    /// </summary>
    /// <param name="checker">The pulse checker to schedule.</param>
    /// <param name="schedule">The schedule to run it on.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous scheduling operation.</returns>
    /// <exception cref="NotSupportedException">
    /// The implementation does not understand this schedule. The default implementation throws for
    /// anything no <see cref="PulseInterval"/> represents exactly.
    /// </exception>
    /// <remarks>
    /// Defaulted rather than abstract so that a scheduler written against the older interface keeps
    /// compiling and keeps working. The default forwards a schedule an interval can express and
    /// refuses one it cannot, naming the implementation that refused: a scheduler that silently
    /// rounded a six-hour schedule down to five minutes would run a check seventy-two times as
    /// often as asked, and look like it worked.
    /// </remarks>
    Task ScheduleAsync(IPulseChecker checker, PulseSchedule schedule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (schedule.TryToInterval(out var interval))
        {
            return ScheduleAsync(checker, interval, cancellationToken);
        }

        throw new NotSupportedException(
            $"{GetType().Name} cannot schedule '{schedule}'. It only supports the intervals " +
            $"{nameof(PulseInterval)} defines; register a scheduler that implements " +
            $"{nameof(ScheduleAsync)}({nameof(PulseSchedule)}) to use it.");
    }

    /// <summary>
    /// Unschedules a previously scheduled pulse checker.
    /// </summary>
    /// <param name="checker">The pulse checker to unschedule.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous unscheduling operation.</returns>
    Task UnscheduleAsync(IPulseChecker checker, CancellationToken cancellationToken = default);
}
