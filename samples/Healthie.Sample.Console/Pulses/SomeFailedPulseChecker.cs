using Healthie.Abstractions;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.Console.Pulses;

public class SomeFailedPulseChecker(IStateProvider stateProvider) : PulseChecker(stateProvider)
{
    public override Pulse<Result> Check()
    {
        return new NotImplementedException($"SomeFailedPulseChecker is not implemented at {DateTime.UtcNow}");
    }
}
