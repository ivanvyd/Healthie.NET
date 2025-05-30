using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.WebApi.Pulses;

public class SomeDefaultPulseChecker : PulseChecker
{
    public SomeDefaultPulseChecker(IStateProvider stateProvider) 
        : base(stateProvider, PulseInterval.Every5Seconds) // Set initial interval to Every5Seconds
    {
    }

    public override PulseCheckerResult Check()
    {
        return new PulseCheckerResult(
            health: PulseCheckerHealth.Healthy,
            message: $"SomeDefaultPulseChecker is healthy at {DateTime.UtcNow}");
    }
}
