using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.BlazorUI.Pulses;

public class BackgroundJobsPulseChecker : PulseChecker
{
    private readonly Random _random = new();

    public BackgroundJobsPulseChecker(IStateProvider stateProvider)
        : base(stateProvider, PulseInterval.Every30Seconds, 3)
    {
    }

    public override string DisplayName => "Background Jobs (Hangfire)";

    public override Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var failedJobs = _random.Next(0, 30);
        var scheduledJobs = _random.Next(5, 200);
        var processingJobs = _random.Next(0, 10);

        if (failedJobs < 3)
        {
            return Task.FromResult(new PulseCheckerResult(
                PulseCheckerHealth.Healthy,
                $"Workers: 4/4 active. Processing: {processingJobs}, Scheduled: {scheduledJobs}, Failed: {failedJobs}. Throughput: {_random.Next(50, 150)}/min."));
        }

        if (failedJobs < 15)
        {
            return Task.FromResult(new PulseCheckerResult(
                PulseCheckerHealth.Suspicious,
                $"Elevated failure rate. Failed: {failedJobs}, Processing: {processingJobs}. Top error: TimeoutException in OrderProcessingJob."));
        }

        return Task.FromResult(new PulseCheckerResult(
            PulseCheckerHealth.Unhealthy,
            $"Job processing stalled. Failed: {failedJobs}, Workers: 1/4 responsive. Retry queue saturated."));
    }
}
