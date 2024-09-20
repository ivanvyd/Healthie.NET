using Healthie.PulseChecking;
using Healthie.PulseChecking.Models;
using Microsoft.Extensions.Hosting;

namespace Healthie.Scheduling;

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
        TimeSpan interval = TimeSpan.FromMinutes(1);

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
