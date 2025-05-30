using Healthie.Abstractions.Enums;

namespace Healthie.Abstractions.Models;

/// <summary>
/// Represents the result of a pulse check.
/// </summary>
public record PulseCheckerResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PulseCheckerResult"/> class.
    /// </summary>
    /// <param name="health">The health status of the pulse check.</param>
    /// <param name="message">An optional message providing more details about the result.</param>
    public PulseCheckerResult(PulseCheckerHealth health, string? message = null)
    {
        _health = health;
        _message = message;
    }

    private readonly PulseCheckerHealth _health;
    private readonly string? _message;

    /// <summary>
    /// Gets the health status of the pulse check.
    /// </summary>
    public PulseCheckerHealth Health => _health;

    /// <summary>
    /// Gets a value indicating whether the pulse check was successful (Health is Healthy).
    /// </summary>
    /// <remarks>
    /// This property is provided for backward compatibility.
    /// </remarks>
    public bool IsHealthy => _health == PulseCheckerHealth.Healthy;

    /// <summary>
    /// Gets an optional message providing more details about the result.
    /// </summary>
    public string? Message => _message;
}
