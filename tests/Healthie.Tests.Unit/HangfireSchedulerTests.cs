using Hangfire;
using Hangfire.Common;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Scheduling;
using Healthie.Scheduling.Hangfire;

namespace Healthie.Tests.Unit;

/// <summary>
/// Hangfire has exactly one way to say "repeatedly" -- a recurring job with a cron expression -- so
/// every schedule has to become cron or be refused. Approximating would be the worst outcome: a
/// check asked to run every seven seconds and quietly running every five looks like it works.
/// </summary>
public class PeriodCronTests
{
    [Theory]
    [InlineData(1, "*/1 * * * * *")]
    [InlineData(5, "*/5 * * * * *")]
    [InlineData(30, "*/30 * * * * *")]
    [InlineData(60, "*/1 * * * *")]
    [InlineData(300, "*/5 * * * *")]
    [InlineData(1800, "*/30 * * * *")]
    [InlineData(3600, "0 */1 * * *")]
    [InlineData(21600, "0 */6 * * *")]
    [InlineData(86400, "0 0 * * *")]
    public void APeriodThatDividesEvenly_BecomesCron(int seconds, string expected)
    {
        Assert.Equal(expected, PeriodCron.FromPeriod(TimeSpan.FromSeconds(seconds)));
    }

    /// <summary>
    /// Seven seconds does not divide a minute. A cron expression firing at :00, :07 … :56 would
    /// then wait four seconds rather than seven, so there is no honest translation.
    /// </summary>
    [Theory]
    [InlineData(7)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(7 * 60)]
    [InlineData(5 * 3600)]
    [InlineData(48 * 3600)]
    public void APeriodThatDoesNot_HasNoCronExpression(int seconds)
    {
        Assert.Null(PeriodCron.FromPeriod(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void ANonPositivePeriod_HasNoCronExpression(int seconds)
    {
        Assert.Null(PeriodCron.FromPeriod(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void ASubSecondPeriod_HasNoCronExpression()
    {
        Assert.Null(PeriodCron.FromPeriod(TimeSpan.FromMilliseconds(250)));
    }

    /// <summary>
    /// Hangfire parses cron with Cronos, the same standard Unix syntax a schedule carries, so an
    /// expression needs no translation -- unlike Quartz, whose dialect differs.
    /// </summary>
    [Fact]
    public void ACronSchedule_PassesThroughUntouched()
    {
        Assert.Equal("0 3 * * 1-5", PeriodCron.From(PulseSchedule.Cron("0 3 * * 1-5"), "any"));
    }

    [Fact]
    public void APeriodWithNoCronExpression_IsRefusedAndNamesTheChecker()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => PeriodCron.From(PulseSchedule.Every(TimeSpan.FromSeconds(7)), "seven-second-checker"));

        Assert.Contains("seven-second-checker", ex.Message, StringComparison.Ordinal);
    }
}

/// <summary>
/// Drives the scheduler against a stand-in recurring job manager, which is the seam where Hangfire
/// begins -- everything past it is Hangfire's own, and running it would need storage and a server.
/// </summary>
public class HangfirePulseSchedulerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class RecordingRecurringJobManager : IRecurringJobManager
    {
        public List<(string Id, string Cron)> Added { get; } = [];
        public List<string> Removed { get; } = [];

        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options)
            => Added.Add((recurringJobId, cronExpression));

        public void RemoveIfExists(string recurringJobId) => Removed.Add(recurringJobId);

        public void Trigger(string recurringJobId) { }
    }

    [Fact]
    public async Task ScheduleAsync_RegistersARecurringJobOnTheRightCron()
    {
        var jobs = new RecordingRecurringJobManager();
        var scheduler = new HangfirePulseScheduler(jobs);
        var checker = new FakePulseChecker("db");

        await scheduler.ScheduleAsync(checker, PulseSchedule.Every(TimeSpan.FromHours(6)), Ct);

        var (id, cron) = Assert.Single(jobs.Added);
        Assert.Equal("healthie:db", id);
        Assert.Equal("0 */6 * * *", cron);
    }

    [Fact]
    public async Task ScheduleAsync_WithTheIntervalOverload_StillRegisters()
    {
        var jobs = new RecordingRecurringJobManager();
        var scheduler = new HangfirePulseScheduler(jobs);

        await scheduler.ScheduleAsync(new FakePulseChecker("legacy"), PulseInterval.Every30Seconds, Ct);

        Assert.Equal("*/30 * * * * *", Assert.Single(jobs.Added).Cron);
    }

    /// <summary>
    /// A schedule Hangfire cannot express must leave the recurring job already in storage running,
    /// rather than removing it and replacing it with nothing.
    /// </summary>
    [Fact]
    public async Task ScheduleAsync_WithAPeriodHangfireCannotExpress_WritesNothing()
    {
        var jobs = new RecordingRecurringJobManager();
        var scheduler = new HangfirePulseScheduler(jobs);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => scheduler.ScheduleAsync(new FakePulseChecker("odd"), PulseSchedule.Every(TimeSpan.FromSeconds(7)), Ct));

        Assert.Empty(jobs.Added);
        Assert.Empty(jobs.Removed);
    }

    [Fact]
    public async Task UnscheduleAsync_RemovesTheRecurringJob()
    {
        var jobs = new RecordingRecurringJobManager();
        var scheduler = new HangfirePulseScheduler(jobs);

        await scheduler.UnscheduleAsync(new FakePulseChecker("db"), Ct);

        Assert.Equal("healthie:db", Assert.Single(jobs.Removed));
    }

    /// <summary>
    /// The identifier is prefixed so Healthie's jobs are recognisable in the Hangfire dashboard and
    /// cannot collide with an application's own recurring job of the same name.
    /// </summary>
    [Fact]
    public async Task RecurringJobIds_ArePrefixed()
    {
        var jobs = new RecordingRecurringJobManager();
        var scheduler = new HangfirePulseScheduler(jobs);

        await scheduler.ScheduleAsync(new FakePulseChecker("orders"), PulseInterval.EveryMinute, Ct);

        Assert.StartsWith("healthie:", Assert.Single(jobs.Added).Id, StringComparison.Ordinal);
    }
}
