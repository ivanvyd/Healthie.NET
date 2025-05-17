using Hangfire;
using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Extensions;
using Healthie.Abstractions.Scheduling;

namespace Healthie.Scheduling.Hangfire;

public class HangfirePulseScheduler(IRecurringJobManager recurringJobManager)
    : IPulseScheduler
{
    private readonly IRecurringJobManager _recurringJobManager = recurringJobManager;

    public void Schedule(IPulseChecker checker, PulseInterval interval)
    {
        var cronExpression = interval.ToCronExpression();
        _recurringJobManager.AddOrUpdate(
            checker.GetType().Name,
            () => checker.Trigger(),
            cronExpression);
    }

    public void Unschedule(IPulseChecker checker)
    {
        throw new NotImplementedException();
    }
}
