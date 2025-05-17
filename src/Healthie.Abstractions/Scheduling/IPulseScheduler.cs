using Healthie.Abstractions;

namespace Healthie.Abstractions.Scheduling;

public interface IPulseScheduler
{
    void Schedule(IPulseChecker checker, TimeSpan interval);
}
