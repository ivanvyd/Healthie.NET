using Healthie.Abstractions.Models;

namespace Healthie.Abstractions.Scheduling;

public interface IPulseScheduler
{
    void Schedule(IPulseChecker checker, PulseInterval interval);
}
