using Healthie.Abstractions.Scheduling;
using System.Globalization;

namespace Healthie.Scheduling.Hangfire;

/// <summary>
/// Expresses a <see cref="PulseSchedule"/> as the cron expression Hangfire schedules by.
/// </summary>
/// <remarks>
/// <para>
/// Hangfire has one way to say "repeatedly": a recurring job with a cron expression. A fixed period
/// therefore has to become cron, and not every period can. Ten seconds divides a minute evenly and
/// becomes <c>*/10 * * * * *</c>; seven seconds does not, and a cron expression that fired at
/// :00, :07 … :56 would then wait four seconds rather than seven before starting the next minute.
/// Those are refused rather than approximated.
/// </para>
/// <para>
/// A cron expression needs no conversion at all. Hangfire parses cron with Cronos, which is the
/// same standard Unix syntax <see cref="PulseSchedule.Cron"/> carries, so it passes straight
/// through -- unlike Quartz, whose dialect differs.
/// </para>
/// </remarks>
internal static class PeriodCron
{
    /// <summary>
    /// Converts a schedule to a cron expression Hangfire understands.
    /// </summary>
    /// <param name="schedule">The schedule to express.</param>
    /// <param name="checkerName">The checker being scheduled, named in the failure message.</param>
    /// <exception cref="NotSupportedException">No cron expression fires at exactly this period.</exception>
    public static string From(PulseSchedule schedule, string checkerName)
    {
        if (schedule.CronExpression is { } cron)
        {
            return cron;
        }

        return FromPeriod(schedule.Period!.Value)
            ?? throw new NotSupportedException(
                $"Hangfire schedules by cron expression, and no cron expression fires every " +
                $"{schedule.Period} exactly, so pulse checker '{checkerName}' cannot be scheduled on it. " +
                $"Use a period that divides evenly into a minute, an hour or a day, or give the " +
                $"schedule a cron expression of its own.");
    }

    /// <summary>
    /// The cron expression firing at exactly this period, or <c>null</c> when none does.
    /// </summary>
    internal static string? FromPeriod(TimeSpan period)
    {
        if (period <= TimeSpan.Zero || period.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            return null;
        }

        var seconds = (long)period.TotalSeconds;

        // Sub-minute: the six-field form, whose leading field is seconds.
        if (seconds < 60)
        {
            return 60 % seconds == 0 ? Format("*/{0} * * * * *", seconds) : null;
        }

        if (seconds % 60 != 0)
        {
            return null;
        }

        var minutes = seconds / 60;

        if (minutes < 60)
        {
            return 60 % minutes == 0 ? Format("*/{0} * * * *", minutes) : null;
        }

        if (minutes % 60 != 0)
        {
            return null;
        }

        var hours = minutes / 60;

        if (hours < 24)
        {
            return 24 % hours == 0 ? Format("0 */{0} * * *", hours) : null;
        }

        // Daily is the longest period a plain repeating cron expression can state: beyond a day,
        // "every N days" stops being periodic because months are not all the same length.
        return hours == 24 ? "0 0 * * *" : null;
    }

    private static string Format(string format, long value) =>
        string.Format(CultureInfo.InvariantCulture, format, value);
}
