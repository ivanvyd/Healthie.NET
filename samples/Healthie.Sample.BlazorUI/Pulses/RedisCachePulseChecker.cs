using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.BlazorUI.Pulses;

public class RedisCachePulseChecker : PulseChecker
{
    private readonly Random _random = new();

    public RedisCachePulseChecker(IStateProvider stateProvider)
        : base(stateProvider, PulseInterval.Every5Seconds, 5)
    {
    }

    public override string DisplayName => "Redis Cache";

    public override async Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(_random.Next(10, 80), cancellationToken);

        var roll = _random.Next(0, 100);

        if (roll < 88)
        {
            var memoryUsage = _random.Next(30, 65);
            var hitRate = _random.Next(92, 99);
            return new PulseCheckerResult(
                PulseCheckerHealth.Healthy,
                $"PING OK. Memory: {memoryUsage}%, Hit rate: {hitRate}%, Keys: {_random.Next(12000, 45000):N0}");
        }

        if (roll < 96)
        {
            var memoryUsage = _random.Next(80, 95);
            return new PulseCheckerResult(
                PulseCheckerHealth.Suspicious,
                $"Memory pressure detected: {memoryUsage}% used. Eviction policy may trigger soon.");
        }

        return new PulseCheckerResult(
            PulseCheckerHealth.Unhealthy,
            "PING failed. Connection refused on redis-primary:6379. Failover in progress.");
    }
}
