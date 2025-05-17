using Hangfire;
using Healthie.Abstractions;
using Healthie.Abstractions.Scheduling;
using Healthie.Scheduling.Hangfire.Converters;

namespace Healthie.Scheduling.Hangfire;

public class HangfirePulseScheduler(ICronConverter cronConverter, IRecurringJobManager recurringJobManager)
    : IPulseScheduler
{
    private readonly ICronConverter _cronConverter = cronConverter;
    private readonly IRecurringJobManager _recurringJobManager = recurringJobManager;

    public void Schedule(IPulseChecker checker, TimeSpan interval)
    {
        var cronExpression = _cronConverter.Convert(interval);
        _recurringJobManager.AddOrUpdate(
            checker.GetType().Name,
            () => checker.Trigger(),
            cronExpression);
    }
}
