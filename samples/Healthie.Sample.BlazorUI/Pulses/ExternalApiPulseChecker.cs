using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.BlazorUI.Pulses;

public class ExternalApiPulseChecker : PulseChecker
{
    private readonly Random _random = new();

    public ExternalApiPulseChecker(IStateProvider stateProvider)
        : base(stateProvider, PulseInterval.Every30Seconds, 3)
    {
    }

    public override string DisplayName => "Partner REST API";

    public override async Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(_random.Next(200, 800), cancellationToken);

        var roll = _random.Next(0, 100);

        if (roll < 60)
        {
            var latency = _random.Next(150, 400);
            return new PulseCheckerResult(
                PulseCheckerHealth.Healthy,
                $"GET /health returned 200 OK in {latency}ms. Rate limit: {_random.Next(700, 950)}/1000 remaining.");
        }

        if (roll < 85)
        {
            return new PulseCheckerResult(
                PulseCheckerHealth.Suspicious,
                $"HTTP 429 Too Many Requests. Rate limit exceeded. Retry-After: {_random.Next(10, 60)}s.");
        }

        return new PulseCheckerResult(
            PulseCheckerHealth.Unhealthy,
            "HTTP 500 Internal Server Error. Response body: {\"error\":\"upstream_timeout\"}");
    }
}
