using Healthie.Abstractions.Models;
using Microsoft.Extensions.Hosting;

namespace Healthie.Abstractions.Scheduling;

public class AsyncPulsesScheduler(IEnumerable<IAsyncPulseChecker> pulseCheckers, IAsyncPulseScheduler pulseScheduler)
    : BackgroundService, IAsyncPulsesScheduler
{
    private readonly IEnumerable<IAsyncPulseChecker> _pulseCheckers = pulseCheckers;
    private readonly IAsyncPulseScheduler _pulseScheduler = pulseScheduler;

    public Task<Dictionary<string, IAsyncPulseChecker>> GetPulseCheckersAsync()
    {
        return Task.FromResult(_pulseCheckers.ToDictionary(pulseChecker => pulseChecker.Name, _ => _));
    }

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

    public async Task SetIntervalAsync(string name, PulseInterval interval)
    {
        var pulseChecker = _pulseCheckers.FirstOrDefault(checker => checker.Name == name);
        if (pulseChecker is null)
        {
            throw new ArgumentException($"Pulse checker with name {name} not found.");
        }

        await pulseChecker.SetIntervalAsync(interval);

        await ScheduleAsync(pulseChecker);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(_pulseCheckers.Select(ScheduleAsync));
    }

    private async Task ScheduleAsync(IAsyncPulseChecker checker)
    {
        var state = await checker.GetStateAsync();

        if (state.LastExecutionDateTime is null)
        {
            await checker.TriggerAsync();
        }

        await _pulseScheduler.ScheduleAsync(checker, state.Interval);
    }
}
