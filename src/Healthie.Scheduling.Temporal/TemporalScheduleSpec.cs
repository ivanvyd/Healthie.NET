using Healthie.Abstractions.Scheduling;
using Temporalio.Client.Schedules;

namespace Healthie.Scheduling.Temporal;

/// <summary>
/// Expresses a <see cref="PulseSchedule"/> as the specification Temporal schedules by.
/// </summary>
/// <remarks>
/// Kept separate from the scheduler because it is the one part of this integration that can be
/// tested without a Temporal server: everything else needs a running cluster, and this is where a
/// mistake would silently change how often a check runs.
/// <para>
/// Cron passes through untranslated. Temporal parses standard Unix cron -- the same syntax
/// <see cref="PulseSchedule.Cron"/> carries -- so unlike Quartz there is no dialect to convert.
/// </para>
/// </remarks>
internal static class TemporalScheduleSpec
{
    /// <summary>Builds the Temporal specification for a schedule.</summary>
    /// <param name="schedule">The schedule to express.</param>
    public static ScheduleSpec From(PulseSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (schedule.CronExpression is { } cron)
        {
            return new ScheduleSpec { CronExpressions = [cron] };
        }

        // An interval rather than a cron expression for a fixed period: Temporal counts interval
        // occurrences from an epoch rather than from when the schedule was created, so two replicas
        // creating the same schedule agree on when it fires.
        return new ScheduleSpec { Intervals = [new ScheduleIntervalSpec(schedule.Period!.Value)] };
    }
}
