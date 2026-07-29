using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Scheduling;
using Healthie.DependencyInjection;
using Healthie.LeaderElection;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.Tests.Unit;

/// <summary>
/// Without leader election every replica runs every check: three replicas ask a database three
/// times whether it is healthy, three sets of results race to write the same state under
/// last-write-wins, and one outage pages somebody three times. These pin the behaviour that stops
/// that, and the behaviour that must survive a leader dying.
/// </summary>
public class LeaderElectionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Records what it was asked to schedule, standing in for a real scheduler.</summary>
    private sealed class RecordingScheduler : IPulseScheduler
    {
        public List<string> Scheduled { get; } = [];
        public List<string> Unscheduled { get; } = [];

        public Task ScheduleAsync(IPulseChecker checker, PulseInterval interval, CancellationToken cancellationToken = default)
            => ScheduleAsync(checker, PulseSchedule.FromInterval(interval), cancellationToken);

        public Task ScheduleAsync(IPulseChecker checker, PulseSchedule schedule, CancellationToken cancellationToken = default)
        {
            Scheduled.Add(checker.Name);
            return Task.CompletedTask;
        }

        public Task UnscheduleAsync(IPulseChecker checker, CancellationToken cancellationToken = default)
        {
            Unscheduled.Add(checker.Name);
            return Task.CompletedTask;
        }
    }

    private static readonly PulseSchedule EverySecond = PulseSchedule.Every(TimeSpan.FromSeconds(1));

    [Fact]
    public async Task AFollower_DoesNotRunChecks()
    {
        var inner = new RecordingScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner);

        await scheduler.ScheduleAsync(new FakePulseChecker("db"), EverySecond, Ct);

        Assert.False(scheduler.IsLeader);
        Assert.Empty(inner.Scheduled);
    }

    /// <summary>
    /// A checker registered while following has to start when leadership is taken, or a replica
    /// that was not leading at startup would never run anything.
    /// </summary>
    [Fact]
    public async Task TakingLeadership_StartsEverythingRequestedWhileFollowing()
    {
        var inner = new RecordingScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner);

        await scheduler.ScheduleAsync(new FakePulseChecker("db"), EverySecond, Ct);
        await scheduler.ScheduleAsync(new FakePulseChecker("cache"), EverySecond, Ct);

        await scheduler.BecomeLeaderAsync(Ct);

        Assert.True(scheduler.IsLeader);
        Assert.Equal(["cache", "db"], inner.Scheduled.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ALeader_SchedulesImmediately()
    {
        var inner = new RecordingScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner);

        await scheduler.BecomeLeaderAsync(Ct);
        await scheduler.ScheduleAsync(new FakePulseChecker("db"), EverySecond, Ct);

        Assert.Equal("db", Assert.Single(inner.Scheduled));
    }

    [Fact]
    public async Task StandingDown_StopsEveryCheck()
    {
        var inner = new RecordingScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner);

        await scheduler.ScheduleAsync(new FakePulseChecker("db"), EverySecond, Ct);
        await scheduler.BecomeLeaderAsync(Ct);
        await scheduler.StandDownAsync(Ct);

        Assert.False(scheduler.IsLeader);
        Assert.Contains("db", inner.Unscheduled);
    }

    /// <summary>
    /// Leadership moves back and forth in a rolling deploy, so what was requested has to outlive
    /// standing down -- otherwise a replica that regains leadership runs nothing.
    /// </summary>
    [Fact]
    public async Task RegainingLeadership_StartsEverythingAgain()
    {
        var inner = new RecordingScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner);

        await scheduler.ScheduleAsync(new FakePulseChecker("db"), EverySecond, Ct);
        await scheduler.BecomeLeaderAsync(Ct);
        await scheduler.StandDownAsync(Ct);
        inner.Scheduled.Clear();

        await scheduler.BecomeLeaderAsync(Ct);

        Assert.Equal("db", Assert.Single(inner.Scheduled));
    }

    [Fact]
    public async Task TakingLeadershipTwice_DoesNotScheduleTwice()
    {
        var inner = new RecordingScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner);

        await scheduler.ScheduleAsync(new FakePulseChecker("db"), EverySecond, Ct);
        await scheduler.BecomeLeaderAsync(Ct);
        await scheduler.BecomeLeaderAsync(Ct);

        Assert.Single(inner.Scheduled);
    }

    /// <summary>
    /// Unscheduling has to reach the inner scheduler even while following: it may still hold the
    /// checker from a period when this replica led.
    /// </summary>
    [Fact]
    public async Task UnschedulingWhileFollowing_StillReachesTheInnerScheduler()
    {
        var inner = new RecordingScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner);
        var checker = new FakePulseChecker("db");

        await scheduler.ScheduleAsync(checker, EverySecond, Ct);
        await scheduler.UnscheduleAsync(checker, Ct);
        await scheduler.BecomeLeaderAsync(Ct);

        Assert.Contains("db", inner.Unscheduled);
        Assert.Empty(inner.Scheduled);
    }
}

/// <summary>
/// A lease has to expire rather than be handed over, because the failure it exists for is the
/// replica that stops without saying anything.
/// </summary>
public class LeaseProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OnlyOneHolder_WinsAtATime()
    {
        var leases = new InMemoryLeaseProvider();

        Assert.True(await leases.TryAcquireAsync("scheduler", "replica-1", TimeSpan.FromMinutes(1), Ct));
        Assert.False(await leases.TryAcquireAsync("scheduler", "replica-2", TimeSpan.FromMinutes(1), Ct));
    }

    [Fact]
    public async Task TheHolder_CanRenew()
    {
        var leases = new InMemoryLeaseProvider();

        Assert.True(await leases.TryAcquireAsync("scheduler", "replica-1", TimeSpan.FromMinutes(1), Ct));
        Assert.True(await leases.TryAcquireAsync("scheduler", "replica-1", TimeSpan.FromMinutes(1), Ct));
    }

    /// <summary>
    /// The case the whole design turns on: a leader that was killed never releases anything, so
    /// another replica has to be able to take over once the lease lapses.
    /// </summary>
    [Fact]
    public async Task AnExpiredLease_CanBeTakenByAnother()
    {
        var leases = new InMemoryLeaseProvider();

        Assert.True(await leases.TryAcquireAsync("scheduler", "replica-1", TimeSpan.FromMilliseconds(1), Ct));
        await Task.Delay(50, Ct);

        Assert.True(await leases.TryAcquireAsync("scheduler", "replica-2", TimeSpan.FromMinutes(1), Ct));
    }

    [Fact]
    public async Task ReleasingLetsTheNextReplicaTakeOverAtOnce()
    {
        var leases = new InMemoryLeaseProvider();

        await leases.TryAcquireAsync("scheduler", "replica-1", TimeSpan.FromMinutes(1), Ct);
        await leases.ReleaseAsync("scheduler", "replica-1", Ct);

        Assert.True(await leases.TryAcquireAsync("scheduler", "replica-2", TimeSpan.FromMinutes(1), Ct));
    }

    /// <summary>
    /// A replica that lost the lease and then shut down must not take it from whoever holds it now.
    /// </summary>
    [Fact]
    public async Task ANonHolder_CannotReleaseSomebodyElsesLease()
    {
        var leases = new InMemoryLeaseProvider();

        await leases.TryAcquireAsync("scheduler", "replica-1", TimeSpan.FromMinutes(1), Ct);
        await leases.ReleaseAsync("scheduler", "replica-2", Ct);

        Assert.False(await leases.TryAcquireAsync("scheduler", "replica-2", TimeSpan.FromMinutes(1), Ct));
    }

    [Fact]
    public async Task DifferentLeases_DoNotContendWithEachOther()
    {
        var leases = new InMemoryLeaseProvider();

        Assert.True(await leases.TryAcquireAsync("scheduler-a", "replica-1", TimeSpan.FromMinutes(1), Ct));
        Assert.True(await leases.TryAcquireAsync("scheduler-b", "replica-2", TimeSpan.FromMinutes(1), Ct));
    }
}

/// <summary>
/// Registration order matters here, unlike everywhere else in this library, so it has to fail
/// loudly rather than wrap the wrong scheduler.
/// </summary>
public class LeaderElectionRegistrationTests
{
    [Fact]
    public void RegisteringBeforeAScheduler_IsRefusedWithAnExplanation()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddHealthieLeaderElection());

        Assert.Contains("after AddHealthie", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisteringAfterAScheduler_WrapsIt()
    {
        var services = new ServiceCollection();
        services.AddHealthie(typeof(LeaderElectionRegistrationTests).Assembly);
        services.AddHealthieLeaderElection();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<LeaderElectedPulseScheduler>(provider.GetRequiredService<IPulseScheduler>());
    }
}
