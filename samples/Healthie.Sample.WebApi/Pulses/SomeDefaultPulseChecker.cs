using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.WebApi.Pulses;

public class SomeDefaultPulseChecker : PulseChecker
{
    private readonly Random _random = new Random();

    public SomeDefaultPulseChecker(IStateProvider stateProvider)
        : base(stateProvider, PulseInterval.Every10Seconds, 4) // Set initial interval to Every10Seconds and threshold to 4
    {
    }

    public override Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        // For demonstration purposes, randomly succeed or fail
        bool isSuccess = _random.Next(0, 10) >= 4; // 60% chance of success

        if (isSuccess)
        {
            return Task.FromResult(new PulseCheckerResult(
                Health: PulseCheckerHealth.Healthy,
                Message: $"SomeDefaultPulseChecker is healthy at {DateTime.UtcNow}"));
        }
        else
        {
            // Return Suspicious, but the PulseChecker base class will change this to Unhealthy
            // if ConsecutiveFailureCount exceeds the threshold
            return Task.FromResult(new PulseCheckerResult(
                Health: PulseCheckerHealth.Suspicious,
                Message: $"SomeDefaultPulseChecker is having issues at {DateTime.UtcNow}"));
        }
    }
}
