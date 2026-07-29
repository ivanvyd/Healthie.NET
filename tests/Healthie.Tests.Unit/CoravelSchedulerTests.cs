using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Scheduling;
using Healthie.Scheduling.Coravel;

namespace Healthie.Tests.Unit;

/// <summary>
/// Coravel has no API for removing a scheduled job -- its <c>IScheduler</c> exposes only
/// <c>Schedule</c> methods -- while <see cref="IPulseScheduler"/> is required to schedule and
/// unschedule at runtime. So the due times live in this scheduler and Coravel only supplies the
/// tick. These drive that tick directly, which is what the single Coravel job does.
/// </summary>
public class CoravelSchedulerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACheckerIsNotTriggered_BeforeItIsDue()
    {
        var scheduler = new CoravelPulseScheduler();
        var checker = new FakePulseChecker("not-yet-due");

        await scheduler.ScheduleAsync(checker, PulseSchedule.Every(TimeSpan.FromHours(1)), Ct);
        await scheduler.TickAsync(Ct);

        Assert.Equal(0, checker.TriggerCount);
    }

    [Fact]
    public async Task ACheckerIsTriggered_OnceItIsDue()
    {
        var scheduler = new CoravelPulseScheduler();
        var checker = new FakePulseChecker("due");

        await scheduler.ScheduleAsync(checker, PulseSchedule.Every(TimeSpan.FromMilliseconds(1)), Ct);
        await Task.Delay(20, Ct);
        await scheduler.TickAsync(Ct);

        Assert.Equal(1, checker.TriggerCount);
    }

    /// <summary>
    /// The whole reason the due times live here: Coravel cannot take a job back, so unscheduling
    /// has to stop the checker being considered due.
    /// </summary>
    [Fact]
    public async Task AnUnscheduledChecker_IsNotTriggered()
    {
        var scheduler = new CoravelPulseScheduler();
        var checker = new FakePulseChecker("removed");

        await scheduler.ScheduleAsync(checker, PulseSchedule.Every(TimeSpan.FromMilliseconds(1)), Ct);
        await scheduler.UnscheduleAsync(checker, Ct);
        await Task.Delay(20, Ct);
        await scheduler.TickAsync(Ct);

        Assert.Equal(0, checker.TriggerCount);
    }

    [Fact]
    public async Task ReschedulingAChecker_ReplacesItsScheduleRatherThanAddingOne()
    {
        var scheduler = new CoravelPulseScheduler();
        var checker = new FakePulseChecker("rescheduled");

        await scheduler.ScheduleAsync(checker, PulseSchedule.Every(TimeSpan.FromMilliseconds(1)), Ct);
        await scheduler.ScheduleAsync(checker, PulseSchedule.Every(TimeSpan.FromMilliseconds(1)), Ct);
        await Task.Delay(20, Ct);
        await scheduler.TickAsync(Ct);

        Assert.Equal(1, checker.TriggerCount);
    }

    /// <summary>
    /// A tick must move the due time on before triggering, or a checker due once would be triggered
    /// by every tick until its check finished.
    /// </summary>
    [Fact]
    public async Task ASingleDueOccurrence_TriggersOnceEvenIfTicksKeepComing()
    {
        // The clock is controlled, so this asks the question directly instead of sleeping and hoping.
        // It used to schedule a one-millisecond period and assert a second tick would not fire --
        // but with that period the second tick is legitimately due after a millisecond, so it failed
        // whenever the machine was busy enough to put one between the two calls, which says nothing
        // about whether a single occurrence can fire twice.
        var clock = new SettableTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var scheduler = new CoravelPulseScheduler(timeProvider: clock);
        var checker = new FakePulseChecker("once-per-occurrence");

        var period = TimeSpan.FromMinutes(1);
        await scheduler.ScheduleAsync(checker, PulseSchedule.Every(period), Ct);

        // Exactly one occurrence has come due.
        clock.Advance(period);

        await scheduler.TickAsync(Ct);
        var afterFirst = checker.TriggerCount;

        // Ticks keep coming, and the clock has not moved: the occurrence is spent.
        await scheduler.TickAsync(Ct);
        await scheduler.TickAsync(Ct);

        Assert.Equal(1, afterFirst);
        Assert.Equal(1, checker.TriggerCount);

        // And the next occurrence still arrives when it should.
        clock.Advance(period);
        await scheduler.TickAsync(Ct);

        Assert.Equal(2, checker.TriggerCount);
    }

    /// <summary>One checker throwing must not stop the rest of the tick.</summary>
    [Fact]
    public async Task ACheckerThatThrows_DoesNotStopTheOthers()
    {
        var scheduler = new CoravelPulseScheduler();
        var failing = new FakePulseChecker("throws") { ThrowOnTrigger = new InvalidOperationException("boom") };
        var working = new FakePulseChecker("still-runs");

        await scheduler.ScheduleAsync(failing, PulseSchedule.Every(TimeSpan.FromMilliseconds(1)), Ct);
        await scheduler.ScheduleAsync(working, PulseSchedule.Every(TimeSpan.FromMilliseconds(1)), Ct);
        await Task.Delay(20, Ct);

        await scheduler.TickAsync(Ct);

        Assert.Equal(1, working.TriggerCount);
    }

    [Fact]
    public async Task ACronSchedule_IsAccepted()
    {
        var scheduler = new CoravelPulseScheduler();
        var checker = new FakePulseChecker("cron");

        await scheduler.ScheduleAsync(checker, PulseSchedule.Cron("* * * * * *"), Ct);
        await Task.Delay(1200, Ct);
        await scheduler.TickAsync(Ct);

        Assert.True(checker.TriggerCount > 0, "a once-a-second cron schedule should have come due");
    }

    [Fact]
    public async Task AnUnparseableCronExpression_IsRefusedAndNamesTheChecker()
    {
        var scheduler = new CoravelPulseScheduler();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => scheduler.ScheduleAsync(new FakePulseChecker("bad-cron"), PulseSchedule.Cron("nonsense"), Ct));

        Assert.Contains("bad-cron", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>A rejected schedule must leave a working one alone.</summary>
    [Fact]
    public async Task WhenANewScheduleIsInvalid_TheRunningOneSurvives()
    {
        var scheduler = new CoravelPulseScheduler();
        var checker = new FakePulseChecker("survives-a-typo");

        await scheduler.ScheduleAsync(checker, PulseSchedule.Every(TimeSpan.FromMilliseconds(1)), Ct);
        await Assert.ThrowsAsync<ArgumentException>(
            () => scheduler.ScheduleAsync(checker, PulseSchedule.Cron("nonsense"), Ct));

        await Task.Delay(20, Ct);
        await scheduler.TickAsync(Ct);

        Assert.Equal(1, checker.TriggerCount);
    }

    [Fact]
    public async Task TheIntervalOverload_StillWorks()
    {
        var scheduler = new CoravelPulseScheduler();
        var checker = new FakePulseChecker("legacy-enum");

        await scheduler.ScheduleAsync(checker, PulseInterval.EverySecond, Ct);
        await Task.Delay(1100, Ct);
        await scheduler.TickAsync(Ct);

        Assert.Equal(1, checker.TriggerCount);
    }
}
