using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Microsoft.Extensions.Hosting;

namespace Healthie.Abstractions.Scheduling;

/// <summary>
/// Schedules all registered synchronous pulse checkers.
/// </summary>
public class PulsesScheduler : BackgroundService, IPulsesScheduler
{
    private readonly IEnumerable<IPulseChecker> _pulseCheckers;
    private readonly IPulseScheduler _pulseScheduler;

    /// <summary>
    /// Initializes a new instance of the <see cref="PulsesScheduler"/> class.
    /// </summary>
    /// <param name="pulseCheckers">The collection of synchronous pulse checkers to schedule.</param>
    /// <param name="pulseScheduler">The scheduler responsible for individual pulse checks.</param>
    public PulsesScheduler(IEnumerable<IPulseChecker> pulseCheckers, IPulseScheduler pulseScheduler)
    {
        _pulseCheckers = pulseCheckers;
        _pulseScheduler = pulseScheduler;
    }

    /// <summary>
    /// Gets all registered pulse checkers.
    /// </summary>
    /// <returns>A dictionary mapping pulse checker names to their instances.</returns>
    public Dictionary<string, IPulseChecker> GetPulseCheckers()
    {
        return _pulseCheckers.ToDictionary(pulseChecker => pulseChecker.Name, _ => _);
    }

    /// <summary>
    /// Gets the states of all registered pulse checkers.
    /// </summary>
    /// <returns>A dictionary mapping pulse checker names to their states.</returns>
    public Dictionary<string, PulseCheckerState> GetPulsesStates()
    {
        return _pulseCheckers.ToDictionary(checker => checker.Name, checker => checker.GetState());
    }

    /// <summary>
    /// Sets the interval for a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <param name="interval">The interval to set.</param>
    /// <exception cref="ArgumentException">Thrown if the pulse checker with the specified name is not found.</exception>
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

    /// <summary>
    /// Sets the unhealthy threshold for a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <param name="threshold">The number of consecutive failures needed to consider the pulse checker unhealthy.</param>
    /// <exception cref="ArgumentException">Thrown if the pulse checker with the specified name is not found.</exception>
    public void SetUnhealthyThreshold(string name, uint threshold)
    {
        var pulseChecker = _pulseCheckers.FirstOrDefault(checker => checker.Name == name);
        if (pulseChecker is null)
        {
            throw new ArgumentException($"Pulse checker with name {name} not found.");
        }

        pulseChecker.SetUnhealthyThreshold(threshold);
    }

    /// <summary>
    /// Resets a specific pulse checker state to healthy.
    /// </summary>
    /// <param name="name">The name of the pulse checker to reset.</param>
    /// <exception cref="ArgumentException">Thrown if the pulse checker with the specified name is not found.</exception>
    public void Reset(string name)
    {
        var pulseChecker = _pulseCheckers.FirstOrDefault(checker => checker.Name == name);
        if (pulseChecker is null)
        {
            throw new ArgumentException($"Pulse checker with name {name} not found.");
        }

        pulseChecker.Reset();
    }

    /// <summary>
    /// Activates a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <exception cref="ArgumentException">Thrown if the pulse checker with the specified name is not found.</exception>
    public void Activate(string name)
    {
        var pulseChecker = _pulseCheckers.FirstOrDefault(checker => checker.Name == name);
        if (pulseChecker is null)
        {
            throw new ArgumentException($"Pulse checker with name {name} not found.");
        }

        bool isStarted = pulseChecker.Start();

        if (isStarted)
        {
            Schedule(pulseChecker);
        }
    }

    /// <summary>
    /// Deactivates a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <exception cref="ArgumentException">Thrown if the pulse checker with the specified name is not found.</exception>
    public void Deactivate(string name)
    {
        var pulseChecker = _pulseCheckers.FirstOrDefault(checker => checker.Name == name);
        if (pulseChecker is null)
        {
            throw new ArgumentException($"Pulse checker with name {name} not found.");
        }

        bool isStopped = pulseChecker.Stop();

        if (isStopped)
        {
            _pulseScheduler.Unschedule(pulseChecker);
        }
    }

    /// <summary>
    /// Executes the scheduling of all pulse checkers.
    /// </summary>
    /// <param name="stoppingToken">A token to signal the operation should be stopped.</param>
    /// <returns>A completed task.</returns>
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var checker in _pulseCheckers)
        {
            Schedule(checker);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Schedules a specific pulse checker.
    /// </summary>
    /// <param name="checker">The pulse checker to schedule.</param>
    private void Schedule(IPulseChecker checker)
    {
        PulseCheckerState state = checker.GetState();

        if (!state.IsActive)
        {
            return;
        }

        _pulseScheduler.Schedule(checker, state.Interval);
    }
}
