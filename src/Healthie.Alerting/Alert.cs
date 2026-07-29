using Healthie.Abstractions.Enums;

namespace Healthie.Alerting;

/// <summary>
/// One health change worth telling someone about.
/// </summary>
/// <param name="CheckerName">The checker's name, which identifies it everywhere else in the library.</param>
/// <param name="DisplayName">The checker's display name, for a human reading the alert.</param>
/// <param name="Group">The checker's group, or <c>null</c> when it has none.</param>
/// <param name="Tags">The checker's tags, for routing. May be empty.</param>
/// <param name="PreviousHealth">The health being left, or <c>null</c> when the checker had never run.</param>
/// <param name="CurrentHealth">The health now.</param>
/// <param name="Message">The check's own message, which usually says what went wrong.</param>
/// <param name="OccurredAt">When the change was observed, in UTC.</param>
public sealed record Alert(
    string CheckerName,
    string DisplayName,
    string? Group,
    IReadOnlyList<string> Tags,
    PulseCheckerHealth? PreviousHealth,
    PulseCheckerHealth CurrentHealth,
    string Message,
    DateTime OccurredAt)
{
    /// <summary>Whether this alert says a checker came back.</summary>
    /// <remarks>
    /// Worth its own property because a recovery is usually delivered differently from a failure --
    /// it closes an incident rather than opening one, and a sink that treats the two alike pages
    /// somebody at three in the morning to tell them everything is fine.
    /// </remarks>
    public bool IsRecovery => CurrentHealth == PulseCheckerHealth.Healthy;

    /// <summary>
    /// Identifies the ongoing situation this alert belongs to, rather than this one occurrence.
    /// </summary>
    /// <remarks>
    /// Deliberately excludes the health and the time. A checker that flaps between suspicious and
    /// unhealthy is one incident, not two, and an incident tracker keyed on this closes and reopens
    /// the same one instead of accumulating a new one per transition.
    /// </remarks>
    public string DeduplicationKey => $"healthie:{CheckerName}";
}
