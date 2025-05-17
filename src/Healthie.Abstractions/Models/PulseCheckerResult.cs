namespace Healthie.Abstractions.Models;

public record PulseCheckerResult
{
    public PulseCheckerResult(bool isHealthy, string? message = null)
    {
        _isHealthy = isHealthy;
        _message = message;
    }

    private readonly bool _isHealthy;
    private readonly string? _message;

    public bool IsHealthy => _isHealthy;
    public string? Message => _message;
}
