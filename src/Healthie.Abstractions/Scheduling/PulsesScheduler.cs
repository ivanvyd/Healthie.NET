using Healthie.Abstractions.Models;
using Microsoft.Extensions.Hosting;

namespace Healthie.Abstractions.Scheduling;

public class PulsesScheduler(IEnumerable<IPulseChecker> pulseCheckers, IPulseScheduler pulseScheduler)
    : BackgroundService, IPulsesScheduler
{
    private readonly IEnumerable<IPulseChecker> _pulseCheckers = pulseCheckers;
    private readonly IPulseScheduler _pulseScheduler = pulseScheduler;

    public Dictionary<string, State> GetPulsesStates()
    {
        return _pulseCheckers.ToDictionary(checker => checker.Name, checker => checker.GetState());
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // TODO: dynamic
        TimeSpan interval = TimeSpan.FromSeconds(5);

        foreach (var checker in _pulseCheckers)
        {
            if (checker.GetState().LastExecutionDateTime is null)
            {
                checker.Trigger();
            }

            _pulseScheduler.Schedule(checker, interval);
        }

        return Task.CompletedTask;
    }
}
