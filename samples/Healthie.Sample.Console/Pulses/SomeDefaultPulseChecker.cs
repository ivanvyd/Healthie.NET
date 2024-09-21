using Healthie.PulseChecking;
using Healthie.PulseChecking.Models;
using Healthie.Storage;

namespace Healthie.Sample.Console.Pulses;

public class SomeDefaultPulseChecker(IStateProvider stateProvider) : PulseChecker(stateProvider)
{
    public override Pulse<Result> Check()
    {
        return new Result(true, $"SomeDefaultPulseChecker is healthy at {DateTime.UtcNow}");
    }
}
