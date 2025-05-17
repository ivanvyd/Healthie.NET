using Healthie.Abstractions.Enums;

namespace Healthie.Abstractions.Models;

public record PulseCheckerState
{
    public DateTime? LastExecutionDateTime { get; set; }
    public PulseCheckerResult? LastResult { get; set; }
    public PulseInterval Interval { get; set; } = PulseInterval.EveryMinute;
    public bool IsActive { get; set; } = true;
}
