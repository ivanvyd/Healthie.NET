using Healthie.Abstractions.Enums;

namespace Healthie.Abstractions.Models;

/// <summary>
/// Represents the state of a pulse checker.
/// </summary>
public record PulseCheckerState
{
    /// <summary>
    /// Gets or sets the date and time when the pulse checker was last executed.
    /// </summary>
    public DateTime? LastExecutionDateTime { get; set; }

    /// <summary>
    /// Gets or sets the result of the last pulse check.
    /// </summary>
    public PulseCheckerResult? LastResult { get; set; }

    /// <summary>
    /// Gets or sets the interval at which the pulse checker operates.
    /// </summary>
    public PulseInterval Interval { get; set; } = PulseInterval.EveryMinute;

    /// <summary>
    /// Gets or sets a value indicating whether the pulse checker is active.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
