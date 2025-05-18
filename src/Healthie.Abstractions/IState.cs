using Healthie.Abstractions.Models;

namespace Healthie.Abstractions;

/// <summary>
/// Represents the state of a pulse checker.
/// </summary>
public interface IState
{
    /// <summary>
    /// Gets the state of the pulse checker.
    /// </summary>
    /// <returns>The current state of checker.</returns>
    PulseCheckerState GetState();

    /// <summary>
    /// Sets the state of the pulse checker.
    /// </summary>
    /// <param name="state">The state to set.</param>
    void SetState(PulseCheckerState state);
}
