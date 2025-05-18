using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;

namespace Healthie.Abstractions;

/// <summary>
/// Defines a contract for a pulse checker.
/// Pulse checkers are used to monitor the health of a specific component or service.
/// </summary>
public interface IPulseChecker : IPulse, IState
{
    /// <summary>
    /// Gets the name of the pulse checker.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Performs the pulse check.
    /// </summary>
    /// <returns>The result of the pulse check.</returns>
    PulseCheckerResult Check();

    /// <summary>
    /// Sets the interval at which the pulse check should be performed.
    /// </summary>
    /// <param name="interval">The interval to set for the pulse check.</param>
    void SetInterval(PulseInterval interval);

    /// <summary>
    /// Stops the pulse checker.
    /// </summary>
    /// <returns><c>true</c> if the pulse checker was successfully stopped; otherwise, <c>false</c>.</returns>
    bool Stop();

    /// <summary>
    /// Starts the pulse checker.
    /// </summary>
    /// <returns><c>true</c> if the pulse checker was successfully started; otherwise, <c>false</c>.</returns>
    bool Start();
}