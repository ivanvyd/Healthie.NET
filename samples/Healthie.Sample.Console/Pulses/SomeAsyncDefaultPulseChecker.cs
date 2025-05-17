using Healthie.Abstractions;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.Console.Pulses;

public class SomeAsyncDefaultPulseChecker(IAsyncStateProvider stateProvider) : AsyncPulseChecker(stateProvider)
{
    public override async Task<Pulse<Result>> CheckAsync()
    {
        return new Result(true, $"SomeDefaultPulseChecker is healthy at {DateTime.UtcNow}");
    }
}
