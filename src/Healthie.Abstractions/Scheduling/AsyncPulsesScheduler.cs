using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Microsoft.Extensions.Hosting;

namespace Healthie.Abstractions.Scheduling;

/// <summary>
/// Schedules all registered asynchronous pulse checkers.
/// </summary>
public class AsyncPulsesScheduler : BackgroundService, IAsyncPulsesScheduler
{
    private readonly IEnumerable<IAsyncPulseChecker> _pulseCheckers;
    private readonly IAsyncPulseScheduler _pulseScheduler;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncPulsesScheduler"/> class.
    /// </summary>
    /// <param name="pulseCheckers">The collection of asynchronous pulse checkers to schedule.</param>
    /// <param name="pulseScheduler">The scheduler responsible for individual asynchronous pulse checks.</param>
    public AsyncPulsesScheduler(IEnumerable<IAsyncPulseChecker> pulseCheckers, IAsyncPulseScheduler pulseScheduler)
    {
        _pulseCheckers = pulseCheckers;
        _pulseScheduler = pulseScheduler;
    }

    /// <summary>
    /// Gets all registered pulse checkers.
    /// </summary>
    /// <returns>A dictionary containing the names and instances of all pulse checkers.</returns>
    public Task<Dictionary<string, IAsyncPulseChecker>> GetPulseCheckersAsync()
    {
        return Task.FromResult(_pulseCheckers.ToDictionary(pulseChecker => pulseChecker.Name, _ => _));
    }

    /// <summary>
    /// Gets the states of all registered pulse checkers.
    /// </summary>
    /// <returns>A dictionary containing the names and states of all pulse checkers.</returns>
    public async Task<Dictionary<string, PulseCheckerState>> GetPulsesStatesAsync()
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

    /// <summary>
    /// Sets the interval for a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <param name="interval">The interval to set.</param>
    /// <exception cref="ArgumentException">Thrown when the pulse checker with the specified name is not found.</exception>
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

    /// <summary>
    /// Sets the unhealthy threshold for a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <param name="threshold">The number of consecutive failures needed to consider the pulse checker unhealthy.</param>
    /// <exception cref="ArgumentException">Thrown when the pulse checker with the specified name is not found.</exception>
    public async Task SetUnhealthyThresholdAsync(string name, uint threshold)
    {
        var pulseChecker = _pulseCheckers.FirstOrDefault(checker => checker.Name == name);
        if (pulseChecker is null)
        {
            throw new ArgumentException($"Pulse checker with name {name} not found.");
        }

        await pulseChecker.SetUnhealthyThresholdAsync(threshold);
    }

    /// <summary>
    /// Resets a specific pulse checker state to healthy.
    /// </summary>
    /// <param name="name">The name of the pulse checker to reset.</param>
    /// <exception cref="ArgumentException">Thrown when the pulse checker with the specified name is not found.</exception>
    public async Task ResetAsync(string name)
    {
        var pulseChecker = _pulseCheckers.FirstOrDefault(checker => checker.Name == name);
        if (pulseChecker is null)
        {
            throw new ArgumentException($"Pulse checker with name {name} not found.");
        }

        await pulseChecker.ResetAsync();
    }

    /// <summary>
    /// Activates a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <exception cref="ArgumentException">Thrown when the pulse checker with the specified name is not found.</exception>
    public async Task ActivateAsync(string name)
    {
        var pulseChecker = _pulseCheckers.FirstOrDefault(checker => checker.Name == name);
        if (pulseChecker is null)
        {
            throw new ArgumentException($"Pulse checker with name {name} not found.");
        }

        bool isStarted = await pulseChecker.StartAsync();

        if (isStarted)
        {
            await ScheduleAsync(pulseChecker);
        }
    }

    /// <summary>
    /// Deactivates a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <exception cref="ArgumentException">Thrown when the pulse checker with the specified name is not found.</exception>
    public async Task DeactivateAsync(string name)
    {
        var pulseChecker = _pulseCheckers.FirstOrDefault(checker => checker.Name == name);
        if (pulseChecker is null)
        {
            throw new ArgumentException($"Pulse checker with name {name} not found.");
        }

        bool isStopped = await pulseChecker.StopAsync();

        if (isStopped)
        {
            await _pulseScheduler.UnscheduleAsync(pulseChecker);
        }
    }

    /// <summary>
    /// Executes the scheduling of all pulse checkers.
    /// </summary>
    /// <param name="stoppingToken">A token to signal stopping the operation.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(_pulseCheckers.Select(ScheduleAsync));
    }

    /// <summary>
    /// Schedules a specific pulse checker.
    /// </summary>
    /// <param name="checker">The pulse checker to schedule.</param>
    private async Task ScheduleAsync(IAsyncPulseChecker checker)
    {
        var state = await checker.GetStateAsync();

        if (!state.IsActive)
        {
            return;
        }

        await _pulseScheduler.ScheduleAsync(checker, state.Interval);
    }
}
