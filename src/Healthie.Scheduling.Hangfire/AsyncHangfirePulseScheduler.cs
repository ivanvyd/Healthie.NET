using Hangfire;
using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Extensions;
using Healthie.Abstractions.Scheduling;

namespace Healthie.Scheduling.Hangfire;

public class AsyncHangfirePulseScheduler(IRecurringJobManagerV2 recurringJobManager)
    : IAsyncPulseScheduler
{
    private readonly IRecurringJobManager _recurringJobManager = recurringJobManager;

    public Task ScheduleAsync(IAsyncPulseChecker checker, PulseInterval interval)
    {
        var cronExpression = interval.ToCronExpression();
        _recurringJobManager.AddOrUpdate(
            checker.Name,
            () => Task.Run(() => checker.TriggerAsync()),
            cronExpression);

        return Task.CompletedTask;
    }

    public Task UnscheduleAsync(IAsyncPulseChecker checker)
    {
        throw new NotImplementedException();
    }
}
