using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Insights;
using Healthie.Alerting;
using Microsoft.Extensions.Hosting;

namespace Healthie.Tests.Unit;

/// <summary>
/// The hard requirement here is not that alerts arrive -- it is that a sink cannot hurt anything.
/// A checker's job is to report on the component it watches, and a webhook that is down or slow
/// must not delay a check, hold a semaphore, or make a healthy component look unhealthy.
/// </summary>
public class AlertingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class RecordingSink : IAlertSink
    {
        private readonly List<Alert> _received = [];

        public Exception? Throw { get; set; }

        public TimeSpan Delay { get; set; }

        public IReadOnlyList<Alert> Received
        {
            get { lock (_received) { return [.. _received]; } }
        }

        public async Task SendAsync(Alert alert, CancellationToken cancellationToken = default)
        {
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            if (Throw is not null)
            {
                throw Throw;
            }

            lock (_received)
            {
                _received.Add(alert);
            }
        }
    }

    private static async Task<(AlertDispatcher Dispatcher, FakePulseChecker Checker)> StartAsync(
        IAlertSink sink,
        HealthieAlertOptions? options = null)
    {
        var checker = new FakePulseChecker("alerting-target");
        var dispatcher = new AlertDispatcher(
            [checker],
            [sink],
            options ?? new HealthieAlertOptions { DeduplicationWindow = TimeSpan.Zero });

        await ((IHostedService)dispatcher).StartAsync(CancellationToken.None);

        return (dispatcher, checker);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }

    [Fact]
    public async Task AFailure_ReachesTheSink()
    {
        var sink = new RecordingSink();
        var (dispatcher, checker) = await StartAsync(sink);

        try
        {
            // Subscribing happens during StartAsync, so no wait is needed before raising.
            Assert.Equal(1, checker.SubscriberCount);
            checker.RaiseStateChanged(PulseCheckerHealth.Unhealthy);

            Assert.True(await WaitUntilAsync(() => sink.Received.Count == 1, TimeSpan.FromSeconds(5)));
            Assert.Equal(PulseCheckerHealth.Unhealthy, sink.Received[0].CurrentHealth);
            Assert.False(sink.Received[0].IsRecovery);
        }
        finally
        {
            await ((IHostedService)dispatcher).StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// An application can install alerting for the dashboard's panel alone and wire up delivery
    /// later. The dispatcher used to skip subscribing when no sink was registered -- correct while
    /// sinks were the only consumer, and the reason that panel could never fill once it was one.
    /// </summary>
    [Fact]
    public async Task WithNoSinkRegistered_AlertsStillReachTheHistoryTheDashboardReads()
    {
        var checker = new FakePulseChecker("alerting-target");
        var history = new AlertHistory(capacity: 4);
        var dispatcher = new AlertDispatcher(
            [checker],
            [],
            new HealthieAlertOptions { DeduplicationWindow = TimeSpan.Zero },
            history);

        await ((IHostedService)dispatcher).StartAsync(Ct);

        try
        {
            Assert.Equal(1, checker.SubscriberCount);
            checker.RaiseStateChanged(PulseCheckerHealth.Unhealthy);

            IReadOnlyList<AlertInsight> recent = [];
            var deadline = DateTime.UtcNow.AddSeconds(5);

            while (recent.Count == 0 && DateTime.UtcNow < deadline)
            {
                recent = (await history.GetAlertsAsync(0, 10, Ct)).Alerts;

                if (recent.Count == 0)
                {
                    await Task.Delay(20, Ct);
                }
            }

            Assert.Equal(PulseCheckerHealth.Unhealthy, Assert.Single(recent).CurrentHealth);
        }
        finally
        {
            await ((IHostedService)dispatcher).StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// A change carrying no result says nothing about health, so it must not alert. This is the one
    /// branch of the health-change test that the other tests here cannot reach: every other way of
    /// raising the event puts a result on both sides.
    /// </summary>
    [Fact]
    public async Task ASettingChangedBeforeTheFirstCheck_ReachesNoSink()
    {
        var sink = new RecordingSink();
        var (dispatcher, checker) = await StartAsync(sink);

        try
        {
            Assert.Equal(1, checker.SubscriberCount);
            checker.RaiseSettingChanged("Tier 1");

            // A failure raised straight after must be the first thing the sink sees.
            checker.RaiseStateChanged(PulseCheckerHealth.Unhealthy);

            Assert.True(await WaitUntilAsync(() => sink.Received.Count == 1, TimeSpan.FromSeconds(5)));
            Assert.Equal(PulseCheckerHealth.Unhealthy, sink.Received[0].CurrentHealth);
        }
        finally
        {
            await ((IHostedService)dispatcher).StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Suspicious is the state a checker passes through on its way to unhealthy, so alerting on it
    /// by default would page somebody for every blip the threshold exists to absorb.
    /// </summary>
    [Fact]
    public async Task ASuspiciousResult_DoesNotAlertAtTheDefaultSeverity()
    {
        var sink = new RecordingSink();
        var (dispatcher, checker) = await StartAsync(sink);

        try
        {
            checker.RaiseStateChanged(PulseCheckerHealth.Suspicious);
            await Task.Delay(300, Ct);

            Assert.Empty(sink.Received);
        }
        finally
        {
            await ((IHostedService)dispatcher).StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ASuspiciousResult_AlertsWhenTheSeverityIsLowered()
    {
        var sink = new RecordingSink();
        var (dispatcher, checker) = await StartAsync(sink, new HealthieAlertOptions
        {
            MinimumSeverity = PulseCheckerHealth.Suspicious,
            DeduplicationWindow = TimeSpan.Zero,
        });

        try
        {
            checker.RaiseStateChanged(PulseCheckerHealth.Suspicious);

            Assert.True(await WaitUntilAsync(() => sink.Received.Count == 1, TimeSpan.FromSeconds(5)));
        }
        finally
        {
            await ((IHostedService)dispatcher).StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ARecovery_IsAlertedAndMarkedAsOne()
    {
        var sink = new RecordingSink();
        var (dispatcher, checker) = await StartAsync(sink);

        try
        {
            checker.RaiseStateChanged(PulseCheckerHealth.Unhealthy);
            Assert.True(await WaitUntilAsync(() => sink.Received.Count == 1, TimeSpan.FromSeconds(5)));

            checker.RaiseStateChanged(PulseCheckerHealth.Healthy);
            Assert.True(await WaitUntilAsync(() => sink.Received.Count == 2, TimeSpan.FromSeconds(5)));

            Assert.True(sink.Received[1].IsRecovery);
            Assert.Equal(PulseCheckerHealth.Unhealthy, sink.Received[1].PreviousHealth);
        }
        finally
        {
            await ((IHostedService)dispatcher).StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ARecovery_IsSuppressedWhenRecoveriesAreOff()
    {
        var sink = new RecordingSink();
        var (dispatcher, checker) = await StartAsync(sink, new HealthieAlertOptions
        {
            SendRecoveries = false,
            DeduplicationWindow = TimeSpan.Zero,
        });

        try
        {
            checker.RaiseStateChanged(PulseCheckerHealth.Unhealthy);
            Assert.True(await WaitUntilAsync(() => sink.Received.Count == 1, TimeSpan.FromSeconds(5)));

            checker.RaiseStateChanged(PulseCheckerHealth.Healthy);
            await Task.Delay(300, Ct);

            Assert.Single(sink.Received);
        }
        finally
        {
            await ((IHostedService)dispatcher).StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// StateChanged fires on every check, because state equality includes the last execution time.
    /// Without keying off the health itself, a checker running every second would alert every
    /// second -- which is the failure mode that makes people turn alerting off.
    /// </summary>
    [Fact]
    public async Task RepeatingTheSameHealth_DoesNotAlertAgain()
    {
        var sink = new RecordingSink();
        var (dispatcher, checker) = await StartAsync(sink);

        try
        {
            checker.RaiseStateChanged(PulseCheckerHealth.Unhealthy);
            Assert.True(await WaitUntilAsync(() => sink.Received.Count == 1, TimeSpan.FromSeconds(5)));

            checker.RaiseStateChanged(PulseCheckerHealth.Unhealthy);
            checker.RaiseStateChanged(PulseCheckerHealth.Unhealthy);
            await Task.Delay(300, Ct);

            Assert.Single(sink.Received);
        }
        finally
        {
            await ((IHostedService)dispatcher).StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// A component on the edge of working flaps, and every flip is a genuine health change. The
    /// window is what turns one incident into one alert.
    /// </summary>
    [Fact]
    public async Task FlappingWithinTheWindow_AlertsOnce()
    {
        var sink = new RecordingSink();
        var (dispatcher, checker) = await StartAsync(sink, new HealthieAlertOptions
        {
            DeduplicationWindow = TimeSpan.FromMinutes(5),
        });

        try
        {
            checker.RaiseStateChanged(PulseCheckerHealth.Unhealthy);
            Assert.True(await WaitUntilAsync(() => sink.Received.Count == 1, TimeSpan.FromSeconds(5)));

            for (var i = 0; i < 5; i++)
            {
                checker.RaiseStateChanged(PulseCheckerHealth.Healthy);
                checker.RaiseStateChanged(PulseCheckerHealth.Unhealthy);
            }

            await Task.Delay(300, Ct);

            Assert.Single(sink.Received);
        }
        finally
        {
            await ((IHostedService)dispatcher).StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// The invariant the whole design exists for: a sink that throws must not reach the check.
    /// </summary>
    [Fact]
    public async Task ASinkThatThrows_DoesNotStopLaterAlerts()
    {
        var failing = new RecordingSink { Throw = new InvalidOperationException("webhook is down") };
        var working = new RecordingSink();

        var checker = new FakePulseChecker("resilient");
        var dispatcher = new AlertDispatcher(
            [checker],
            [failing, working],
            new HealthieAlertOptions { DeduplicationWindow = TimeSpan.Zero });

        await ((IHostedService)dispatcher).StartAsync(CancellationToken.None);

        try
        {
            checker.RaiseStateChanged(PulseCheckerHealth.Unhealthy);

            // The failing sink is first, so the second one receiving proves the first did not stop it.
            Assert.True(await WaitUntilAsync(() => working.Received.Count == 1, TimeSpan.FromSeconds(5)));

            checker.RaiseStateChanged(PulseCheckerHealth.Healthy);
            Assert.True(await WaitUntilAsync(() => working.Received.Count == 2, TimeSpan.FromSeconds(5)));
        }
        finally
        {
            await ((IHostedService)dispatcher).StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Raising the event is what a check does inside its own semaphore, so it has to return at once
    /// however slow the sink is. A sink taking ten seconds must not add ten seconds to a check.
    /// </summary>
    [Fact]
    public async Task ASinkThatHangs_DoesNotDelayTheCheck()
    {
        var slow = new RecordingSink { Delay = TimeSpan.FromSeconds(30) };
        var (dispatcher, checker) = await StartAsync(slow);

        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            checker.RaiseStateChanged(PulseCheckerHealth.Unhealthy);
            clock.Stop();

            Assert.True(
                clock.Elapsed < TimeSpan.FromSeconds(1),
                $"raising the event took {clock.Elapsed}, so the sink was on the check's thread");
        }
        finally
        {
            await ((IHostedService)dispatcher).StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// If sinks are slower than checks produce alerts, something has to give, and it must not be
    /// the checks -- an unbounded queue would trade a delivery problem for a memory leak inside the
    /// process being monitored.
    /// </summary>
    [Fact]
    public async Task WhenTheQueueIsFull_AlertsAreDroppedRatherThanBlocking()
    {
        var stuck = new RecordingSink { Delay = TimeSpan.FromMinutes(5) };
        var checkers = Enumerable.Range(0, 40).Select(i => new FakePulseChecker($"flood-{i}")).ToList();

        var dispatcher = new AlertDispatcher(
            checkers,
            [stuck],
            new HealthieAlertOptions { QueueCapacity = 4, DeduplicationWindow = TimeSpan.Zero });

        await ((IHostedService)dispatcher).StartAsync(CancellationToken.None);

        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (var checker in checkers)
            {
                checker.RaiseStateChanged(PulseCheckerHealth.Unhealthy);
            }

            clock.Stop();

            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"raising events blocked for {clock.Elapsed}");
            Assert.True(await WaitUntilAsync(() => dispatcher.DroppedCount > 0, TimeSpan.FromSeconds(5)),
                "a full queue should drop rather than wait");
        }
        finally
        {
            await ((IHostedService)dispatcher).StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void TheDeduplicationKey_IdentifiesTheIncidentNotTheOccurrence()
    {
        var opened = new Alert("db", "db", null, [], PulseCheckerHealth.Healthy, PulseCheckerHealth.Unhealthy, "down", DateTime.UtcNow);
        var closed = new Alert("db", "db", null, [], PulseCheckerHealth.Unhealthy, PulseCheckerHealth.Healthy, "up", DateTime.UtcNow.AddMinutes(5));

        // An incident tracker keyed on this closes the same incident it opened, rather than
        // accumulating one per transition.
        Assert.Equal(opened.DeduplicationKey, closed.DeduplicationKey);
    }
}
