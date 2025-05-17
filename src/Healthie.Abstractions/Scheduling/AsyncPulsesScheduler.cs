using Healthie.Abstractions.Models;
using Microsoft.Extensions.Hosting;

namespace Healthie.Abstractions.Scheduling;

public class AsyncPulsesScheduler(IEnumerable<IAsyncPulseChecker> pulseCheckers, IAsyncPulseScheduler pulseScheduler)
    : BackgroundService, IAsyncPulsesScheduler
{
    private readonly IEnumerable<IAsyncPulseChecker> _pulseCheckers = pulseCheckers;
    private readonly IAsyncPulseScheduler _pulseScheduler = pulseScheduler;

    public async Task<Dictionary<string, State>> GetPulsesStatesAsync()
    {
        var pulsesStates = await Task.WhenAll(_pulseCheckers.Select(async checker =>
        {
            var state = await checker.GetStateAsync();

            return new
            {
                checker.Name,
                State = state,
            };
        }));

        return pulsesStates.ToDictionary(checker => checker.Name, checker => checker.State);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // TODO: dynamic
        TimeSpan interval = TimeSpan.FromSeconds(5);

        await Task.WhenAll(_pulseCheckers.Select(async checker =>
        {
            var state = await checker.GetStateAsync();

            if (state is { LastExecutionDateTime: null })
            {
                await checker.TriggerAsync();
            }

            await _pulseScheduler.ScheduleAsync(checker, interval);
        }));
    }
}
