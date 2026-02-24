using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.BlazorUI.Pulses;

public class DatabaseConnectionPulseChecker : PulseChecker
{
    private readonly Random _random = new();

    public DatabaseConnectionPulseChecker(IStateProvider stateProvider)
        : base(stateProvider, PulseInterval.Every10Seconds, 3)
    {
    }

    public override string DisplayName => "SQL Database";

    public override async Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(_random.Next(50, 300), cancellationToken);

        var roll = _random.Next(0, 100);

        if (roll < 80)
        {
            var latency = _random.Next(5, 45);
            return new PulseCheckerResult(
                PulseCheckerHealth.Healthy,
                $"Connection pool active. Latency: {latency}ms, Open connections: {_random.Next(2, 15)}/100");
        }

        if (roll < 95)
        {
            var latency = _random.Next(800, 2500);
            return new PulseCheckerResult(
                PulseCheckerHealth.Suspicious,
                $"High latency detected: {latency}ms. Connection pool usage: {_random.Next(75, 95)}%");
        }

        return new PulseCheckerResult(
            PulseCheckerHealth.Unhealthy,
            "Connection timeout after 5000ms. All retry attempts exhausted.");
    }
}
