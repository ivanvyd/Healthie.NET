using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.WebApi.Pulses;

public class SomeAsyncDefaultPulseChecker : AsyncPulseChecker
{
    private readonly Random _random = new Random();

    public SomeAsyncDefaultPulseChecker(IAsyncStateProvider stateProvider)
        : base(stateProvider, PulseInterval.Every10Seconds, 4) // Set initial interval to Every10Seconds and threshold to 4
    {
    }

    public override Task<PulseCheckerResult> CheckAsync()
    {
        // For demonstration purposes, randomly succeed or fail
        bool isSuccess = _random.Next(0, 10) >= 4; // 60% chance of success
        
        if (isSuccess)
        {
            return Task.FromResult(new PulseCheckerResult(
                health: PulseCheckerHealth.Healthy,
                message: $"SomeAsyncDefaultPulseChecker is healthy at {DateTime.UtcNow}"));
        }
        else
        {
            // Return Suspicious, but the AsyncPulseChecker base class will change this to Unhealthy
            // if ConsecutiveFailureCount exceeds the threshold
            return Task.FromResult(new PulseCheckerResult(
                health: PulseCheckerHealth.Suspicious,
                message: $"SomeAsyncDefaultPulseChecker is having issues at {DateTime.UtcNow}"));
        }
    }
}
