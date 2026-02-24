using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.BlazorUI.Pulses;

public class StorageBlobPulseChecker(IStateProvider stateProvider)
    : PulseChecker(stateProvider, PulseInterval.Every30Seconds, 3)
{
    private readonly Random _random = new();

    public override string DisplayName => "Azure Blob Storage";

    public override async Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(_random.Next(40, 180), cancellationToken);

        var roll = _random.Next(0, 100);

        if (roll < 93)
        {
            return new PulseCheckerResult(
                PulseCheckerHealth.Healthy,
                $"Container 'uploads' accessible. List/Read/Write OK. Latency: {_random.Next(10, 60)}ms. Objects: {_random.Next(50000, 200000):N0}.");
        }

        if (roll < 98)
        {
            return new PulseCheckerResult(
                PulseCheckerHealth.Suspicious,
                $"Throttling detected: HTTP 429 on 'uploads' container. {_random.Next(2, 8)} requests throttled in last minute.");
        }

        return new PulseCheckerResult(
            PulseCheckerHealth.Unhealthy,
            "Storage account unreachable. DNS resolution failed for contosostorage.blob.core.windows.net.");
    }
}
