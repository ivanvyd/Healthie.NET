using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.BlazorUI.Pulses;

public class PaymentGatewayPulseChecker : PulseChecker
{
    private readonly Random _random = new();

    public PaymentGatewayPulseChecker(IStateProvider stateProvider)
        : base(stateProvider, PulseInterval.Every15Seconds, 2)
    {
    }

    public override string DisplayName => "Payment Gateway (Stripe)";

    public override async Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(_random.Next(100, 500), cancellationToken);

        var roll = _random.Next(0, 100);

        if (roll < 70)
        {
            var latency = _random.Next(120, 350);
            return new PulseCheckerResult(
                PulseCheckerHealth.Healthy,
                $"API reachable. Latency: {latency}ms. Webhook endpoint verified.");
        }

        if (roll < 90)
        {
            var latency = _random.Next(2000, 4500);
            return new PulseCheckerResult(
                PulseCheckerHealth.Suspicious,
                $"Degraded performance. Latency: {latency}ms. Stripe status: minor outage reported.");
        }

        return new PulseCheckerResult(
            PulseCheckerHealth.Unhealthy,
            "HTTP 503 from api.stripe.com. Payment processing unavailable.");
    }
}
