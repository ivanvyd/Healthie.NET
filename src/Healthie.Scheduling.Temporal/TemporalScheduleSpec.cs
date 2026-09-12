using Cronos;
using Healthie.Abstractions.Scheduling;
using Temporalio.Client.Schedules;

namespace Healthie.Scheduling.Temporal;

/// <summary>
/// Expresses a <see cref="PulseSchedule"/> as the specification Temporal schedules by.
/// </summary>
/// <remarks>
/// Kept separate from the scheduler because it is the one part of this integration that can be
/// tested without a Temporal server: everything else needs a running cluster, and this is where a
/// mistake would silently change how often a check runs.
/// <para>
/// Temporal accepts the same standard Unix fields, but its six-field form adds a trailing year;
/// Healthie's six-field form adds leading seconds. Seconds expressions therefore gain a wildcard
/// year so every field keeps the meaning the caller supplied.
/// </para>
/// </remarks>
internal static class TemporalScheduleSpec
{
    // google.protobuf.Duration is bounded to 10,000 years in either direction. Temporal schedules
    // serialize intervals through that type, so accepting anything larger only defers the failure.
    private static readonly TimeSpan MaxInterval = TimeSpan.FromSeconds(315_576_000_000);

    /// <summary>Builds the Temporal specification for a schedule.</summary>
    /// <param name="schedule">The schedule to express.</param>
    public static ScheduleSpec From(PulseSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (!TryValidate(schedule, out var error))
        {
            throw new ArgumentException($"Schedule '{schedule}' cannot be used by Temporal. {error}", nameof(schedule));
        }

        if (schedule.CronExpression is { } cron)
        {
            return new ScheduleSpec { CronExpressions = [ForTemporal(cron)] };
        }

        // An interval rather than a cron expression for a fixed period: Temporal counts interval
        // occurrences from an epoch rather than from when the schedule was created, so two replicas
        // creating the same schedule agree on when it fires.
        return new ScheduleSpec { Intervals = [new ScheduleIntervalSpec(schedule.Period!.Value)] };
    }

    /// <summary>Validates Healthie's public schedule syntax against Temporal's limits.</summary>
    internal static bool TryValidate(PulseSchedule schedule, out string? error)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (schedule.Period is { } period)
        {
            if (period >= TimeSpan.FromSeconds(1) && period <= MaxInterval)
            {
                error = null;
                return true;
            }

            error = $"Temporal requires fixed intervals from one second through {MaxInterval}.";
            return false;
        }

        var cron = schedule.CronExpression!;
        if (!UsesTemporalFieldGrammar(cron))
        {
            error = "Temporal cron fields support values, wildcards, steps, ascending ranges, and lists; " +
                "relative-day and seeded-jitter extensions are not supported.";
            return false;
        }

        try
        {
            CronExpression.Parse(cron, CronFormatFor(cron));
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is CronFormatException or MissingSeedException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Temporal uses five fields for minute precision, six for a trailing year, and seven for a
    /// leading seconds field plus year. Healthie's six-field form has seconds first.
    /// </summary>
    private static string ForTemporal(string expression) =>
        FieldCount(expression) == 6 ? $"{expression} *" : expression;

    private static CronFormat CronFormatFor(string expression) =>
        FieldCount(expression) == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;

    /// <summary>
    /// Cronos also accepts relative-day and seeded-jitter extensions that Temporal's calendar
    /// grammar cannot represent. Accept only Temporal's documented field forms before using Cronos
    /// for field counts, ranges, and occurrence semantics.
    /// </summary>
    private static bool UsesTemporalFieldGrammar(string expression)
    {
        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length is not (5 or 6))
        {
            return false;
        }

        var monthIndex = fields.Length - 2;
        var dayIndex = fields.Length - 1;
        for (var index = 0; index < fields.Length; index++)
        {
            if (!UsesTemporalFieldGrammar(fields[index], index, monthIndex, dayIndex))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Cronos wraps a descending range across the end of its field. Temporal instead collapses a
    /// range whose end precedes its start to the start value, so accepting one would change when it
    /// fires.
    /// </summary>
    private static bool HasNoReversedRanges(string field, int index, int monthIndex, int dayIndex)
    {
        foreach (var item in field.Split(','))
        {
            var range = item.Split('/', 2)[0];
            var separator = range.IndexOf('-');
            if (separator < 0)
            {
                continue;
            }

            if (!TryFieldValue(range[..separator], index, monthIndex, dayIndex, out var start)
                || !TryFieldValue(range[(separator + 1)..], index, monthIndex, dayIndex, out var end)
                || end < start)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryFieldValue(
        string value,
        int index,
        int monthIndex,
        int dayIndex,
        out int parsed)
    {
        if (int.TryParse(value, out parsed))
        {
            return true;
        }

        parsed = index == monthIndex
            ? MonthValue(value)
            : index == dayIndex ? DayValue(value) : -1;
        return parsed >= 0;
    }

    private static int MonthValue(string value) => value.ToUpperInvariant() switch
    {
        "JAN" or "JANUARY" => 1,
        "FEB" or "FEBRUARY" => 2,
        "MAR" or "MARCH" => 3,
        "APR" or "APRIL" => 4,
        "MAY" => 5,
        "JUN" or "JUNE" => 6,
        "JUL" or "JULY" => 7,
        "AUG" or "AUGUST" => 8,
        "SEP" or "SEPTEMBER" => 9,
        "OCT" or "OCTOBER" => 10,
        "NOV" or "NOVEMBER" => 11,
        "DEC" or "DECEMBER" => 12,
        _ => -1,
    };

    private static int DayValue(string value) => value.ToUpperInvariant() switch
    {
        "SUN" or "SUNDAY" => 0,
        "MON" or "MONDAY" => 1,
        "TUE" or "TUESDAY" => 2,
        "WED" or "WEDNESDAY" => 3,
        "THU" or "THURSDAY" => 4,
        "FRI" or "FRIDAY" => 5,
        "SAT" or "SATURDAY" => 6,
        _ => -1,
    };

    private static bool UsesTemporalFieldGrammar(string field, int index, int monthIndex, int dayIndex)
    {
        for (var position = 0; position < field.Length; position++)
        {
            var character = field[position];
            if (char.IsAsciiDigit(character) || character is '*' or '/' or '-' or ',')
            {
                continue;
            }

            if (!char.IsAsciiLetter(character))
            {
                return false;
            }

            var start = position;
            while (position + 1 < field.Length && char.IsAsciiLetter(field[position + 1]))
            {
                position++;
            }

            var name = field[start..(position + 1)];
            var known = index == monthIndex
                ? MonthValue(name) > 0
                : index == dayIndex && DayValue(name) >= 0;
            if (!known)
            {
                return false;
            }
        }

        return HasNoReversedRanges(field, index, monthIndex, dayIndex);
    }

    private static int FieldCount(string expression) =>
        expression.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
}
