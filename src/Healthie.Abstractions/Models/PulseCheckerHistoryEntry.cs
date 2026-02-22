using Healthie.Abstractions.Enums;

namespace Healthie.Abstractions.Models;

/// <summary>
/// Represents a single historical trigger execution entry for a pulse checker.
/// </summary>
/// <param name="Health">The health status of the pulse check.</param>
/// <param name="Message">An optional message describing the result.</param>
/// <param name="ExecutedAt">The UTC date and time when the check was executed.</param>
public record PulseCheckerHistoryEntry(
    PulseCheckerHealth Health,
    string? Message,
    DateTime ExecutedAt);
