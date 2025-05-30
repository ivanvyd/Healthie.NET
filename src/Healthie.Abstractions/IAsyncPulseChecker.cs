using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;

namespace Healthie.Abstractions;

/// <summary>
/// Defines a contract for an asynchronous pulse checker.
/// Asynchronous pulse checkers are used to monitor the health of a specific component or service using asynchronous operations.
/// </summary>
public interface IAsyncPulseChecker : IAsyncPulse, IAsyncState, IAsyncDisposable
{
    /// <summary>
    /// Gets the name of the pulse checker.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Performs the pulse check asynchronously.
    /// </summary>
    /// <returns>A task that represents the asynchronous pulse check operation. The task result contains the <see cref="PulseCheckerResult"/>.</returns>
    Task<PulseCheckerResult> CheckAsync();

    /// <summary>
    /// Sets the interval for the pulse check asynchronously.
    /// </summary>
    /// <param name="interval">The interval to set for the pulse check.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task SetIntervalAsync(PulseInterval interval);

    /// <summary>
    /// Sets the unhealthy threshold for the pulse checker asynchronously.
    /// </summary>
    /// <param name="threshold">The number of consecutive failures needed to consider the pulse checker unhealthy.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task SetUnhealthyThresholdAsync(uint threshold);

    /// <summary>
    /// Resets the pulse checker state to healthy asynchronously.
    /// </summary>
    /// <remarks>
    /// This resets the consecutive failures count, sets the health status to Healthy, and clears any error messages.
    /// </remarks>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task ResetAsync();

    /// <summary>
    /// Stops the pulse checker asynchronously.
    /// </summary>
    /// <returns>A task that represents the asynchronous stop operation. The task result indicates whether the stop was successful.</returns>
    Task<bool> StopAsync();

    /// <summary>
    /// Starts the pulse checker asynchronously.
    /// </summary>
    /// <returns>A task that represents the asynchronous start operation. The task result indicates whether the start was successful.</returns>
    Task<bool> StartAsync();
}