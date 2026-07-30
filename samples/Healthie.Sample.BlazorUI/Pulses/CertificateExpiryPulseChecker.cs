using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.BlazorUI.Pulses;

public class CertificateExpiryPulseChecker : PulseChecker
{
    private readonly Random _random = new();

    // On a cron expression rather than an interval, because that is what a certificate check
    // actually wants and because it is the case the board has to render: no rate a minute, and an
    // interval picker that must not pretend to apply. Every two minutes here so the sample shows it
    // running rather than waiting until 03:20.
    public CertificateExpiryPulseChecker(IStateProvider stateProvider)
        : base(stateProvider, PulseSchedule.Cron("*/2 * * * *"), 1)
    {
    }

    public override string DisplayName => "TLS Certificate";

    public override string DefaultGroup => "Infrastructure";

    public override IReadOnlyList<string> DefaultTags => ["security"];


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
