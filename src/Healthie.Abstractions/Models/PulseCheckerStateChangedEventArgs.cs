using Healthie.Abstractions.Enums;

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

    /// <summary>
    /// Gets the health before this change, or <c>null</c> if no check had produced one yet.
    /// </summary>
    public PulseCheckerHealth? PreviousHealth => OldState.LastResult?.Health;

    /// <summary>
    /// Gets the health after this change, or <c>null</c> if there is still no result -- which is
    /// what a setting changed before the first check ever ran looks like.
    /// </summary>
    public PulseCheckerHealth? CurrentHealth => NewState.LastResult?.Health;

    /// <summary>
    /// Gets whether this change was a change of health, rather than only of settings or of when the
    /// check last ran.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IPulseChecker.StateChanged"/> fires whenever the stored state differs, and a check
    /// storing its result always changes it -- the execution time alone is enough. So a handler that
    /// cares about a component going down, rather than about it having been looked at, wants this
    /// and not the event itself.
    /// </para>
    /// <para>
    /// A first result counts: going from nothing known to <c>Healthy</c> is a change. Losing a
    /// result does not, because a state with no result says nothing about health.
    /// </para>
    /// </remarks>
    public bool HealthChanged => CurrentHealth is not null && PreviousHealth != CurrentHealth;
}
