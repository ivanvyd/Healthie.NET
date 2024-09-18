using Healthie.PulseChecking;

namespace Healthie.Scheduling;

public interface IPulseScheduler
{
    void Schedule(IPulseChecker checker, TimeSpan interval);
}
