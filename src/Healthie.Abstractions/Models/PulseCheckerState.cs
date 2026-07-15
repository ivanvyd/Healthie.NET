using Healthie.Abstractions.Enums;

namespace Healthie.Abstractions.Models;

/// <summary>
/// Represents the state of a pulse checker.
/// </summary>
public record PulseCheckerState
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PulseCheckerState"/> class with default settings.
    /// </summary>
    public PulseCheckerState() : this(PulseInterval.EveryMinute)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PulseCheckerState"/> class with the specified interval.
    /// </summary>
    /// <param name="interval">The initial interval at which the pulse checker operates.</param>
    public PulseCheckerState(PulseInterval interval) : this(interval, 0)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PulseCheckerState"/> class with the specified interval and unhealthy threshold.
    /// </summary>
    /// <param name="interval">The initial interval at which the pulse checker operates.</param>
    /// <param name="unhealthyThreshold">The number of consecutive failures needed to consider the pulse checker unhealthy.</param>
    public PulseCheckerState(PulseInterval interval, uint unhealthyThreshold)
    {
        Interval = interval;
        UnhealthyThreshold = unhealthyThreshold;
    }

    /// <summary>
    /// Gets or sets the date and time when the pulse checker was last executed.
    /// </summary>
    public DateTime? LastExecutionDateTime { get; set; }

    /// <summary>
    /// Gets or sets the result of the last pulse check.
    /// </summary>
    public PulseCheckerResult? LastResult { get; set; }

    /// <summary>
    /// Gets or sets the interval at which the pulse checker operates.
    /// </summary>
    public PulseInterval Interval { get; set; }

    /// <summary>
    /// Gets or sets the count of consecutive failures (non-healthy results).
    /// </summary>
    /// <remarks>
    /// This is reset to 0 when a healthy result is received.
    /// </remarks>
    public int ConsecutiveFailureCount { get; set; }

    /// <summary>
    /// Gets or sets the threshold for consecutive failures before the checker is considered unhealthy.
    /// </summary>
    public uint UnhealthyThreshold { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the pulse checker is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether history recording is enabled for this pulse checker.
    /// </summary>
    public bool IsHistoryEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the list of historical trigger execution entries for this pulse checker.
    /// </summary>
    public List<PulseCheckerHistoryEntry> History { get; set; } = [];

    /// <summary>
    /// Determines whether this state is equal to another by comparing every value it holds.
    /// </summary>
    /// <param name="other">The state to compare against.</param>
    /// <returns><c>true</c> if both states hold the same values; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// The compiler-generated equality of a record compares <see cref="History"/> by reference,
    /// which reports two states as different whenever they came from separate reads and as equal
    /// whenever they happen to share a list. State changes are detected by comparing states, so
    /// history is compared element by element here instead.
    /// </remarks>
    public virtual bool Equals(PulseCheckerState? other)
    {
        return other is not null
            && LastExecutionDateTime == other.LastExecutionDateTime
            && LastResult == other.LastResult
            && Interval == other.Interval
            && ConsecutiveFailureCount == other.ConsecutiveFailureCount
            && UnhealthyThreshold == other.UnhealthyThreshold
            && IsActive == other.IsActive
            && IsHistoryEnabled == other.IsHistoryEnabled
            && History.SequenceEqual(other.History);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hashCode = new HashCode();

        hashCode.Add(LastExecutionDateTime);
        hashCode.Add(LastResult);
        hashCode.Add(Interval);
        hashCode.Add(ConsecutiveFailureCount);
        hashCode.Add(UnhealthyThreshold);
        hashCode.Add(IsActive);
        hashCode.Add(IsHistoryEnabled);

        foreach (var entry in History)
        {
            hashCode.Add(entry);
        }

        return hashCode.ToHashCode();
    }
}
