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
    public async Task ScheduleAsync(
        IPulseChecker checker,
        PulseInterval interval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checker);

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

        var cronExpression = interval.ToCronExpression();
        var trigger = TriggerBuilder
            .Create()
            .WithIdentity(triggerKey)
            .WithCronSchedule(cronExpression)
            .Build();

        await scheduler
            .ScheduleJob(job, [trigger], replace: true, cancellationToken)
            .ConfigureAwait(false);

        logger?.LogInformation(
            "Scheduled pulse checker '{CheckerName}' with CRON expression '{CronExpression}'.",
            jobName,
            cronExpression);
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
