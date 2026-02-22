using Healthie.Abstractions.Enums;

namespace Healthie.Abstractions.Models;

/// <summary>
/// Represents the result of a pulse check.
/// </summary>
/// <param name="Health">The health status of the pulse check.</param>
/// <param name="Message">An optional message providing more details about the result.</param>
public record PulseCheckerResult(PulseCheckerHealth Health, string? Message = null)
{
    /// <summary>
    /// Gets a value indicating whether the pulse check was successful (Health is Healthy).
    /// </summary>
    public bool IsHealthy => Health == PulseCheckerHealth.Healthy;
}
