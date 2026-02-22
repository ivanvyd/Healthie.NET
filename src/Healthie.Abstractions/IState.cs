using Healthie.Abstractions.Models;

namespace Healthie.Abstractions;

/// <summary>
/// Defines a contract for getting and setting pulse checker state asynchronously.
/// </summary>
public interface IState
{
    /// <summary>
    /// Gets the current state of the pulse checker asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the current <see cref="PulseCheckerState"/>.</returns>
    Task<PulseCheckerState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the state of the pulse checker asynchronously.
    /// </summary>
    /// <param name="state">The state to set.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task SetStateAsync(PulseCheckerState state, CancellationToken cancellationToken = default);
}
