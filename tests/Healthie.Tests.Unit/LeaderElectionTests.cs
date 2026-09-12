using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Healthie.Abstractions.StateProviding;
using Healthie.DependencyInjection;
using Healthie.LeaderElection;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

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
        private readonly TaskCompletionSource _unscheduleAttempted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentDictionary<string, byte> Active { get; } = new(StringComparer.Ordinal);
        public ConcurrentQueue<string> Scheduled { get; } = [];
        public ConcurrentQueue<string> Unscheduled { get; } = [];
        public ConcurrentDictionary<string, PulseSchedule> LastSchedules { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, int> UnscheduleFailuresRemaining { get; } = new(StringComparer.Ordinal);

        public Task UnscheduleAttempted => _unscheduleAttempted.Task;

        public bool AcceptSchedules { get; set; } = true;

        public string? ValidationError { get; set; }

        public Task ScheduleAsync(IPulseChecker checker, PulseInterval interval, CancellationToken cancellationToken = default)
            => ScheduleAsync(checker, PulseSchedule.FromInterval(interval), cancellationToken);

        public Task ScheduleAsync(IPulseChecker checker, PulseSchedule schedule, CancellationToken cancellationToken = default)
        {
            Scheduled.Enqueue(checker.Name);
            Active[checker.Name] = 0;
            LastSchedules[checker.Name] = schedule;
            return Task.CompletedTask;
        }

        public bool TryValidateSchedule(PulseSchedule schedule, out string? error)
        {
            error = ValidationError;
            return AcceptSchedules;
        }

        public Task UnscheduleAsync(IPulseChecker checker, CancellationToken cancellationToken = default)
        {
            Unscheduled.Enqueue(checker.Name);
            _unscheduleAttempted.TrySetResult();
            if (UnscheduleFailuresRemaining.TryGetValue(checker.Name, out var remaining) && remaining > 0)
            {
                UnscheduleFailuresRemaining[checker.Name] = remaining - 1;
                throw new InvalidOperationException($"could not stop {checker.Name}");
            }

            Active.TryRemove(checker.Name, out _);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Pauses one schedule call so tests can put a competing operation precisely in the old gap.
    /// </summary>
    private sealed class BlockingScheduler : IPulseScheduler
    {
        private readonly object _sync = new();
        private TaskCompletionSource _entered = NewSignal();
        private TaskCompletionSource _release = NewSignal();
        private readonly TaskCompletionSource _scheduled = NewSignal();
        private int _blockNext;

        public Dictionary<string, PulseSchedule> Active { get; } = new(StringComparer.Ordinal);

        public Task Entered => _entered.Task;

        public Task Scheduled => _scheduled.Task;

        public void BlockNextSchedule()
        {
            _entered = NewSignal();
            _release = NewSignal();
            Interlocked.Exchange(ref _blockNext, 1);
        }

        public void ReleaseSchedule() => _release.TrySetResult();

        public Task ScheduleAsync(
            IPulseChecker checker,
            PulseInterval interval,
            CancellationToken cancellationToken = default) =>
            ScheduleAsync(checker, PulseSchedule.FromInterval(interval), cancellationToken);

        public async Task ScheduleAsync(
            IPulseChecker checker,
            PulseSchedule schedule,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _blockNext, 0) == 1)
            {
                _entered.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }

            lock (_sync)
            {
                Active[checker.Name] = schedule;
            }
            _scheduled.TrySetResult();
        }

        public Task UnscheduleAsync(IPulseChecker checker, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                Active.Remove(checker.Name);
            }

            return Task.CompletedTask;
        }

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Fails individual reads so leader reconciliation proves it uses the bulk path.</summary>
    private sealed class BulkOnlyStateProvider : IStateProvider
    {
        private readonly Dictionary<string, object> _states = new(StringComparer.Ordinal);

        public int BulkReads { get; private set; }

        public Task<TState?> GetStateAsync<TState>(string name, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Leader reconciliation made an individual state read.");

        public Task SetStateAsync<TState>(
            string name,
            TState state,
            CancellationToken cancellationToken = default)
        {
            _states[name] = state!;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, TState>> GetStatesAsync<TState>(
            IEnumerable<string> names,
            CancellationToken cancellationToken = default)
        {
            BulkReads++;
            IReadOnlyDictionary<string, TState> found = names
                .Where(_states.ContainsKey)
                .ToDictionary(name => name, name => (TState)_states[name], StringComparer.Ordinal);
            return Task.FromResult(found);
        }
    }

    private sealed class BlockingLeaseProvider : ILeaseProvider
    {
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public int Releases { get; private set; }

        public async Task<bool> TryAcquireAsync(
            string leaseName,
            string holderId,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return false;
        }

        public Task ReleaseAsync(
            string leaseName,
            string holderId,
            CancellationToken cancellationToken = default)
        {
            Releases++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLeaseProvider : ILeaseProvider
    {
        public int Releases { get; private set; }

        public Task<bool> TryAcquireAsync(
            string leaseName,
            string holderId,
            TimeSpan duration,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task ReleaseAsync(
            string leaseName,
            string holderId,
            CancellationToken cancellationToken = default)
        {
            Releases++;
            return Task.CompletedTask;
        }
    }

    private sealed class ConcurrencyProbeScheduler : IPulseScheduler
    {
        private int _active;
        private int _calls;
        private int _maxActive;
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaxActive => Volatile.Read(ref _maxActive);

        public int Calls => Volatile.Read(ref _calls);

        public Task Entered => _entered.Task;

        public Task ScheduleAsync(
            IPulseChecker checker,
            PulseInterval interval,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task UnscheduleAsync(
            IPulseChecker checker,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            _entered.TrySetResult();
            var active = Interlocked.Increment(ref _active);
            int observed;
            do
            {
                observed = Volatile.Read(ref _maxActive);
            }
            while (active > observed && Interlocked.CompareExchange(ref _maxActive, active, observed) != observed);

            try
            {
                await Task.Delay(25, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
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

    [Fact]
    public async Task APartialStandDown_TriesEveryCheckerAndCanRetryTheFailure()
    {
        var inner = new RecordingScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner);
        var first = new FakePulseChecker("first");
        var second = new FakePulseChecker("second");
        await scheduler.ScheduleAsync(first, EverySecond, Ct);
        await scheduler.ScheduleAsync(second, EverySecond, Ct);
        await scheduler.BecomeLeaderAsync(Ct);
        inner.UnscheduleFailuresRemaining[first.Name] = 1;

        await Assert.ThrowsAsync<InvalidOperationException>(() => scheduler.StandDownAsync(Ct));

        Assert.Contains(first.Name, inner.Unscheduled);
        Assert.Contains(second.Name, inner.Unscheduled);
        Assert.False(scheduler.IsLeader);

        await scheduler.StandDownAsync(Ct);

        Assert.Equal(2, inner.Unscheduled.Count(name => name == first.Name));
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

    [Fact]
    public void ValidationIsDelegatedToTheWrappedScheduler()
    {
        var inner = new RecordingScheduler
        {
            AcceptSchedules = false,
            ValidationError = "the wrapped scheduler rejected it",
        };
        IPulseScheduler scheduler = new LeaderElectedPulseScheduler(inner);

        Assert.False(scheduler.TryValidateSchedule(EverySecond, out var error));
        Assert.Equal(inner.ValidationError, error);
    }

    /// <summary>
    /// The cached request says what this replica knew at startup. Persisted state is authoritative
    /// when it later takes over, because another replica may have paused the checker meanwhile.
    /// </summary>
    [Fact]
    public async Task TakingLeadership_DoesNotResumeACheckerPausedByAnotherReplica()
    {
        var states = new InMemoryStateProvider();
        using var local = new AlwaysHealthyPulseChecker(states);
        using var remote = new AlwaysHealthyPulseChecker(states);
        await local.TriggerAsync(Ct);

        var inner = new RecordingScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner, [local], states);
        await scheduler.ScheduleAsync(local, EverySecond, Ct);
        await scheduler.BecomeLeaderAsync(Ct);
        await remote.StopAsync(Ct);

        await scheduler.BecomeLeaderAsync(Ct);

        Assert.Contains(local.Name, inner.Unscheduled);
    }

    [Fact]
    public async Task FreshLeader_RemovesAnInactiveDurableScheduleLeftByThePreviousProcess()
    {
        var states = new InMemoryStateProvider();
        using var local = new AlwaysHealthyPulseChecker(states);
        using var remote = new AlwaysHealthyPulseChecker(states);
        await local.TriggerAsync(Ct);
        await remote.StopAsync(Ct);
        var inner = new RecordingScheduler();
        await inner.ScheduleAsync(local, EverySecond, Ct);
        inner.Scheduled.Clear();
        var scheduler = new LeaderElectedPulseScheduler(inner, [local], states);

        await scheduler.BecomeLeaderAsync(Ct);

        Assert.Equal(local.Name, Assert.Single(inner.Unscheduled));
        Assert.Empty(inner.Scheduled);
    }

    [Fact]
    public async Task TakingLeadership_UsesAScheduleChangedByAnotherReplica()
    {
        var states = new InMemoryStateProvider();
        using var local = new AlwaysHealthyPulseChecker(states);
        using var remote = new AlwaysHealthyPulseChecker(states);
        await local.TriggerAsync(Ct);

        var inner = new RecordingScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner, [local], states);
        await scheduler.ScheduleAsync(local, EverySecond, Ct);
        await scheduler.BecomeLeaderAsync(Ct);
        var remoteSchedule = PulseSchedule.Every(TimeSpan.FromMinutes(2));
        await remote.SetScheduleAsync(remoteSchedule, Ct);

        await scheduler.BecomeLeaderAsync(Ct);

        Assert.Equal(remoteSchedule, inner.LastSchedules[local.Name]);
    }

    [Fact]
    public async Task RenewalDiscoversACheckerActivatedByAnotherReplica()
    {
        var states = new InMemoryStateProvider();
        using var local = new AlwaysHealthyPulseChecker(states);
        using var remote = new AlwaysHealthyPulseChecker(states);
        await local.TriggerAsync(Ct);
        await local.StopAsync(Ct);

        var inner = new RecordingScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner, [local], states);
        await scheduler.BecomeLeaderAsync(Ct);
        Assert.Empty(inner.Scheduled);

        await remote.StartAsync(Ct);
        await scheduler.BecomeLeaderAsync(Ct);

        Assert.Contains(local.Name, inner.Scheduled);
    }

    [Fact]
    public async Task RenewalReadsRegisteredCheckerStateInOneBulkCall()
    {
        var states = new BulkOnlyStateProvider();
        var first = new FakePulseChecker("first");
        var second = new FakePulseChecker("second");
        await states.SetStateAsync(first.Name, new PulseCheckerState(PulseInterval.EverySecond), Ct);
        await states.SetStateAsync(second.Name, new PulseCheckerState(PulseInterval.EveryMinute), Ct);
        var inner = new RecordingScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner, [first, second], states);

        await scheduler.BecomeLeaderAsync(Ct);

        Assert.Equal(1, states.BulkReads);
        Assert.Equal([first.Name, second.Name], inner.Scheduled.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task MissingStoredState_UsesTheScheduleRequestedFromCodeAtStartup()
    {
        var states = new BulkOnlyStateProvider();
        var checker = new FakePulseChecker("not-stored-yet");
        var inner = new RecordingScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner, [checker], states);
        var codeDefault = PulseSchedule.Every(TimeSpan.FromMinutes(2));
        await scheduler.ScheduleAsync(checker, codeDefault, Ct);

        await scheduler.BecomeLeaderAsync(Ct);

        Assert.Equal(1, states.BulkReads);
        Assert.Equal(codeDefault, inner.LastSchedules[checker.Name]);
    }

    [Fact]
    public async Task FreshTakeover_BoundsAndParallelizesDurableInactiveCleanup()
    {
        var states = new BulkOnlyStateProvider();
        var checkers = Enumerable.Range(0, 64)
            .Select(index => new FakePulseChecker($"inactive-{index}"))
            .ToList();
        foreach (var checker in checkers)
        {
            await states.SetStateAsync(
                checker.Name,
                new PulseCheckerState(PulseInterval.EveryMinute) { IsActive = false },
                Ct);
        }
        var inner = new ConcurrencyProbeScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner, checkers, states);

        await scheduler.BecomeLeaderAsync(Ct);

        Assert.InRange(inner.MaxActive, 2, 16);
    }

    [Fact]
    public async Task CancelledStandDown_StopsStartingMoreCleanupCalls()
    {
        var inner = new ConcurrencyProbeScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner);
        var checkers = Enumerable.Range(0, 64)
            .Select(index => new FakePulseChecker($"active-{index}"))
            .ToList();
        foreach (var checker in checkers)
        {
            await scheduler.ScheduleAsync(checker, EverySecond, Ct);
        }
        await scheduler.BecomeLeaderAsync(Ct);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var standDown = scheduler.StandDownAsync(cancellation.Token);
        await inner.Entered.WaitAsync(Ct);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => standDown);
        Assert.InRange(inner.Calls, 1, 16);
    }

    [Fact]
    public async Task CancelledTakeover_TracksAndStopsSchedulesThatAlreadyStarted()
    {
        var inner = new BlockingScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner);
        var checkers = Enumerable.Range(0, 64)
            .Select(index => new FakePulseChecker($"takeover-{index}"))
            .ToList();
        foreach (var checker in checkers)
        {
            await scheduler.ScheduleAsync(checker, EverySecond, Ct);
        }
        inner.BlockNextSchedule();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var takeover = scheduler.BecomeLeaderAsync(cancellation.Token);
        await inner.Entered.WaitAsync(Ct);
        await inner.Scheduled.WaitAsync(Ct);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => takeover);
        Assert.NotEmpty(inner.Active);

        await scheduler.StandDownAsync(Ct);

        Assert.Empty(inner.Active);
    }

    [Fact]
    public async Task RejectedReplacement_DoesNotPoisonTheNextRenewal()
    {
        var inner = new RecordingScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner);
        var checker = new FakePulseChecker("keeps-good-schedule");
        await scheduler.ScheduleAsync(checker, EverySecond, Ct);
        await scheduler.BecomeLeaderAsync(Ct);
        inner.AcceptSchedules = false;
        inner.ValidationError = "invalid test schedule";

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => scheduler.ScheduleAsync(checker, PulseSchedule.Cron("bad"), Ct));
        inner.AcceptSchedules = true;
        await scheduler.BecomeLeaderAsync(Ct);

        Assert.Contains(inner.ValidationError!, error.Message);
        Assert.Equal(EverySecond, inner.LastSchedules[checker.Name]);
        Assert.Single(inner.Scheduled);
    }

    [Fact]
    public async Task ShutdownInterruptedDuringContention_StillStopsSchedulesAndReleasesTheLease()
    {
        var leases = new BlockingLeaseProvider();
        var inner = new RecordingScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner);
        var checker = new FakePulseChecker("shutdown-cleanup");
        await scheduler.ScheduleAsync(checker, EverySecond, Ct);
        await scheduler.BecomeLeaderAsync(Ct);
        var options = new LeaderElectionOptions { RenewInterval = TimeSpan.FromSeconds(1) };
        using var service = new LeaderElectionService(leases, scheduler, options);
        await service.StartAsync(CancellationToken.None);
        await leases.Entered.WaitAsync(Ct);

        await service.StopAsync(Ct);

        Assert.Contains(checker.Name, inner.Unscheduled);
        Assert.Equal(1, leases.Releases);
    }

    [Fact]
    public async Task FailedShutdownCleanup_DoesNotReleaseTheLeaseWhileACheckStillRuns()
    {
        var leases = new BlockingLeaseProvider();
        var inner = new RecordingScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner);
        var checker = new FakePulseChecker("shutdown-failure");
        await scheduler.ScheduleAsync(checker, EverySecond, Ct);
        await scheduler.BecomeLeaderAsync(Ct);
        inner.UnscheduleFailuresRemaining[checker.Name] = 1;
        var options = new LeaderElectionOptions { RenewInterval = TimeSpan.FromSeconds(1) };
        using var service = new LeaderElectionService(leases, scheduler, options);
        await service.StartAsync(CancellationToken.None);
        await leases.Entered.WaitAsync(Ct);

        await service.StopAsync(Ct);

        Assert.Equal(0, leases.Releases);
        Assert.False(scheduler.IsLeader);
    }

    [Fact]
    public async Task FailedStaleDurableCleanup_PreventsLeaseReleaseOnShutdown()
    {
        var states = new InMemoryStateProvider();
        using var checker = new AlwaysHealthyPulseChecker(states);
        await checker.TriggerAsync(Ct);
        await checker.StopAsync(Ct);
        var inner = new RecordingScheduler();
        await inner.ScheduleAsync(checker, EverySecond, Ct);
        inner.UnscheduleFailuresRemaining[checker.Name] = 10;
        var scheduler = new LeaderElectedPulseScheduler(inner, [checker], states);
        var leases = new RecordingLeaseProvider();
        var options = new LeaderElectionOptions
        {
            HolderId = "stale-cleanup-owner",
            LeaseDuration = TimeSpan.FromSeconds(1),
            RenewInterval = TimeSpan.FromMilliseconds(100),
        };
        using var service = new LeaderElectionService(leases, scheduler, options);

        await service.StartAsync(CancellationToken.None);
        await inner.UnscheduleAttempted.WaitAsync(Ct);
        await service.StopAsync(Ct);

        Assert.True(inner.Active.ContainsKey(checker.Name));
        Assert.Equal(0, leases.Releases);
    }

    [Fact]
    public async Task SlowReconciliation_DoesNotLetTheLeaseExpire()
    {
        var leases = new InMemoryLeaseProvider();
        var firstInner = new BlockingScheduler();
        var firstScheduler = new LeaderElectedPulseScheduler(firstInner);
        var secondInner = new RecordingScheduler();
        var secondScheduler = new LeaderElectedPulseScheduler(secondInner);
        var firstChecker = new FakePulseChecker("shared-checker");
        var secondChecker = new FakePulseChecker("shared-checker");
        await firstScheduler.ScheduleAsync(firstChecker, EverySecond, Ct);
        await secondScheduler.ScheduleAsync(secondChecker, EverySecond, Ct);
        firstInner.BlockNextSchedule();
        var leaseDuration = TimeSpan.FromMilliseconds(120);
        var renewInterval = TimeSpan.FromMilliseconds(20);
        using var firstService = new LeaderElectionService(
            leases,
            firstScheduler,
            new LeaderElectionOptions
            {
                HolderId = "first",
                LeaseDuration = leaseDuration,
                RenewInterval = renewInterval,
            });
        using var secondService = new LeaderElectionService(
            leases,
            secondScheduler,
            new LeaderElectionOptions
            {
                HolderId = "second",
                LeaseDuration = leaseDuration,
                RenewInterval = renewInterval,
            });

        await firstService.StartAsync(CancellationToken.None);
        await firstInner.Entered.WaitAsync(Ct);
        await secondService.StartAsync(CancellationToken.None);
        await Task.Delay(leaseDuration * 3, Ct);

        Assert.True(firstScheduler.IsLeader);
        Assert.False(secondScheduler.IsLeader);
        Assert.Empty(secondInner.Scheduled);

        await secondService.StopAsync(Ct);
        firstInner.ReleaseSchedule();
        await firstService.StopAsync(Ct);
    }

    [Fact]
    public async Task ReschedulingDuringTakeover_LeavesTheNewestScheduleRunning()
    {
        var inner = new BlockingScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner);
        var checker = new FakePulseChecker("rescheduled-during-takeover");
        await scheduler.ScheduleAsync(checker, EverySecond, Ct);
        inner.BlockNextSchedule();

        var takeover = scheduler.BecomeLeaderAsync(Ct);
        await inner.Entered.WaitAsync(Ct);

        var newest = PulseSchedule.Every(TimeSpan.FromMinutes(2));
        var reschedule = scheduler.ScheduleAsync(checker, newest, Ct);
        inner.ReleaseSchedule();

        await Task.WhenAll(takeover, reschedule);

        Assert.Equal(newest, inner.Active[checker.Name]);
    }

    [Fact]
    public async Task UnschedulingDuringTakeover_LeavesTheCheckerStopped()
    {
        var inner = new BlockingScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner);
        var checker = new FakePulseChecker("unscheduled-during-takeover");
        await scheduler.ScheduleAsync(checker, EverySecond, Ct);
        inner.BlockNextSchedule();

        var takeover = scheduler.BecomeLeaderAsync(Ct);
        await inner.Entered.WaitAsync(Ct);

        var unschedule = scheduler.UnscheduleAsync(checker, Ct);
        inner.ReleaseSchedule();

        await Task.WhenAll(takeover, unschedule);

        Assert.False(inner.Active.ContainsKey(checker.Name));
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
    public async Task ConcurrentContenders_ProduceExactlyOneWinner()
    {
        const int rounds = 100;
        const int contenders = 32;

        for (var round = 0; round < rounds; round++)
        {
            var leases = new InMemoryLeaseProvider();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var attempts = Enumerable.Range(0, contenders).Select(async contender =>
            {
                await start.Task.WaitAsync(Ct);
                return await leases.TryAcquireAsync(
                    "scheduler", $"replica-{contender}", TimeSpan.FromMinutes(1), Ct);
            }).ToArray();

            start.SetResult();
            var results = await Task.WhenAll(attempts);

            Assert.Single(results, won => won);
        }
    }

    [Fact]
    public async Task NonPositiveOrOverflowingDurations_AreRejected()
    {
        var leases = new InMemoryLeaseProvider();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => leases.TryAcquireAsync("scheduler", "replica", TimeSpan.Zero, Ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => leases.TryAcquireAsync("scheduler", "replica", TimeSpan.FromSeconds(-1), Ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => leases.TryAcquireAsync("scheduler", "replica", TimeSpan.MaxValue, Ct));
    }

    [Fact]
    public void InvalidRenewalTiming_IsRejectedWhenTheServiceIsBuilt()
    {
        using var inner = new TimerPulseScheduler();
        var scheduler = new LeaderElectedPulseScheduler(inner);

        Assert.Throws<ArgumentOutOfRangeException>(() => new LeaderElectionService(
            new InMemoryLeaseProvider(),
            scheduler,
            new LeaderElectionOptions
            {
                RenewInterval = TimeSpan.FromSeconds(30),
                LeaseDuration = TimeSpan.FromSeconds(30),
            }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LeaderElectionService(
            new InMemoryLeaseProvider(),
            scheduler,
            new LeaderElectionOptions
            {
                RenewInterval = TimeSpan.FromTicks(1),
                LeaseDuration = TimeSpan.FromSeconds(30),
            }));
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
