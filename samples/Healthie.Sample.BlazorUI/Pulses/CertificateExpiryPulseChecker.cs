using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.BlazorUI.Pulses;

public class CertificateExpiryPulseChecker : PulseChecker
{
    private readonly Random _random = new();

    public CertificateExpiryPulseChecker(IStateProvider stateProvider)
        : base(stateProvider, PulseInterval.Every2Minutes, 1)
    {
    }

    public override string DisplayName => "TLS Certificate";

    public override Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var daysUntilExpiry = _random.Next(-2, 120);

        if (daysUntilExpiry > 30)
        {
            return Task.FromResult(new PulseCheckerResult(
                PulseCheckerHealth.Healthy,
                $"Certificate *.app.contoso.com valid. Expires in {daysUntilExpiry} days (SHA-256, Let's Encrypt R3)."));
        }

        if (daysUntilExpiry > 0)
        {
            return Task.FromResult(new PulseCheckerResult(
                PulseCheckerHealth.Suspicious,
                $"Certificate expiring soon: {daysUntilExpiry} day(s) remaining. Auto-renewal scheduled but not confirmed."));
        }

        return Task.FromResult(new PulseCheckerResult(
            PulseCheckerHealth.Unhealthy,
            $"EXPIRED: Certificate expired {Math.Abs(daysUntilExpiry)} day(s) ago. Browsers will show security warnings."));
    }
}
