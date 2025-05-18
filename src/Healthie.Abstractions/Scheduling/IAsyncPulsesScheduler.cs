using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Microsoft.Extensions.Hosting;

namespace Healthie.Abstractions.Scheduling;

/// <summary>
/// Defines a contract for scheduling asynchronous pulse checks.
/// </summary>
public interface IAsyncPulsesScheduler : IHostedService
{
    /// <summary>
    /// Retrieves the states of all pulses.
    /// </summary>
    /// <returns>A dictionary containing the pulse names and their corresponding states.</returns>
    Task<Dictionary<string, PulseCheckerState>> GetPulsesStatesAsync();

    /// <summary>
    /// Retrieves all pulse checkers.
    /// </summary>
    /// <returns>A dictionary containing the pulse names and their corresponding pulse checkers.</returns>
    Task<Dictionary<string, IAsyncPulseChecker>> GetPulseCheckersAsync();

    /// <summary>
    /// Sets the interval for a specific pulse.
    /// </summary>
    /// <param name="name">The name of the pulse.</param>
    /// <param name="interval">The interval to set for the pulse.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task SetIntervalAsync(string name, PulseInterval interval);

    /// <summary>
    /// Activates a specific pulse.
    /// </summary>
    /// <param name="name">The name of the pulse to activate.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task ActivateAsync(string name);

    /// <summary>
    /// Deactivates a specific pulse.
    /// </summary>
    /// <param name="name">The name of the pulse to deactivate.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task DeactivateAsync(string name);
}
