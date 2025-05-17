using Healthie.Abstractions;
using Quartz;

namespace Healthie.Scheduling.Quartz.Jobs;

[DisallowConcurrentExecution]
public class AsyncPulseCheckerJob : IJob
{
    public static readonly string CheckerKey = "async_checker";

    public async Task Execute(IJobExecutionContext context)
    {
        var checker = (IAsyncPulseChecker)context.MergedJobDataMap[CheckerKey];
        await checker.TriggerAsync();
    }
}
