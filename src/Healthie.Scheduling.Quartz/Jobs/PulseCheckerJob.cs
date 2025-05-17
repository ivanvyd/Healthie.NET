using Healthie.Abstractions;
using Quartz;

namespace Healthie.Scheduling.Quartz.Jobs;

[DisallowConcurrentExecution]
public class PulseCheckerJob : IJob
{
    public static readonly string CheckerKey = "checker";

    public Task Execute(IJobExecutionContext context)
    {
        var checker = (IPulseChecker)context.MergedJobDataMap[CheckerKey];
        checker.Trigger();
        return Task.CompletedTask;
    }
}