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
        var scheduler = new CoravelPulseScheduler();
        var checker = new FakePulseChecker("once-per-occurrence");

        // Long enough that the occurrence this consumes cannot come round again between the two
        // ticks below. A millisecond here made the second tick legitimately due, so the test failed
        // whenever the machine was busy enough to put a millisecond between them -- which said
        // nothing about whether one occurrence can fire twice.
        var period = TimeSpan.FromMilliseconds(500);

        await scheduler.ScheduleAsync(checker, PulseSchedule.Every(period), Ct);

        // Wait for that one occurrence to come due.
        await Task.Delay(period + TimeSpan.FromMilliseconds(50), Ct);

        await scheduler.TickAsync(Ct);
        var afterFirst = checker.TriggerCount;

        // Straight away again: the occurrence is spent and the next is half a second off.
        await scheduler.TickAsync(Ct);

        Assert.Equal(1, afterFirst);
        Assert.Equal(1, checker.TriggerCount);
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
