using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.BlazorUI.Pulses;

public class IdentityProviderPulseChecker(IStateProvider stateProvider)
    : PulseChecker(stateProvider, PulseInterval.Every15Seconds, 2)
{
    private readonly Random _random = new();

    public override string DisplayName => "Identity Provider (Azure AD)";

    public override async Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(_random.Next(80, 250), cancellationToken);

        var roll = _random.Next(0, 100);

        if (roll < 90)
        {
            return new PulseCheckerResult(
                PulseCheckerHealth.Healthy,
                $"OIDC discovery endpoint reachable. Token validation OK. JWKS refreshed {_random.Next(1, 30)}min ago.");
        }

        if (roll < 97)
        {
            return new PulseCheckerResult(
                PulseCheckerHealth.Suspicious,
                $"Token endpoint latency elevated: {_random.Next(1500, 3000)}ms. JWKS cache age: {_random.Next(50, 90)}min.");
        }

        return new PulseCheckerResult(
            PulseCheckerHealth.Unhealthy,
            "OIDC discovery failed: HTTP 503 from login.microsoftonline.com. Users cannot authenticate.");
    }
}
