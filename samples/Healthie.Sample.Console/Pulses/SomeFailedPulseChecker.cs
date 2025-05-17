using Healthie.Abstractions;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.Console.Pulses;

public class SomeFailedPulseChecker(IStateProvider stateProvider) : PulseChecker(stateProvider)
{
    public override PulseCheckerResult Check()
    {
        throw new NotImplementedException($"SomeFailedPulseChecker is not implemented at {DateTime.UtcNow}");
    }
}
