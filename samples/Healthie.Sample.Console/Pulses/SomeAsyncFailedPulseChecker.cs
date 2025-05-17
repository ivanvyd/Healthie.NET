using Healthie.Abstractions;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.Console.Pulses;

public class SomeAsyncFailedPulseChecker(IAsyncStateProvider stateProvider) : AsyncPulseChecker(stateProvider)
{
    public override async Task<Pulse<Result>> CheckAsync()
    {
        return new NotImplementedException($"SomeFailedPulseChecker is not implemented at {DateTime.UtcNow}");
    }
}
