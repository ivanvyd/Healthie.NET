using System.Globalization;

namespace Healthie.Scheduling.Quartz;

/// <summary>
/// Translates the standard Unix cron expressions a <c>PulseSchedule</c> carries into the dialect
/// Quartz parses.
/// </summary>
/// <remarks>
/// <para>
/// The two dialects look alike and are not. Quartz always leads with a seconds field, and numbers
/// its days of the week 1 to 7 from Sunday where Unix numbers them 0 to 6 from Sunday -- so
/// <c>1</c> means Monday in one and Sunday in the other. Passing an expression through untranslated
/// runs every weekly check a day early, on a schedule that looks entirely correct.
/// </para>
/// <para>
/// Quartz also refuses to constrain the day of the month and the day of the week at once, and
/// requires <c>?</c> in whichever one is not being used. Unix says <c>*</c> for both.
/// </para>
/// </remarks>
internal static class UnixCron
{
    /// <summary>
    /// Converts a standard Unix cron expression to its Quartz equivalent.
    /// </summary>
    /// <param name="expression">Five fields, or six with a leading seconds field.</param>
    /// <returns>A Quartz cron expression with six fields.</returns>
    /// <exception cref="NotSupportedException">
    /// The expression has the wrong number of fields, constrains both day fields at once, or uses
    /// syntax that has no Quartz equivalent.
    /// </exception>
    public static string ToQuartz(string expression)
    {
        var fields = expression.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        string seconds, minute, hour, dayOfMonth, month, dayOfWeek;

        switch (fields.Length)
        {
            case 5:
                (seconds, minute, hour, dayOfMonth, month, dayOfWeek) =
                    ("0", fields[0], fields[1], fields[2], fields[3], fields[4]);
                break;
            case 6:
                (seconds, minute, hour, dayOfMonth, month, dayOfWeek) =
                    (fields[0], fields[1], fields[2], fields[3], fields[4], fields[5]);
                break;
            default:
                throw new NotSupportedException(
                    $"Cron expression '{expression}' has {fields.Length} fields. Standard Unix cron has " +
                    "five, or six with a leading seconds field.");
        }

        dayOfWeek = ShiftDayOfWeek(dayOfWeek, expression);

        // Quartz wants '?' in whichever day field is unconstrained, and rejects constraining both.
        var dayOfMonthIsAny = dayOfMonth is "*" or "?";
        var dayOfWeekIsAny = dayOfWeek is "*" or "?";

        if (dayOfWeekIsAny)
        {
            dayOfWeek = "?";
        }
        else if (dayOfMonthIsAny)
        {
            dayOfMonth = "?";
        }
        else
        {
            throw new NotSupportedException(
                $"Cron expression '{expression}' constrains both the day of the month and the day of " +
                "the week. Quartz can only constrain one of them, so this schedule cannot be run by " +
                "the Quartz scheduler. The built-in timer scheduler accepts it.");
        }

        return string.Join(' ', seconds, minute, hour, dayOfMonth, month, dayOfWeek);
    }

    /// <summary>
    /// Renumbers a Unix day-of-week field (0-6, Sunday first) into the Quartz numbering (1-7).
    /// </summary>
    /// <remarks>
    /// Day names pass through: both dialects spell them the same way. Digits are shifted one up,
    /// including inside lists, ranges and steps, with Unix's alternative <c>7</c> for Sunday folding
    /// onto Quartz's <c>1</c>. Anything carrying Quartz-only syntax is refused rather than guessed
    /// at -- it was never a Unix expression to begin with.
    /// </remarks>
    private static string ShiftDayOfWeek(string field, string expression)
    {
        if (field is "*" or "?")
        {
            return field;
        }

        if (field.Contains('#', StringComparison.Ordinal) || field.Contains('L', StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Cron expression '{expression}' uses '#' or 'L' in its day-of-week field, which is not " +
                "standard Unix cron syntax.");
        }

        // A field is a comma-separated list of terms; each term may be a range and may carry a
        // step. Only the day numbers move -- a step of /2 means every second day either way.
        var terms = field.Split(',');

        for (var i = 0; i < terms.Length; i++)
        {
            var term = terms[i];

            var slash = term.IndexOf('/', StringComparison.Ordinal);
            var step = slash >= 0 ? term[slash..] : string.Empty;
            var days = slash >= 0 ? term[..slash] : term;

            terms[i] = string.Join('-', days.Split('-').Select(ShiftDay)) + step;
        }

        return string.Join(',', terms);

        string ShiftDay(string day)
        {
            if (day is "*")
            {
                return day;
            }

            if (!int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                // A name such as MON. Both dialects use the same three letters, so it is already right.
                return day;
            }

            return number switch
            {
                // Unix allows 7 as a second spelling of Sunday, which is Quartz's 1.
                7 => "1",
                >= 0 and <= 6 => (number + 1).ToString(CultureInfo.InvariantCulture),
                _ => throw new NotSupportedException(
                    $"Cron expression '{expression}' has day-of-week '{day}', which is outside the " +
                    "standard Unix range of 0 to 7."),
            };
        }
    }
}
