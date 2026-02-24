using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.BlazorUI.Pulses;

public class CdnAvailabilityPulseChecker(IStateProvider stateProvider)
    : PulseChecker(stateProvider, PulseInterval.Every20Seconds, 5)
{
    private readonly Random _random = new();

    public override string DisplayName => "CDN (CloudFront)";

    public override async Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(_random.Next(30, 120), cancellationToken);

        var roll = _random.Next(0, 100);

        if (roll < 92)
        {
            var cacheHitRate = _random.Next(88, 99);
            return new PulseCheckerResult(
                PulseCheckerHealth.Healthy,
                $"Edge locations responding. Cache hit ratio: {cacheHitRate}%. P99 latency: {_random.Next(8, 45)}ms.");
        }

        if (roll < 98)
        {
            return new PulseCheckerResult(
                PulseCheckerHealth.Suspicious,
                $"Cache hit ratio dropped to {_random.Next(40, 65)}%. Origin requests spiking. Possible cache invalidation storm.");
        }

        return new PulseCheckerResult(
            PulseCheckerHealth.Unhealthy,
            "Distribution d1234abcdef.cloudfront.net returning 502. Origin unreachable from edge locations.");
    }
}
