using Healthie.Abstractions;
using Healthie.Abstractions.Scheduling;
using Healthie.Scheduling.Quartz.Converters;
using Healthie.Scheduling.Quartz.Jobs;
using Quartz;

namespace Healthie.Scheduling.Quartz;

public class AsyncQuartzPulseScheduler : IAsyncPulseScheduler
{
    private readonly ICronConverter _cronConverter;
    private readonly ISchedulerFactory _schedulerFactory;

    public AsyncQuartzPulseScheduler(ICronConverter cronConverter, ISchedulerFactory schedulerFactory)
    {
        _cronConverter = cronConverter;
        _schedulerFactory = schedulerFactory;
    }

    public async Task ScheduleAsync(IAsyncPulseChecker checker, TimeSpan interval)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.Start();

        var jobName = checker.GetType().Name;
        var jobKey = new JobKey(jobName);
        var triggerKey = new TriggerKey($"{jobName}-trigger");

        // Create job
        var job = JobBuilder.Create<AsyncPulseCheckerJob>()
            .WithIdentity(jobKey)
            .UsingJobData(new JobDataMap { { AsyncPulseCheckerJob.CheckerKey, checker } })
            .Build();

        // Create trigger with cron schedule
        var cronExpression = _cronConverter.Convert(interval);
        var trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .WithCronSchedule(cronExpression)
            .Build();

        // Schedule the job
        await scheduler.ScheduleJob(job, trigger);
    }
}
