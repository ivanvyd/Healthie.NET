using Hangfire;
using Healthie.Abstractions;
using Healthie.Abstractions.Scheduling;
using Healthie.Scheduling.Hangfire.Converters;

namespace Healthie.Scheduling.Hangfire;

public class AsyncHangfirePulseScheduler(ICronConverter cronConverter, IRecurringJobManagerV2 recurringJobManager)
    : IAsyncPulseScheduler
{
    private readonly ICronConverter _cronConverter = cronConverter;
    private readonly IRecurringJobManager _recurringJobManager = recurringJobManager;

    public Task ScheduleAsync(IAsyncPulseChecker checker, TimeSpan interval)
    {
        var cronExpression = _cronConverter.Convert(interval);
        _recurringJobManager.AddOrUpdate(
            checker.GetType().Name,
            () => Task.Run(() => checker.TriggerAsync()),
            cronExpression);

        return Task.CompletedTask;
    }
}
