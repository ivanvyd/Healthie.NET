using Healthie.Abstractions;
using Healthie.Abstractions.Extensions;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Healthie.Scheduling.Quartz.Jobs;
using Quartz;

namespace Healthie.Scheduling.Quartz;

public class AsyncQuartzPulseScheduler(ISchedulerFactory schedulerFactory) : IAsyncPulseScheduler
{
    private readonly ISchedulerFactory _schedulerFactory = schedulerFactory;

    public async Task ScheduleAsync(IAsyncPulseChecker checker, PulseInterval interval)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.Start();

        var jobName = checker.Name;
        var jobKey = new JobKey(jobName);
        var triggerKey = new TriggerKey($"{jobName}-trigger");

        var job = JobBuilder
            .Create<AsyncPulseCheckerJob>()
            .WithIdentity(jobKey)
            .UsingJobData(new JobDataMap { { AsyncPulseCheckerJob.CheckerKey, checker } })
            .Build();

        var cronExpression = interval.ToCronExpression();
        var trigger = TriggerBuilder
            .Create()
            .WithIdentity(triggerKey)
            .WithCronSchedule(cronExpression)
            .Build();

        await scheduler.ScheduleJob(job, [trigger], true);
    }
}
