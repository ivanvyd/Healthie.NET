using Healthie.Abstractions;

namespace Healthie.Abstractions.Scheduling;

public interface IAsyncPulseScheduler
{
    Task ScheduleAsync(IAsyncPulseChecker checker, TimeSpan interval);
}
