namespace Healthie.Abstractions.Models;

public record State
{
    public DateTime? LastExecutionDateTime { get; set; }
    public Pulse<Result>? LastPulse { get; set; }
}
