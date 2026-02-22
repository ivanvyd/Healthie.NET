using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.Console.Pulses;

public class SomeDefaultPulseChecker(IStateProvider stateProvider)
    : PulseChecker(stateProvider)
{
    public override Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PulseCheckerResult(
            Health: PulseCheckerHealth.Healthy,
            Message: $"SomeDefaultPulseChecker is healthy at {DateTime.UtcNow}"));
    }
}
