using Healthie.PulseChecking;

namespace Healthie.Scheduling;

public interface IAsyncPulseScheduler
{
    Task ScheduleAsync(IAsyncPulseChecker checker, TimeSpan interval);
}
