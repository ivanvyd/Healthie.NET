using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Extensions;
using System.Text.Json.Serialization;

namespace Healthie.Abstractions.Scheduling;

/// <summary>
/// Describes when a pulse checker runs: either every fixed period, or on a cron expression.
/// </summary>
/// <remarks>
/// <para>
/// This exists because <see cref="PulseInterval"/> is a closed list that stops at five minutes,
/// which is short of what a certificate-expiry or disk-space check wants. The enum is still
/// supported and still stored -- a checker that never mentions a schedule keeps behaving exactly
/// as it did -- and this type covers what the enum cannot express.
/// </para>
/// <para>
/// Cron expressions are standard Unix syntax, five fields (minute, hour, day-of-month, month,
/// day-of-week) or six with a leading seconds field. That is deliberately <em>not</em> the Quartz
/// dialect, which numbers its fields differently and requires <c>?</c> in one of the two day
/// fields. Schedulers built on Quartz translate on the way in.
/// </para>
/// <para>
/// Expressions are evaluated in UTC by every scheduler. That is worth stating rather than leaving
/// to be assumed: Quartz defaults a cron trigger to the machine's local timezone, so the same
/// expression would otherwise mean one time under the built-in scheduler and another under Quartz,
/// while agreeing on any host configured as UTC -- which most containers are, so the disagreement
/// would surface only somewhere it mattered. The dashboard renders UTC throughout, so UTC is what
/// the rest of this library already means.
/// </para>
/// </remarks>
public sealed record PulseSchedule
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PulseSchedule"/> class.
    /// </summary>
    /// <param name="period">The fixed period between runs, or <c>null</c> when this is a cron schedule.</param>
    /// <param name="cronExpression">The cron expression, or <c>null</c> when this is a fixed period.</param>
    /// <exception cref="ArgumentException">
    /// Neither or both of <paramref name="period"/> and <paramref name="cronExpression"/> were supplied,
    /// or <paramref name="period"/> was not positive.
    /// </exception>
    /// <remarks>
    /// Public so that a stored schedule round-trips through <c>System.Text.Json</c>, which needs a
    /// constructor whose parameters match the properties. Prefer <see cref="Every"/> and
    /// <see cref="Cron"/>, which say which of the two you meant.
    /// </remarks>
    [JsonConstructor]
    public PulseSchedule(TimeSpan? period, string? cronExpression)
    {
        var hasCron = !string.IsNullOrWhiteSpace(cronExpression);

        if (period.HasValue == hasCron)
        {
            throw new ArgumentException(
                "A schedule is either a fixed period or a cron expression, and must be exactly one of them.",
                period.HasValue ? nameof(cronExpression) : nameof(period));
        }

        if (period is { } value && value <= TimeSpan.Zero)
        {
            throw new ArgumentException($"A period must be positive, but was {value}.", nameof(period));
        }

        Period = period;
        CronExpression = hasCron ? NormalizeCron(cronExpression!) : null;
    }

    /// <summary>
    /// Reduces an expression to one spelling, so two that mean the same thing are the same.
    /// </summary>
    /// <remarks>
    /// A schedule takes part in state equality, and <c>StateChanged</c> fires off that equality.
    /// Left as typed, "0 0 * * MON-FRI" and "0  0  *  *  mon-fri" would compare as a change every
    /// time one replaced the other, and the same checker would look like it had been edited. Tags
    /// were normalized for this reason already; this is the same lesson.
    /// </remarks>
    private static string NormalizeCron(string expression) =>
        string.Join(' ', expression.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();

    /// <summary>Gets the fixed period between runs, or <c>null</c> when this is a cron schedule.</summary>
    public TimeSpan? Period { get; }

    /// <summary>Gets the cron expression, or <c>null</c> when this is a fixed period.</summary>
    public string? CronExpression { get; }

    /// <summary>Gets a value indicating whether this schedule is expressed as a cron expression.</summary>
    [JsonIgnore]
    public bool IsCron => CronExpression is not null;

    /// <summary>Creates a schedule that runs every <paramref name="period"/>.</summary>
    /// <param name="period">The period between runs. Must be positive.</param>
    public static PulseSchedule Every(TimeSpan period) => new(period, null);

    /// <summary>Creates a schedule from a standard Unix cron expression.</summary>
    /// <param name="expression">Five fields, or six with a leading seconds field.</param>
    /// <remarks>
    /// The expression is not parsed here. <c>Healthie.Abstractions</c> carries a single dependency
    /// on purpose, and a cron parser would be a second one; the scheduler that runs the expression
    /// is what rejects a malformed one, and it says which scheduler could not read it.
    /// </remarks>
    public static PulseSchedule Cron(string expression) => new(null, expression);

    /// <summary>Creates the schedule equivalent to a <see cref="PulseInterval"/>.</summary>
    /// <param name="interval">The interval to convert.</param>
    public static PulseSchedule FromInterval(PulseInterval interval) => new(interval.ToTimeSpan(), null);

    /// <summary>
    /// Converts this schedule back to a <see cref="PulseInterval"/> when one represents it exactly.
    /// </summary>
    /// <param name="interval">The matching interval, when this returns <c>true</c>.</param>
    /// <returns><c>true</c> when an interval represents this schedule exactly; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Only an exact match counts. Rounding six minutes down to the enum's five would quietly run a
    /// check more often than asked, and a schedule that cannot be represented is better refused
    /// than approximated.
    /// </remarks>
    public bool TryToInterval(out PulseInterval interval)
    {
        if (Period is { } period)
        {
            foreach (var candidate in Enum.GetValues<PulseInterval>())
            {
                if (candidate.ToTimeSpan() == period)
                {
                    interval = candidate;
                    return true;
                }
            }
        }

        interval = default;
        return false;
    }

    /// <inheritdoc />
    public override string ToString() => CronExpression ?? $"every {Period}";
}
