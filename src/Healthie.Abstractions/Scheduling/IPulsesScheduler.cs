using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Microsoft.Extensions.Hosting;

namespace Healthie.Abstractions.Scheduling;

/// <summary>
/// Defines a contract for managing and scheduling all registered pulse checkers.
/// </summary>
public interface IPulsesScheduler : IHostedService
{
    /// <summary>
    /// Retrieves the states of all registered pulse checkers.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A dictionary containing the pulse checker names and their corresponding states.</returns>
    Task<Dictionary<string, PulseCheckerState>> GetPulsesStatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all registered pulse checkers.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A dictionary containing the pulse checker names and their corresponding instances.</returns>
    Task<Dictionary<string, IPulseChecker>> GetPulseCheckersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the interval for a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <param name="interval">The interval to set for the pulse checker.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentException">Thrown when no pulse checker with the specified <paramref name="name"/> exists.</exception>
    Task SetIntervalAsync(string name, PulseInterval interval, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the unhealthy threshold for a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <param name="threshold">The number of consecutive failures needed to consider the pulse checker unhealthy.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentException">Thrown when no pulse checker with the specified <paramref name="name"/> exists.</exception>
    Task SetUnhealthyThresholdAsync(string name, uint threshold, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets a specific pulse checker state to healthy.
    /// </summary>
    /// <param name="name">The name of the pulse checker to reset.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentException">Thrown when no pulse checker with the specified <paramref name="name"/> exists.</exception>
    Task ResetAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker to activate.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentException">Thrown when no pulse checker with the specified <paramref name="name"/> exists.</exception>
    Task ActivateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker to deactivate.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentException">Thrown when no pulse checker with the specified <paramref name="name"/> exists.</exception>
    Task DeactivateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the trigger history for a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A list of history entries, most recent last.</returns>
    Task<List<PulseCheckerHistoryEntry>> GetHistoryAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all trigger history entries for a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task ClearHistoryAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables history recording for a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <param name="enabled">Whether history recording should be enabled.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentException">Thrown when no pulse checker with the specified <paramref name="name"/> exists.</exception>
    Task SetHistoryEnabledAsync(string name, bool enabled, CancellationToken cancellationToken = default);

}
