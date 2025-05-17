using Healthie.Abstractions.Enums;

namespace Healthie.Abstractions.Scheduling;

public interface IAsyncPulseScheduler
{
    Task ScheduleAsync(IAsyncPulseChecker checker, PulseInterval interval);

    Task UnscheduleAsync(IAsyncPulseChecker checker);
}
