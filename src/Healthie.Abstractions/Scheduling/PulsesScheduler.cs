using Healthie.Abstractions.Models;
using Microsoft.Extensions.Hosting;

namespace Healthie.Abstractions.Scheduling;

public class PulsesScheduler(IEnumerable<IPulseChecker> pulseCheckers, IPulseScheduler pulseScheduler)
    : BackgroundService, IPulsesScheduler
{
    private readonly IEnumerable<IPulseChecker> _pulseCheckers = pulseCheckers;
    private readonly IPulseScheduler _pulseScheduler = pulseScheduler;

    public Dictionary<string, IPulseChecker> GetPulseCheckers()
    {
        return _pulseCheckers.ToDictionary(pulseChecker => pulseChecker.Name, _ => _);
    }

    public Dictionary<string, State> GetPulsesStates()
    {
        return _pulseCheckers.ToDictionary(checker => checker.Name, checker => checker.GetState());
    }

    public void SetInterval(string name, PulseInterval interval)
    {
        var pulseChecker = _pulseCheckers.FirstOrDefault(checker => checker.Name == name);
        if (pulseChecker is null)
        {
            throw new ArgumentException($"Pulse checker with name {name} not found.");
        }

        pulseChecker.SetInterval(interval);

        Schedule(pulseChecker);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var checker in _pulseCheckers)
        {
            Schedule(checker);
        }

        return Task.CompletedTask;
    }

    private void Schedule(IPulseChecker checker)
    {
        State state = checker.GetState();

        if (state.LastExecutionDateTime is null)
        {
            checker.Trigger();
        }

        _pulseScheduler.Schedule(checker, state.Interval);
    }
}
