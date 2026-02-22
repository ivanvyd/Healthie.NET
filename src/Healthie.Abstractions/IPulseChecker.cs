using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;

namespace Healthie.Abstractions;

/// <summary>
/// Defines a contract for a pulse checker that monitors the health of a component or service.
/// </summary>
/// <remarks>
/// Implementations should derive from <see cref="PulseChecker"/> rather than implementing this interface directly.
/// </remarks>
public interface IPulseChecker : IPulse, IState, IAsyncDisposable
{
    /// <summary>
    /// Occurs when the state of the pulse checker changes.
    /// </summary>
    event EventHandler<PulseCheckerStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Gets the unique name identifying this pulse checker.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the display name shown in the dashboard UI.
    /// </summary>
    /// <remarks>
    /// Override this property to provide a custom, human-friendly name.
    /// Defaults to <see cref="Name"/> (the full type name).
    /// </remarks>
    string DisplayName { get; }

    /// <summary>
    /// Performs the pulse check asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous pulse check operation. The task result contains the <see cref="PulseCheckerResult"/>.</returns>
    Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the interval for the pulse check asynchronously.
    /// </summary>
    /// <param name="interval">The interval to set for the pulse check.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task SetIntervalAsync(PulseInterval interval, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the unhealthy threshold for the pulse checker asynchronously.
    /// </summary>
    /// <param name="threshold">The number of consecutive failures needed to consider the pulse checker unhealthy.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task SetUnhealthyThresholdAsync(uint threshold, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the pulse checker state to healthy asynchronously.
    /// </summary>
    /// <remarks>
    /// This resets the consecutive failures count, sets the health status to Healthy, and clears any error messages.
    /// </remarks>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task ResetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the pulse checker asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous stop operation. The task result is <c>true</c> if the checker was already stopped; <c>false</c> if it was newly deactivated.</returns>
    Task<bool> StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the pulse checker asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous start operation. The task result is <c>true</c> if the checker was already active; <c>false</c> if it was newly activated.</returns>
    Task<bool> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the trigger history for this pulse checker.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A list of history entries, most recent last.</returns>
    Task<List<PulseCheckerHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all trigger history entries for this pulse checker.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task ClearHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables history recording for this pulse checker.
    /// </summary>
    /// <param name="enabled">Whether history recording should be enabled.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task SetHistoryEnabledAsync(bool enabled, CancellationToken cancellationToken = default);

}
