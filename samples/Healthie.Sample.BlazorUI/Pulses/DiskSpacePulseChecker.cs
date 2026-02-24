using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.BlazorUI.Pulses;

public class DiskSpacePulseChecker : PulseChecker
{
    private readonly Random _random = new();

    public DiskSpacePulseChecker(IStateProvider stateProvider)
        : base(stateProvider, PulseInterval.EveryMinute, 2)
    {
    }

    public override string DisplayName => "Disk Space (Volume /data)";

    public override Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var usedPercent = _random.Next(55, 97);

        if (usedPercent < 75)
        {
            var freeGb = (100 - usedPercent) * 5.12;
            return Task.FromResult(new PulseCheckerResult(
                PulseCheckerHealth.Healthy,
                $"Volume /data: {usedPercent}% used, {freeGb:F1} GB free of 512 GB."));
        }

        if (usedPercent < 90)
        {
            var freeGb = (100 - usedPercent) * 5.12;
            return Task.FromResult(new PulseCheckerResult(
                PulseCheckerHealth.Suspicious,
                $"Volume /data: {usedPercent}% used, only {freeGb:F1} GB free. Consider cleanup or expansion."));
        }

        var freeGbCritical = (100 - usedPercent) * 5.12;
        return Task.FromResult(new PulseCheckerResult(
            PulseCheckerHealth.Unhealthy,
            $"CRITICAL: Volume /data: {usedPercent}% used, {freeGbCritical:F1} GB free. Risk of write failures."));
    }
}
