using Hangfire;
using Healthie.Hangfire.Converters;
using Healthie.PulseChecking;
using Healthie.Scheduling;

namespace Healthie.Hangfire;

public class HangfirePulseScheduler(ICronConverter cronConverter, IRecurringJobManager recurringJobManager)
    : IPulseScheduler
{
    private readonly ICronConverter _cronConverter = cronConverter
        ?? throw new ArgumentNullException(nameof(cronConverter));

    private readonly IRecurringJobManager _recurringJobManager = recurringJobManager
        ?? throw new ArgumentNullException(nameof(recurringJobManager));

    public void Schedule(IPulseChecker checker, TimeSpan interval)
    {
        var cronExpression = _cronConverter.Convert(interval);
        _recurringJobManager.AddOrUpdate(
            checker.GetType().Name,
            () => checker.Trigger(),
            cronExpression);
    }
}
