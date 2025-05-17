using Healthie.Abstractions.Enums;

namespace Healthie.Abstractions.Scheduling;

public interface IPulseScheduler
{
    void Schedule(IPulseChecker checker, PulseInterval interval);

    void Unschedule(IPulseChecker checker);
}
