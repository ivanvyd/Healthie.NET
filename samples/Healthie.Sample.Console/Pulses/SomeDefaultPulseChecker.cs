using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.Console.Pulses;

public class SomeDefaultPulseChecker(IStateProvider stateProvider) : PulseChecker(stateProvider)
{
    public override PulseCheckerResult Check()
    {
        return new PulseCheckerResult(
            health: PulseCheckerHealth.Healthy,
            message: $"SomeDefaultPulseChecker is healthy at {DateTime.UtcNow}");
    }
}
