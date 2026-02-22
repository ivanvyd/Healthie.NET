namespace Healthie.Abstractions.Models;

/// <summary>
/// Provides data for the <see cref="IPulseChecker.StateChanged"/> event.
/// </summary>
/// <param name="oldState">The state before the change.</param>
/// <param name="newState">The state after the change.</param>
public class PulseCheckerStateChangedEventArgs(
    PulseCheckerState oldState,
    PulseCheckerState newState) : EventArgs
{
    /// <summary>
    /// Gets the state before the change.
    /// </summary>
    public PulseCheckerState OldState { get; } = oldState;

    /// <summary>
    /// Gets the state after the change.
    /// </summary>
    public PulseCheckerState NewState { get; } = newState;
}
