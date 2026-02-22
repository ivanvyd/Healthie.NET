using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.BlazorUI.Pulses;

public class SomeFailedPulseChecker(IStateProvider stateProvider) : PulseChecker(stateProvider)
{
    public override Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PulseCheckerResult(
           Health: PulseCheckerHealth.Unhealthy,
            Message: $"SomeFailedPulseChecker is not implemented at {DateTime.UtcNow}"));
    }
}
