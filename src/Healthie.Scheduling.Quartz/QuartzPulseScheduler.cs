using Healthie.Abstractions;
using Healthie.Abstractions.Extensions;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Healthie.Scheduling.Quartz.Jobs;
using Quartz;

namespace Healthie.Scheduling.Quartz;

public class QuartzPulseScheduler(ISchedulerFactory schedulerFactory) : IPulseScheduler
{
    private readonly ISchedulerFactory _schedulerFactory = schedulerFactory;

    public void Schedule(IPulseChecker checker, PulseInterval interval)
    {
        var scheduler = _schedulerFactory.GetScheduler().GetAwaiter().GetResult();
        scheduler.Start().GetAwaiter().GetResult();

        var jobName = checker.Name;
        var jobKey = new JobKey(jobName);
        var triggerKey = new TriggerKey($"{jobName}-trigger");

        var job = JobBuilder
            .Create<PulseCheckerJob>()
            .WithIdentity(jobKey)
            .UsingJobData(new JobDataMap { { PulseCheckerJob.CheckerKey, checker } })
            .Build();

        var cronExpression = interval.ToCronExpression();
        var trigger = TriggerBuilder
            .Create()
            .WithIdentity(triggerKey)
            .WithCronSchedule(cronExpression)
            .Build();

        scheduler.ScheduleJob(job, [trigger], true).GetAwaiter().GetResult();
    }
}
