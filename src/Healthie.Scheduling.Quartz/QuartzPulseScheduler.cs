using Healthie.Abstractions;
using Healthie.Abstractions.Scheduling;
using Healthie.Scheduling.Quartz.Converters;
using Healthie.Scheduling.Quartz.Jobs;
using Quartz;

namespace Healthie.Scheduling.Quartz;

public class QuartzPulseScheduler : IPulseScheduler
{
    private readonly ICronConverter _cronConverter;
    private readonly ISchedulerFactory _schedulerFactory;

    public QuartzPulseScheduler(ICronConverter cronConverter, ISchedulerFactory schedulerFactory)
    {
        _cronConverter = cronConverter;
        _schedulerFactory = schedulerFactory;
    }

    public void Schedule(IPulseChecker checker, TimeSpan interval)
    {
        var scheduler = _schedulerFactory.GetScheduler().GetAwaiter().GetResult();
        scheduler.Start().GetAwaiter().GetResult();

        var jobName = checker.GetType().Name;
        var jobKey = new JobKey(jobName);
        var triggerKey = new TriggerKey($"{jobName}-trigger");

        // Create job
        var job = JobBuilder.Create<PulseCheckerJob>()
            .WithIdentity(jobKey)
            .UsingJobData(new JobDataMap { { PulseCheckerJob.CheckerKey, checker } })
            .Build();

        // Create trigger with cron schedule
        var cronExpression = _cronConverter.Convert(interval);
        var trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .WithCronSchedule(cronExpression)
            .Build();

        // Schedule the job
        scheduler.ScheduleJob(job, trigger).GetAwaiter().GetResult();
    }
}
