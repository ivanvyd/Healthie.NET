using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.WebApi.Pulses;

public class SomeAsyncFailedPulseChecker(IAsyncStateProvider stateProvider) : AsyncPulseChecker(stateProvider)
{
    public override Task<PulseCheckerResult> CheckAsync()
    {
        return Task.FromResult(new PulseCheckerResult(
           health: PulseCheckerHealth.Unhealthy, 
            message: $"SomeFailedPulseChecker is not implemented at {DateTime.UtcNow}"));
    }
}
