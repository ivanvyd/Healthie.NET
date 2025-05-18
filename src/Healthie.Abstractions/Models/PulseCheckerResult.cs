namespace Healthie.Abstractions.Models;

/// <summary>
/// Represents the result of a pulse check.
/// </summary>
public record PulseCheckerResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PulseCheckerResult"/> class.
    /// </summary>
    /// <param name="isHealthy">A value indicating whether the pulse check was successful.</param>
    /// <param name="message">An optional message providing more details about the result.</param>
    public PulseCheckerResult(bool isHealthy, string? message = null)
    {
        _isHealthy = isHealthy;
        _message = message;
    }

    private readonly bool _isHealthy;
    private readonly string? _message;

    /// <summary>
    /// Gets a value indicating whether the pulse check was successful.
    /// </summary>
    public bool IsHealthy => _isHealthy;

    /// <summary>
    /// Gets an optional message providing more details about the result.
    /// </summary>
    public string? Message => _message;
}
