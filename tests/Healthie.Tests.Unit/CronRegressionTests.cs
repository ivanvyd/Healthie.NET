using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Healthie.DependencyInjection;
using Healthie.Scheduling.Quartz;
using Quartz;
using Quartz.Impl;

namespace Healthie.Tests.Unit;

/// <summary>
/// Regressions for what an adversarial review of the schedule work found after it had already
/// merged. Each of these passed review, passed CI, and was wrong.
/// </summary>
public class CronRegressionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// <c>Task.Delay</c> refuses anything past uint.MaxValue milliseconds -- a little under fifty
    /// days -- and throws rather than waiting. The throw is not an
    /// <see cref="OperationCanceledException"/>, so the scheduler's catch could not see it, and the
    /// loop runs detached: an annual check fired once and then stopped forever, silently. A
    /// certificate-expiry check is exactly the case that reaches it, and exactly the case the
    /// feature was built for.
    /// </summary>
    [Theory]
    [InlineData(49.0)]
    [InlineData(49.71)]
    public void TaskDelay_AcceptsWaitsUpToRoughlyFiftyDays(double days)
    {
        _ = Task.Delay(TimeSpan.FromDays(days), CancellationToken.None);
    }

    [Theory]
    [InlineData(49.72)]
    [InlineData(366.0)]
    public void TaskDelay_RefusesLongerOnes_WhichIsWhyTheSchedulerWaitsInSteps(double days)
    {
        // A statement lambda, so this is an Action rather than a Func<Task>: Task.Delay validates
        // its argument and throws synchronously, which is the whole point -- the throw never
        // becomes a faulted task the scheduler could observe.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => { _ = Task.Delay(TimeSpan.FromDays(days), CancellationToken.None); });
    }

    /// <summary>
    /// The scheduler's only catch is for cancellation, so nothing else may reach it. Pinning this
    /// stops someone reintroducing the bug by "simplifying" the stepped wait back to a single one.
    /// </summary>
    [Fact]
    public void TheDelayFailure_IsNotSomethingTheSchedulersCatchWouldHaveSeen()
    {
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => { _ = Task.Delay(TimeSpan.FromDays(366), CancellationToken.None); });

        Assert.IsNotType<OperationCanceledException>(thrown);
    }

    /// <summary>
    /// The fix itself, rather than the BCL behaviour that forced it: a wait longer than Task.Delay
    /// accepts is capped and re-entered, and one that already fits is returned exactly so the
    /// common case is not rounded up to the cap.
    /// </summary>
    [Fact]
    public void AWaitLongerThanTaskDelayAccepts_IsCapped()
    {
        var capped = TimerPulseScheduler.BoundedDelay(TimeSpan.FromDays(366));

        Assert.True(capped < TimeSpan.FromDays(49.71), $"{capped} is still past what Task.Delay takes");
        _ = Task.Delay(capped, CancellationToken.None);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(30)]
    public void AWaitThatAlreadyFits_IsReturnedExactly(double minutes)
    {
        var remaining = TimeSpan.FromMinutes(minutes);

        Assert.Equal(remaining, TimerPulseScheduler.BoundedDelay(remaining));
    }

    /// <summary>
    /// Quartz defaults a cron trigger to <see cref="TimeZoneInfo.Local"/> while the built-in
    /// scheduler evaluates against <see cref="DateTime.UtcNow"/>. One expression meant two
    /// different times, and on a UTC-configured host -- which most containers are -- it looked
    /// fine. The dashboard renders UTC throughout, so UTC is what the rest of the library means.
    /// <para>
    /// Driven through a real in-memory Quartz scheduler and read back off the trigger the scheduler
    /// actually stored. An earlier version of this test built its own trigger with the timezone set
    /// and asserted it was set -- it passed against the unfixed code, and only mutation testing
    /// showed it was proving nothing.
    /// </para>
    /// </summary>
    [Fact]
    public async Task QuartzCronTriggers_RunInUtc_NotTheMachinesTimeZone()
    {
        var factory = new StdSchedulerFactory();
        var scheduler = new QuartzPulseScheduler(factory);
        var checker = new FakePulseChecker("tz-checker");

        await scheduler.ScheduleAsync(checker, PulseSchedule.Cron("0 9 * * *"), Ct);

        var quartz = await factory.GetScheduler(Ct);
        var stored = (ICronTrigger)(await quartz.GetTrigger(new TriggerKey("tz-checker-trigger"), Ct))!;

        Assert.Equal(TimeZoneInfo.Utc, stored.TimeZone);
    }

    /// <summary>Shows the divergence this guards against, rather than only asserting the fix.</summary>
    [Fact]
    public void TheSameExpression_MeansDifferentTimesInDifferentZones()
    {
        var quartzCron = UnixCron.ToQuartz("0 9 * * *");
        var from = new DateTimeOffset(2026, 7, 29, 0, 0, 0, TimeSpan.Zero);

        var inUtc = new CronExpression(quartzCron) { TimeZone = TimeZoneInfo.Utc }.GetTimeAfter(from)!.Value;

        Assert.Equal(9, inUtc.UtcDateTime.Hour);

        // Only meaningful off UTC; on a UTC host the two agree and there is nothing to compare.
        if (TimeZoneInfo.Local.BaseUtcOffset != TimeSpan.Zero)
        {
            var inLocal = new CronExpression(quartzCron) { TimeZone = TimeZoneInfo.Local }.GetTimeAfter(from)!.Value;
            Assert.NotEqual(inUtc, inLocal);
        }
    }

    /// <summary>
    /// Quartz validates the fields it finds borderline and quietly accepts the rest: it took
    /// "99 99 * * *" without complaint and produced a trigger firing every minute, forever. A check
    /// running sixty times an hour instead of on a mistyped schedule is worse than one that refuses.
    /// </summary>
    [Theory]
    [InlineData("99 99 * * *")]
    [InlineData("60 * * * *")]
    [InlineData("* 24 * * *")]
    [InlineData("0 0 32 * *")]
    [InlineData("0 0 * 13 *")]
    [InlineData("60 0 0 * * *")]
    public void AnOutOfRangeField_IsRefused(string expression)
    {
        // The last case is six-field, so its leading 60 is an out-of-range seconds; the rest are
        // five-field. Both shapes go through the same check.
        Assert.Throws<NotSupportedException>(() => UnixCron.ToQuartz(expression));
    }

    [Theory]
    [InlineData("59 23 31 12 *")]
    [InlineData("0 0 1 1 *")]
    [InlineData("*/15 * * * *")]
    [InlineData("0 0 * * SUN")]
    public void AnInRangeField_IsAccepted(string expression)
    {
        Assert.True(CronExpression.IsValidExpression(UnixCron.ToQuartz(expression)));
    }

    /// <summary>
    /// A schedule takes part in state equality and <c>StateChanged</c> fires off it, so two
    /// spellings of one schedule comparing unequal would report an edit that never happened. The
    /// project learned this once already with tags.
    /// </summary>
    [Theory]
    [InlineData("0 0 * * *", "0  0  *  *  *")]
    [InlineData("0 0 * * MON-FRI", "0 0 * * mon-fri")]
    [InlineData("0 3 * * *", "  0 3 * * *  ")]
    public void TwoSpellingsOfOneSchedule_AreEqual(string left, string right)
    {
        Assert.Equal(PulseSchedule.Cron(left), PulseSchedule.Cron(right));
        Assert.Equal(PulseSchedule.Cron(left).GetHashCode(), PulseSchedule.Cron(right).GetHashCode());
    }

    [Fact]
    public void TwoSpellingsOfOneSchedule_DoNotLookLikeAStateChange()
    {
        var before = new PulseCheckerState { Schedule = PulseSchedule.Cron("0 0 * * MON-FRI") };
        var after = new PulseCheckerState { Schedule = PulseSchedule.Cron("0  0  *  *  mon-fri") };

        Assert.Equal(before, after);
    }

    [Fact]
    public void ScheduleThatGenuinelyDiffers_StillComparesUnequal()
    {
        Assert.NotEqual(PulseSchedule.Cron("0 0 * * *"), PulseSchedule.Cron("0 3 * * *"));
    }
}
