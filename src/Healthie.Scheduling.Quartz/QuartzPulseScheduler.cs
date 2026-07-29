using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Extensions;
using Healthie.Abstractions.Scheduling;
using Healthie.Scheduling.Quartz.Jobs;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Healthie.Scheduling.Quartz;

/// <summary>
/// An <see cref="IPulseScheduler"/> implementation backed by Quartz.NET.
/// Provides persistent, CRON-based scheduling with support for clustering
/// and advanced job store configurations.
/// </summary>
/// <remarks>
/// <para>
/// Each pulse checker is scheduled as a Quartz job with a CRON trigger derived
/// from the <see cref="PulseInterval"/> via <see cref="PulseIntervalExtensions.ToCronExpression"/>.
/// The <see cref="PulseCheckerJob"/> resolves the checker by name from DI,
/// avoiding serialization of complex objects in the Quartz <see cref="JobDataMap"/>.
/// </para>
/// <para>
/// For simple scenarios without persistence or clustering requirements,
/// consider using the built-in <c>TimerPulseScheduler</c> from <c>Healthie.DependencyInjection</c>.
/// </para>
/// </remarks>
public sealed class QuartzPulseScheduler(
    ISchedulerFactory schedulerFactory,
    ILogger<QuartzPulseScheduler>? logger = null) : IPulseScheduler
{
    /// <inheritdoc />
    /// <remarks>
    /// Kept on the interval's own cron expression rather than routed through
    /// <see cref="ScheduleAsync(IPulseChecker, PulseSchedule, CancellationToken)"/>. A cron trigger
    /// fires on wall-clock boundaries -- every fifth second means :00, :05, :10 -- where a simple
    /// repeating trigger counts from whenever the job was scheduled. Existing checkers are on the
    /// former and should stay there.
    /// </remarks>
    public Task ScheduleAsync(
        IPulseChecker checker,
        PulseInterval interval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checker);

        return ScheduleCoreAsync(checker, interval.ToCronExpression(), period: null, cancellationToken);
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">
    /// The cron expression has no Quartz equivalent -- see <see cref="UnixCron"/>.
    /// </exception>
    public Task ScheduleAsync(
        IPulseChecker checker,
        PulseSchedule schedule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(schedule);

        // An interval the enum already names keeps the aligned cron trigger it has always had.
        if (schedule.TryToInterval(out var interval))
        {
            return ScheduleAsync(checker, interval, cancellationToken);
        }

        return schedule.IsCron
            ? ScheduleCoreAsync(checker, UnixCron.ToQuartz(schedule.CronExpression!), period: null, cancellationToken)
            : ScheduleCoreAsync(checker, cronExpression: null, schedule.Period, cancellationToken);
    }

    /// <summary>
    /// Replaces this checker's Quartz job with one on the given trigger.
    /// </summary>
    /// <remarks>
    /// Exactly one of <paramref name="cronExpression"/> and <paramref name="period"/> is supplied:
    /// a cron trigger for anything expressible on wall-clock boundaries, and a simple repeating
    /// trigger for a bare period, which is what a schedule like "every 90 seconds" needs.
    /// </remarks>
    private async Task ScheduleCoreAsync(
        IPulseChecker checker,
        string? cronExpression,
        TimeSpan? period,
        CancellationToken cancellationToken)
    {
        var scheduler = await schedulerFactory
            .GetScheduler(cancellationToken)
            .ConfigureAwait(false);

        var jobName = checker.Name;
        var jobKey = new JobKey(jobName);
        var triggerKey = new TriggerKey($"{jobName}-trigger");

        // Remove any existing schedule for this checker before re-scheduling
        if (await scheduler.CheckExists(jobKey, cancellationToken).ConfigureAwait(false))
        {
            await scheduler.DeleteJob(jobKey, cancellationToken).ConfigureAwait(false);

            logger?.LogDebug(
                "Removed existing Quartz job for pulse checker '{CheckerName}' before rescheduling.",
                jobName);
        }

        var job = JobBuilder
            .Create<PulseCheckerJob>()
            .WithIdentity(jobKey)
            .UsingJobData(PulseCheckerJob.CheckerNameKey, jobName)
            .Build();

        var builder = TriggerBuilder.Create().WithIdentity(triggerKey);

        // Pinned to UTC. Quartz defaults a cron trigger to TimeZoneInfo.Local, so "0 9 * * *"
        // would fire at 09:00 wherever the server happens to be, while the built-in scheduler
        // evaluates the same expression against UtcNow -- one expression, two different times, and
        // invisible on the UTC-configured hosts most containers run as. The dashboard renders UTC
        // throughout, so UTC is also the one the rest of this library already means.
        var trigger = cronExpression is not null
            ? builder.WithCronSchedule(cronExpression, c => c.InTimeZone(TimeZoneInfo.Utc)).Build()
            : builder
                .StartNow()
                .WithSimpleSchedule(schedule => schedule.WithInterval(period!.Value).RepeatForever())
                .Build();

        await scheduler
            .ScheduleJob(job, [trigger], replace: true, cancellationToken)
            .ConfigureAwait(false);

        logger?.LogInformation(
            "Scheduled pulse checker '{CheckerName}' on {Schedule}.",
            jobName,
            cronExpression is not null ? $"CRON expression '{cronExpression}'" : $"a {period} interval");
    }

    /// <inheritdoc />
    public async Task UnscheduleAsync(
        IPulseChecker checker,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checker);

        var scheduler = await schedulerFactory
            .GetScheduler(cancellationToken)
            .ConfigureAwait(false);

        var jobName = checker.Name;
        var jobKey = new JobKey(jobName);

        if (await scheduler.CheckExists(jobKey, cancellationToken).ConfigureAwait(false))
        {
            await scheduler.DeleteJob(jobKey, cancellationToken).ConfigureAwait(false);

            logger?.LogInformation(
                "Unscheduled pulse checker '{CheckerName}'.",
                jobName);
        }
        else
        {
            logger?.LogDebug(
                "Pulse checker '{CheckerName}' was not scheduled; nothing to unschedule.",
                jobName);
        }
    }
}
