using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.BlazorUI.Pulses;

public class SomeDefaultPulseChecker : PulseChecker
{
    private readonly Random _random = new();

    public SomeDefaultPulseChecker(IStateProvider stateProvider)
        : base(stateProvider, PulseInterval.Every10Seconds, 4)
    {
    }

    public override string DisplayName => "Default Health Check";

    public override Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        bool isSuccess = _random.Next(0, 10) >= 4; // 60% chance of success

        if (isSuccess)
        {
            return Task.FromResult(new PulseCheckerResult(
                Health: PulseCheckerHealth.Healthy,
                Message: $"SomeDefaultPulseChecker is healthy at {DateTime.UtcNow}"));
        }
        else
        {
            return Task.FromResult(new PulseCheckerResult(
                Health: PulseCheckerHealth.Suspicious,
                Message: $"SomeDefaultPulseChecker is having issues at {DateTime.UtcNow}"));
        }
    }
}
