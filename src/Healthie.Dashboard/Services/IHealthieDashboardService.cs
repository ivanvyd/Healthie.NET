using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;

namespace Healthie.Dashboard.Services;

/// <summary>
/// Internal service used by dashboard components to interact with the pulse checking backend.
/// </summary>
internal interface IHealthieDashboardService : IAsyncDisposable
{
    /// <summary>
    /// Subscribes to state changes on every registered pulse checker.
    /// </summary>
    /// <param name="onStateChanged">
    /// Invoked with the checker's name and its new state whenever a checker's state changes.
    /// </param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <remarks>
    /// Subscribing is what starts the flow of updates, so this both registers the handler and
    /// attaches to the checkers. Handlers are released when the service is disposed, which happens
    /// when the circuit ends.
    /// </remarks>
    Task SubscribeToStateChangesAsync(
        Func<string, PulseCheckerState, Task> onStateChanged,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers every registered pulse checker.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task TriggerAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the states of all registered pulse checkers.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A dictionary of pulse checker names and their states.</returns>
    Task<Dictionary<string, PulseCheckerState>> GetAllStatesAsync(
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
    /// Retrieves the display names for all registered pulse checkers.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A dictionary mapping pulse checker names to their display names.</returns>
    Task<Dictionary<string, string>> GetDisplayNamesAsync(
        CancellationToken cancellationToken = default);

}
