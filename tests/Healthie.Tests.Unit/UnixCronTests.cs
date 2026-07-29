using Healthie.Scheduling.Quartz;
using Quartz;

namespace Healthie.Tests.Unit;

/// <summary>
/// Unix cron and Quartz cron look alike and are not: Quartz leads with a seconds field, numbers
/// days of the week 1-7 from Sunday where Unix numbers them 0-6, and will not constrain both day
/// fields at once. An untranslated expression parses cleanly and runs a day early, which is the
/// kind of wrong that never gets noticed.
/// <para>
/// These assert against Quartz's own parser and the times it actually produces, rather than
/// against the translated string. A textual comparison would pass just as happily on an
/// expression that is one day out.
/// </para>
/// </summary>
public class UnixCronTests
{
    /// <summary>The first UTC instant this expression fires at or after the given moment.</summary>
    private static DateTimeOffset NextFire(string unixExpression, DateTimeOffset after)
    {
        var quartz = new CronExpression(UnixCron.ToQuartz(unixExpression)) { TimeZone = TimeZoneInfo.Utc };
        return quartz.GetTimeAfter(after)!.Value;
    }

    // 2026-07-27 is a Monday, so this window covers one of each weekday.
    private static readonly DateTimeOffset SundayMidnight =
        new(2026, 7, 26, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, DayOfWeek.Sunday)]
    [InlineData(1, DayOfWeek.Monday)]
    [InlineData(2, DayOfWeek.Tuesday)]
    [InlineData(3, DayOfWeek.Wednesday)]
    [InlineData(4, DayOfWeek.Thursday)]
    [InlineData(5, DayOfWeek.Friday)]
    [InlineData(6, DayOfWeek.Saturday)]
    [InlineData(7, DayOfWeek.Sunday)]
    public void EveryUnixDayNumber_FiresOnThatDay(int unixDay, DayOfWeek expected)
    {
        var fires = NextFire($"0 3 * * {unixDay}", SundayMidnight);

        Assert.Equal(expected, fires.UtcDateTime.DayOfWeek);
        Assert.Equal(3, fires.UtcDateTime.Hour);
    }

    [Fact]
    public void DayNames_PassThroughUnshifted()
    {
        Assert.Equal(DayOfWeek.Friday, NextFire("0 3 * * FRI", SundayMidnight).UtcDateTime.DayOfWeek);
    }

    [Fact]
    public void ARangeOfDayNumbers_ShiftsAtBothEnds()
    {
        // Unix 1-5 is Monday to Friday. Starting from Sunday, the first hit must be Monday.
        var fires = NextFire("0 3 * * 1-5", SundayMidnight);

        Assert.Equal(DayOfWeek.Monday, fires.UtcDateTime.DayOfWeek);
    }

    [Fact]
    public void AListOfDayNumbers_ShiftsEveryEntry()
    {
        // Unix 0,6 is the weekend. From Sunday midnight the next 03:00 hit is Sunday itself.
        var sunday = NextFire("0 3 * * 0,6", SundayMidnight);
        Assert.Equal(DayOfWeek.Sunday, sunday.UtcDateTime.DayOfWeek);

        var saturday = NextFire("0 3 * * 0,6", sunday);
        Assert.Equal(DayOfWeek.Saturday, saturday.UtcDateTime.DayOfWeek);
    }

    [Fact]
    public void AFiveFieldExpression_GainsASecondsFieldOfZero()
    {
        Assert.StartsWith("0 ", UnixCron.ToQuartz("30 3 * * *"), StringComparison.Ordinal);
        Assert.Equal(0, NextFire("30 3 * * *", SundayMidnight).UtcDateTime.Second);
    }

    [Fact]
    public void ASixFieldExpression_KeepsItsOwnSecondsField()
    {
        Assert.Equal(15, NextFire("15 30 3 * * *", SundayMidnight).UtcDateTime.Second);
    }

    [Fact]
    public void AnUnconstrainedDayOfWeek_BecomesQuestionMark()
    {
        Assert.EndsWith(" ?", UnixCron.ToQuartz("0 3 1 * *"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnconstrainedDayOfMonth_BecomesQuestionMark()
    {
        // Quartz forbids '*' in both day fields at once; the unused one has to be '?'.
        Assert.Contains(" ? ", UnixCron.ToQuartz("0 3 * * 1"), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryTranslationQuartzMustAccept_Parses()
    {
        foreach (var expression in new[]
                 {
                     "* * * * *", "*/5 * * * *", "0 3 * * *", "0 0 1 * *",
                     "0 3 * * 1-5", "0 */6 * * *", "30 2 1 1 *", "0 0 * * SUN",
                 })
        {
            var translated = UnixCron.ToQuartz(expression);
            Assert.True(
                CronExpression.IsValidExpression(translated),
                $"'{expression}' translated to '{translated}', which Quartz rejects");
        }
    }

    /// <summary>
    /// Quartz cannot constrain both day fields, so an expression that does is refused rather than
    /// quietly having one of its constraints dropped.
    /// </summary>
    [Fact]
    public void ConstrainingBothDayFields_IsRefused()
    {
        var ex = Assert.Throws<NotSupportedException>(() => UnixCron.ToQuartz("0 3 1 * 1"));

        Assert.Contains("Quartz can only constrain one", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0 3 * *")]
    [InlineData("0 3 * * * * *")]
    public void AnExpressionWithTheWrongFieldCount_IsRefused(string expression)
    {
        Assert.Throws<NotSupportedException>(() => UnixCron.ToQuartz(expression));
    }

    [Theory]
    [InlineData("0 3 * * 5#2")]
    [InlineData("0 3 * * 5L")]
    public void QuartzOnlyDayOfWeekSyntax_IsRefusedRatherThanMisread(string expression)
    {
        Assert.Throws<NotSupportedException>(() => UnixCron.ToQuartz(expression));
    }
}
