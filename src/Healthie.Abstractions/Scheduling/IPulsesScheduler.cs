using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Microsoft.Extensions.Hosting;

namespace Healthie.Abstractions.Scheduling;

/// <summary>
/// Defines a contract for scheduling pulse checks and managing their states.
/// </summary>
public interface IPulsesScheduler : IHostedService
{
    /// <summary>
    /// Retrieves the current states of all pulse checkers.
    /// </summary>
    /// <returns>A dictionary containing the names of pulse checkers and their states.</returns>
    Dictionary<string, PulseCheckerState> GetPulsesStates();

    /// <summary>
    /// Retrieves all pulse checkers.
    /// </summary>
    /// <returns>A dictionary containing the names of pulse checkers and their corresponding instances.</returns>
    Dictionary<string, IPulseChecker> GetPulseCheckers();

    /// <summary>
    /// Sets the interval for a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <param name="interval">The interval to set for the pulse checker.</param>
    void SetInterval(string name, PulseInterval interval);

    /// <summary>
    /// Sets the unhealthy threshold for a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <param name="threshold">The number of consecutive failures needed to consider the pulse checker unhealthy.</param>
    void SetUnhealthyThreshold(string name, uint threshold);

    /// <summary>
    /// Resets a specific pulse checker state to healthy.
    /// </summary>
    /// <param name="name">The name of the pulse checker to reset.</param>
    void Reset(string name);

    /// <summary>
    /// Activates a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker to activate.</param>
    void Activate(string name);

    /// <summary>
    /// Deactivates a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker to deactivate.</param>
    void Deactivate(string name);
}
