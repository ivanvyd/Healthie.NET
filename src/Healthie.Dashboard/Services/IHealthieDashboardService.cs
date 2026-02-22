using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;

namespace Healthie.Dashboard.Services;

/// <summary>
/// Internal service used by dashboard components to interact with the pulse checking backend.
/// </summary>
internal interface IHealthieDashboardService : IAsyncDisposable
{
    /// <summary>
    /// Retrieves the states of all registered pulse checkers.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A dictionary of pulse checker names and their states.</returns>
    Task<Dictionary<string, PulseCheckerState>> GetAllStatesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all registered pulse checker instances.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A dictionary of pulse checker names and their instances.</returns>
    Task<Dictionary<string, IPulseChecker>> GetAllCheckersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the interval for a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <param name="interval">The interval to set.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task SetIntervalAsync(string name, PulseInterval interval,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the unhealthy threshold for a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <param name="threshold">The number of consecutive failures before unhealthy.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task SetThresholdAsync(string name, uint threshold,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task StartAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task StopAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers a specific pulse checker immediately.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task TriggerAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets a specific pulse checker state to healthy.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task ResetAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the trigger history for a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A list of history entries, most recent last.</returns>
    Task<List<PulseCheckerHistoryEntry>> GetHistoryAsync(string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all trigger history entries for a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task ClearHistoryAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates all registered pulse checkers.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task StartAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates all registered pulse checkers.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task StopAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables history recording for a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <param name="enabled">Whether history recording should be enabled.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task SetHistoryEnabledAsync(string name, bool enabled,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the display names for all registered pulse checkers.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A dictionary mapping pulse checker names to their display names.</returns>
    Task<Dictionary<string, string>> GetDisplayNamesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to state change notifications from all registered pulse checkers.
    /// </summary>
    /// <param name="onStateChanged">
    /// The callback to invoke when a checker's state changes.
    /// The first parameter is the checker name; the second is the new state.
    /// </param>
    void SubscribeToStateChanges(Action<string, PulseCheckerState> onStateChanged);
}
