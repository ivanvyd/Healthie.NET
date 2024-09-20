using Healthie.PulseChecking;
using Healthie.PulseChecking.Models;
using Healthie.Storage;

namespace Healthie.Api.Pulses;

public class SomeFailedPulseChecker(IStateProvider stateProvider) : PulseChecker(stateProvider)
{
    public override Pulse<Result> Check()
    {
        return new NotImplementedException($"SomeFailedPulseChecker is not implemented at {DateTime.UtcNow}");
    }
}
