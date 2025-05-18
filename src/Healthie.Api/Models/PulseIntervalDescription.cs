namespace Healthie.Api.Models;

public sealed record PulseIntervalDescription(int Id, string Key)
{
    public string? Description { get; set; }
}
