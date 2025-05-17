using Healthie.Abstractions;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.Console.Pulses;

public class SomeAsyncFailedPulseChecker(IAsyncStateProvider stateProvider) : AsyncPulseChecker(stateProvider)
{
    public override Task<PulseCheckerResult> CheckAsync()
    {
        return Task.FromResult(new PulseCheckerResult(
           isHealthy: false, 
            message: $"SomeFailedPulseChecker is not implemented at {DateTime.UtcNow}"));
    }
}
