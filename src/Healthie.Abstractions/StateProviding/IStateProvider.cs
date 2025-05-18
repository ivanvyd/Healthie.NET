namespace Healthie.Abstractions.StateProviding;

/// <summary>
/// Defines a contract for providing and managing pulse checker states.
/// </summary>
public interface IStateProvider
{
    /// <summary>
    /// Gets the state of a specific pulse checker.
    /// </summary>
    /// <param name="name">The name of the pulse checker.</param>
    /// <returns>The <see cref="IState"/> of the pulse checker, or <c>null</c> if not found.</returns>
    TState? GetState<TState>(string name);

    /// <summary>
    /// Gets the states of all pulse checkers.
    /// </summary>
    /// <returns>A collection of <see cref="IState"/>.</returns>
    void SetState<TState>(string name, TState state);
}
