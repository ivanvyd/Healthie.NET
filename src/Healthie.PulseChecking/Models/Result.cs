namespace Healthie.PulseChecking.Models;

public record Result(bool isHealthy, string? message = null)
{
    private readonly bool _isHealthy = isHealthy;
    private readonly string? _message = message;

    public bool IsHealthy => _isHealthy;
    public string? Message => _message;
}
