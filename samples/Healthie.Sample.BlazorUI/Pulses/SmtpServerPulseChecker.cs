using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.BlazorUI.Pulses;

public class SmtpServerPulseChecker(IStateProvider stateProvider)
    : PulseChecker(stateProvider, PulseInterval.Every30Seconds, 3)
{
    private readonly Random _random = new();

    public override string DisplayName => "SMTP Mail Server";

    public override async Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(_random.Next(100, 400), cancellationToken);

        var roll = _random.Next(0, 100);

        if (roll < 75)
        {
            return new PulseCheckerResult(
                PulseCheckerHealth.Healthy,
                $"EHLO accepted by smtp.sendgrid.net:587. TLS 1.3 handshake OK. Queue: {_random.Next(0, 50)} pending.");
        }

        if (roll < 92)
        {
            return new PulseCheckerResult(
                PulseCheckerHealth.Suspicious,
                $"SMTP response delayed: {_random.Next(3, 8)}s. Bounce rate elevated: {_random.Next(5, 15)}%.");
        }

        return new PulseCheckerResult(
            PulseCheckerHealth.Unhealthy,
            "SMTP connection refused. Error: 454 TLS not available. Emails will not be delivered.");
    }
}
