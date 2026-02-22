namespace Healthie.Api.Models;

/// <summary>
/// Describes a pulse interval option with its numeric identifier, enum key name, and optional human-readable description.
/// </summary>
/// <param name="Id">The numeric value of the <see cref="Healthie.Abstractions.Enums.PulseInterval"/> enum member.</param>
/// <param name="Key">The string name of the <see cref="Healthie.Abstractions.Enums.PulseInterval"/> enum member.</param>
public sealed record PulseIntervalDescription(int Id, string Key)
{
    /// <summary>
    /// Gets or sets the human-readable description of the interval, sourced from the
    /// <see cref="System.ComponentModel.DescriptionAttribute"/> on the enum member.
    /// </summary>
    public string? Description { get; set; }
}
