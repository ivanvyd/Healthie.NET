using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using System.Text.Json;

namespace Healthie.Tests.Unit;

/// <summary>
/// A schedule is persisted and compared, so the two things that matter are that it cannot be
/// constructed into a meaningless state, and that a state written before schedules existed still
/// reads back as the interval it was saved with.
/// </summary>
public class PulseScheduleTests
{
    [Fact]
    public void Constructor_WithNeitherPeriodNorCron_Throws()
    {
        Assert.Throws<ArgumentException>(() => new PulseSchedule(null, null));
    }

    [Fact]
    public void Constructor_WithBothPeriodAndCron_Throws()
    {
        Assert.Throws<ArgumentException>(() => new PulseSchedule(TimeSpan.FromMinutes(1), "* * * * *"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithNonPositivePeriod_Throws(int seconds)
    {
        Assert.Throws<ArgumentException>(() => PulseSchedule.Every(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Cron_WithWhitespaceOnlyExpression_ThrowsRatherThanCreatingAnEmptySchedule()
    {
        Assert.Throws<ArgumentException>(() => PulseSchedule.Cron("   "));
    }

    [Fact]
    public void Cron_TrimsTheExpression()
    {
        Assert.Equal("0 * * * *", PulseSchedule.Cron("  0 * * * *  ").CronExpression);
    }

    [Fact]
    public void Every_HoldsThePeriodAndIsNotCron()
    {
        var schedule = PulseSchedule.Every(TimeSpan.FromHours(6));

        Assert.Equal(TimeSpan.FromHours(6), schedule.Period);
        Assert.Null(schedule.CronExpression);
        Assert.False(schedule.IsCron);
    }

    [Fact]
    public void FromInterval_MatchesTheIntervalItWasBuiltFrom()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), PulseSchedule.FromInterval(PulseInterval.Every5Minutes).Period);
    }

    [Fact]
    public void TryToInterval_ForAPeriodAnIntervalRepresentsExactly_ReturnsThatInterval()
    {
        Assert.True(PulseSchedule.Every(TimeSpan.FromSeconds(30)).TryToInterval(out var interval));
        Assert.Equal(PulseInterval.Every30Seconds, interval);
    }

    /// <summary>
    /// Six minutes sits between nothing -- the enum stops at five. Rounding down would run the
    /// check more often than asked and look like it worked, so a schedule the enum cannot say is
    /// refused rather than approximated.
    /// </summary>
    [Fact]
    public void TryToInterval_ForAPeriodBeyondTheEnum_ReturnsFalse()
    {
        Assert.False(PulseSchedule.Every(TimeSpan.FromMinutes(6)).TryToInterval(out _));
    }

    [Fact]
    public void TryToInterval_ForACronSchedule_ReturnsFalse()
    {
        Assert.False(PulseSchedule.Cron("0 3 * * *").TryToInterval(out _));
    }

    [Theory]
    [InlineData("0 3 * * *", null)]
    [InlineData(null, "06:00:00")]
    public void RoundTripsThroughJson(string? cron, string? period)
    {
        var original = cron is not null
            ? PulseSchedule.Cron(cron)
            : PulseSchedule.Every(TimeSpan.Parse(period!));

        var restored = JsonSerializer.Deserialize<PulseSchedule>(JsonSerializer.Serialize(original));

        Assert.Equal(original, restored);
    }

    [Fact]
    public void EffectiveSchedule_WhenNoScheduleIsSet_ComesFromTheInterval()
    {
        var state = new PulseCheckerState(PulseInterval.Every10Seconds);

        Assert.Null(state.Schedule);
        Assert.Equal(TimeSpan.FromSeconds(10), state.EffectiveSchedule.Period);
    }

    [Fact]
    public void EffectiveSchedule_WhenAScheduleIsSet_OverridesTheInterval()
    {
        var state = new PulseCheckerState(PulseInterval.Every10Seconds)
        {
            Schedule = PulseSchedule.Cron("0 3 * * *"),
        };

        Assert.Equal("0 3 * * *", state.EffectiveSchedule.CronExpression);
    }

    /// <summary>
    /// State written before this property existed carries no schedule. It has to deserialize into
    /// a state that still runs on its stored interval, or every checker in an existing store would
    /// silently change how often it runs on upgrade.
    /// </summary>
    [Fact]
    public void StateStoredBeforeSchedulesExisted_KeepsItsInterval()
    {
        const string storedBeforeSchedules =
            """{"Interval":"Every30Seconds","UnhealthyThreshold":2,"IsActive":true}""";

        var state = JsonSerializer.Deserialize<PulseCheckerState>(storedBeforeSchedules)!;

        Assert.Null(state.Schedule);
        Assert.Equal(PulseInterval.Every30Seconds, state.Interval);
        Assert.Equal(TimeSpan.FromSeconds(30), state.EffectiveSchedule.Period);
    }

    /// <summary>
    /// StateChanged fires off state equality, so a schedule change has to register as a change --
    /// otherwise editing a schedule would update the store and tell nobody.
    /// </summary>
    [Fact]
    public void Equals_WhenOnlyTheScheduleDiffers_ReturnsFalse()
    {
        var hourly = new PulseCheckerState { Schedule = PulseSchedule.Cron("0 * * * *") };
        var daily = new PulseCheckerState { Schedule = PulseSchedule.Cron("0 3 * * *") };

        Assert.NotEqual(hourly, daily);
    }

    [Fact]
    public void Equals_ForTheSameScheduleOnSeparateInstances_ReturnsTrue()
    {
        var left = new PulseCheckerState { Schedule = PulseSchedule.Every(TimeSpan.FromHours(1)) };
        var right = new PulseCheckerState { Schedule = PulseSchedule.Every(TimeSpan.FromHours(1)) };

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }
}
