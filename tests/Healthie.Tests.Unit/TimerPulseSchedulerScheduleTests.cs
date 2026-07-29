using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Scheduling;
using Healthie.DependencyInjection;
using System.Diagnostics;

namespace Healthie.Tests.Unit;

/// <summary>
/// The point of a schedule is the checks the interval enum could never express -- anything past
/// five minutes, and anything shaped like "at 03:00". These drive the scheduler rather than
/// inspecting it, because a schedule that is stored but never fires looks identical to one that
/// works.
/// </summary>
public class TimerPulseSchedulerScheduleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Waits for a condition, so a slow agent does not turn into a flaky assertion.</summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(25, Ct);
        }

        return condition();
    }

    /// <summary>
    /// Installing a schedule was "stop the old one, then start the new one" -- two steps, which a
    /// second caller could interleave. Both would install a timer, only the last would be in the
    /// dictionary, and the other kept triggering with nothing able to reach it: unschedulable, and
    /// holding a linked CancellationTokenSource that was never disposed.
    /// </summary>
    /// <remarks>
    /// Driven rather than inspected, because the orphan is by definition the one the scheduler can
    /// no longer see. What it does still do is trigger the checker, so that is what is asserted:
    /// after unscheduling, nothing may trigger again.
    /// </remarks>
    [Fact]
    public async Task UnschedulingAfterConcurrentSchedules_StopsEveryTimer()
    {
        await using var scheduler = new TimerPulseScheduler();
        var checker = new FakePulseChecker("racing-schedules");

        var schedule = PulseSchedule.Every(TimeSpan.FromMilliseconds(20));

        // Enough threads that at least one pair overlaps, few enough not to swamp the pool.
        const int ScheduleAttempts = 16;

        // Task.Run, not a bare call: nothing in the unfixed ScheduleAsync yields, so calling it in
        // a loop runs each one to completion before the next starts and no two ever overlap. The
        // race needs real threads.
        using var readyToRace = new Barrier(ScheduleAttempts);

        await Task.WhenAll(Enumerable.Range(0, ScheduleAttempts).Select(_ => Task.Run(
            () =>
            {
                readyToRace.SignalAndWait(Ct);
                return scheduler.ScheduleAsync(checker, schedule, Ct);
            },
            Ct)));

        Assert.True(
            await WaitUntilAsync(() => checker.TriggerCount > 0, TimeSpan.FromSeconds(5)),
            "the checker never triggered at all, so stopping it would prove nothing");

        await scheduler.UnscheduleAsync(checker, Ct);

        // Anything still running gets a generous window to show itself.
        await Task.Delay(250, Ct);
        var afterUnscheduling = checker.TriggerCount;
        await Task.Delay(500, Ct);

        Assert.Equal(afterUnscheduling, checker.TriggerCount);
    }

    [Fact]
    public async Task ScheduleAsync_WithACronSchedule_TriggersTheChecker()
    {
        await using var scheduler = new TimerPulseScheduler();
        var checker = new FakePulseChecker("cron-fires");

        // Six fields, so the leading one is seconds: every second.
        await scheduler.ScheduleAsync(checker, PulseSchedule.Cron("* * * * * *"), Ct);

        Assert.True(
            await WaitUntilAsync(() => checker.TriggerCount > 0, TimeSpan.FromSeconds(5)),
            "the cron schedule never triggered the checker");
    }

    [Fact]
    public async Task ScheduleAsync_WithAPeriodSchedule_TriggersTheChecker()
    {
        await using var scheduler = new TimerPulseScheduler();
        var checker = new FakePulseChecker("period-fires");

        await scheduler.ScheduleAsync(checker, PulseSchedule.Every(TimeSpan.FromMilliseconds(100)), Ct);

        Assert.True(
            await WaitUntilAsync(() => checker.TriggerCount > 0, TimeSpan.FromSeconds(5)),
            "the period schedule never triggered the checker");
    }

    /// <summary>
    /// The whole reason this type exists: six hours is not something <see cref="PulseInterval"/>
    /// can say, and before schedules it could not be asked for at all.
    /// </summary>
    [Fact]
    public async Task ScheduleAsync_WithAPeriodBeyondTheIntervalEnum_IsAccepted()
    {
        await using var scheduler = new TimerPulseScheduler();
        var checker = new FakePulseChecker("six-hourly");

        await scheduler.ScheduleAsync(checker, PulseSchedule.Every(TimeSpan.FromHours(6)), Ct);

        // Nothing should have fired yet -- the first occurrence is six hours out, not immediate.
        await Task.Delay(200, Ct);
        Assert.Equal(0, checker.TriggerCount);
    }

    [Fact]
    public async Task ScheduleAsync_WithAnUnparseableCronExpression_Throws()
    {
        await using var scheduler = new TimerPulseScheduler();
        var checker = new FakePulseChecker("bad-cron");

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => scheduler.ScheduleAsync(checker, PulseSchedule.Cron("not a cron expression"), Ct));

        Assert.Contains("bad-cron", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A rejected schedule must not take the working one down with it. Cancelling first and
    /// validating second would leave a checker that was running fine stopped by a typo.
    /// </summary>
    [Fact]
    public async Task ScheduleAsync_WhenTheNewScheduleIsInvalid_LeavesTheRunningOneAlone()
    {
        await using var scheduler = new TimerPulseScheduler();
        var checker = new FakePulseChecker("survives-a-typo");

        await scheduler.ScheduleAsync(checker, PulseSchedule.Every(TimeSpan.FromMilliseconds(100)), Ct);
        Assert.True(await WaitUntilAsync(() => checker.TriggerCount > 0, TimeSpan.FromSeconds(5)));

        await Assert.ThrowsAsync<ArgumentException>(
            () => scheduler.ScheduleAsync(checker, PulseSchedule.Cron("nonsense"), Ct));

        var countAfterTheBadRequest = checker.TriggerCount;
        Assert.True(
            await WaitUntilAsync(() => checker.TriggerCount > countAfterTheBadRequest, TimeSpan.FromSeconds(5)),
            "the original schedule stopped after a malformed one was rejected");
    }

    [Fact]
    public async Task UnscheduleAsync_StopsTheChecker()
    {
        await using var scheduler = new TimerPulseScheduler();
        var checker = new FakePulseChecker("stops");

        await scheduler.ScheduleAsync(checker, PulseSchedule.Every(TimeSpan.FromMilliseconds(100)), Ct);
        Assert.True(await WaitUntilAsync(() => checker.TriggerCount > 0, TimeSpan.FromSeconds(5)));

        await scheduler.UnscheduleAsync(checker, Ct);
        await Task.Delay(200, Ct);

        var settled = checker.TriggerCount;
        await Task.Delay(400, Ct);
        Assert.Equal(settled, checker.TriggerCount);
    }

    /// <summary>
    /// The enum overload has to keep behaving exactly as it did; it is what every existing caller
    /// and every stored state still uses.
    /// </summary>
    [Fact]
    public async Task ScheduleAsync_WithTheIntervalOverload_StillTriggers()
    {
        await using var scheduler = new TimerPulseScheduler();
        var checker = new FakePulseChecker("legacy-enum");

        await scheduler.ScheduleAsync(checker, PulseInterval.EverySecond, Ct);

        Assert.True(
            await WaitUntilAsync(() => checker.TriggerCount > 0, TimeSpan.FromSeconds(5)),
            "the interval overload stopped triggering");
    }
}

/// <summary>
/// A scheduler written against the interface before schedules existed implements only the interval
/// overload. The default implementation has to keep it working for what an interval can express,
/// and refuse -- loudly -- what it cannot, rather than quietly running a six-hourly check every
/// five minutes.
/// </summary>
public class LegacySchedulerCompatibilityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class IntervalOnlyScheduler : IPulseScheduler
    {
        public PulseInterval? Scheduled { get; private set; }

        public Task ScheduleAsync(IPulseChecker checker, PulseInterval interval, CancellationToken cancellationToken = default)
        {
            Scheduled = interval;
            return Task.CompletedTask;
        }

        public Task UnscheduleAsync(IPulseChecker checker, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task ASchedulePredatingTheInterface_IsForwardedToTheIntervalOverload()
    {
        var implementation = new IntervalOnlyScheduler();

        // Typed as the interface deliberately: a default implementation is not a member of the
        // implementing class, so it is reachable only through IPulseScheduler. That is how the
        // library calls it -- PulsesScheduler holds the interface -- and the only way to reach it.
        IPulseScheduler scheduler = implementation;

        await scheduler.ScheduleAsync(
            new FakePulseChecker("forwarded"),
            PulseSchedule.Every(TimeSpan.FromSeconds(15)),
            Ct);

        Assert.Equal(PulseInterval.Every15Seconds, implementation.Scheduled);
    }

    [Fact]
    public async Task AScheduleTheIntervalEnumCannotExpress_IsRefused()
    {
        var implementation = new IntervalOnlyScheduler();
        IPulseScheduler scheduler = implementation;

        await Assert.ThrowsAsync<NotSupportedException>(
            () => scheduler.ScheduleAsync(new FakePulseChecker("refused"), PulseSchedule.Cron("0 3 * * *"), Ct));

        Assert.Null(implementation.Scheduled);
    }
}
