using Healthie.Abstractions.Enums;

namespace Healthie.Abstractions.Insights;

/// <summary>
/// One alert, as the dashboard shows it.
/// </summary>
/// <param name="CheckerName">The checker the alert is about.</param>
/// <param name="DisplayName">That checker's display name.</param>
/// <param name="PreviousHealth">The health before, or <c>null</c> if it had never run.</param>
/// <param name="CurrentHealth">The health that raised the alert.</param>
/// <param name="Message">The check's own message.</param>
/// <param name="OccurredAt">When it was raised, in UTC.</param>
/// <param name="Delivered">Whether every sink accepted it.</param>
/// <remarks>
/// Raised and delivered are separate facts, and both are carried: an alert that fired and reached
/// nobody is the failure worth seeing on the board.
/// </remarks>
public sealed record AlertInsight(
    string CheckerName,
    string DisplayName,
    PulseCheckerHealth? PreviousHealth,
    PulseCheckerHealth CurrentHealth,
    string Message,
    DateTime OccurredAt,
    bool Delivered);
