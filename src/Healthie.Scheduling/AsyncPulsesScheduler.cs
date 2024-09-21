using Healthie.PulseChecking;
using Healthie.PulseChecking.Models;
using Microsoft.Extensions.Hosting;

namespace Healthie.Scheduling;

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
        TimeSpan interval = TimeSpan.FromMinutes(1);

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
