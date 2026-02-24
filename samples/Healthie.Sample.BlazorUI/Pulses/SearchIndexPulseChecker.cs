using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.BlazorUI.Pulses;

public class SearchIndexPulseChecker : PulseChecker
{
    private readonly Random _random = new();

    public SearchIndexPulseChecker(IStateProvider stateProvider)
        : base(stateProvider, PulseInterval.Every20Seconds, 4)
    {
    }

    public override string DisplayName => "Elasticsearch Cluster";

    public override async Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(_random.Next(30, 200), cancellationToken);

        var roll = _random.Next(0, 100);

        if (roll < 85)
        {
            var docs = _random.Next(1_000_000, 5_000_000);
            return new PulseCheckerResult(
                PulseCheckerHealth.Healthy,
                $"Cluster status: green. Nodes: 3/3. Indices: 12, Docs: {docs:N0}. Avg query: {_random.Next(3, 25)}ms.");
        }

        if (roll < 95)
        {
            return new PulseCheckerResult(
                PulseCheckerHealth.Suspicious,
                $"Cluster status: yellow. 1 replica shard unassigned. Node es-node-02 high CPU: {_random.Next(80, 98)}%.");
        }

        return new PulseCheckerResult(
            PulseCheckerHealth.Unhealthy,
            "Cluster status: red. Primary shard [products-v2][0] unassigned. Index read-only. Search degraded.");
    }
}
