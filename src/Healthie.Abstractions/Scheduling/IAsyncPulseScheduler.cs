using Healthie.Abstractions.Models;

namespace Healthie.Abstractions.Scheduling;

public interface IAsyncPulseScheduler
{
    Task ScheduleAsync(IAsyncPulseChecker checker, PulseInterval interval);
}
