using Healthie.Abstractions;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.Console.Pulses;

public class SomeAsyncDefaultPulseChecker(IAsyncStateProvider stateProvider)
    : AsyncPulseChecker(stateProvider)
{
    public override Task<PulseCheckerResult> CheckAsync()
    {
        return Task.FromResult(new PulseCheckerResult(
            isHealthy: true,
            message: $"SomeDefaultPulseChecker is healthy at {DateTime.UtcNow}"));
    }
}
