using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.BlazorUI.Pulses;

public class MemoryUsagePulseChecker : PulseChecker
{
    private readonly Random _random = new();

    public MemoryUsagePulseChecker(IStateProvider stateProvider)
        : base(stateProvider, PulseInterval.Every10Seconds, 4)
    {
    }

    public override string DisplayName => "Application Memory";

    public override Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var heapMb = _random.Next(180, 750);
        var gen2Collections = _random.Next(0, 12);

        if (heapMb < 450)
        {
            return Task.FromResult(new PulseCheckerResult(
                PulseCheckerHealth.Healthy,
                $"Heap: {heapMb} MB / 1024 MB. GC Gen2 collections: {gen2Collections}. No memory pressure."));
        }

        if (heapMb < 650)
        {
            return Task.FromResult(new PulseCheckerResult(
                PulseCheckerHealth.Suspicious,
                $"Elevated heap usage: {heapMb} MB / 1024 MB. GC Gen2: {gen2Collections}. Possible memory leak in request pipeline."));
        }

        return Task.FromResult(new PulseCheckerResult(
            PulseCheckerHealth.Unhealthy,
            $"CRITICAL: Heap at {heapMb} MB / 1024 MB ({heapMb * 100 / 1024}%). GC under heavy pressure. OOM risk imminent."));
    }
}
