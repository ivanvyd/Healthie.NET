using Healthie.Abstractions.Enums;
using Healthie.Uptime;
using Microsoft.Extensions.Hosting;

namespace Healthie.Tests.Unit;

/// <summary>
/// Uptime is arithmetic on overlapping intervals, and every interesting case is an edge: a segment
/// that began before the window, one still open, a gap while the application was not running. The
/// calculator is a pure function so all of it can be asserted without a clock or a store.
/// </summary>
public class UptimeCalculatorTests
{
    private static readonly DateTime Noon = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

    private static UptimeSegment Segment(PulseCheckerHealth health, double fromHours, double? toHours = null) =>
        new("svc", health, Noon.AddHours(fromHours), toHours is { } h ? Noon.AddHours(h) : null);

    [Fact]
    public void AWindowFullyHealthy_IsAHundredPercent()
    {
        var report = UptimeCalculator.Calculate(
            "svc", [Segment(PulseCheckerHealth.Healthy, 0, 24)], Noon, Noon.AddHours(24));

        Assert.Equal(100d, report.UptimePercentage);
        Assert.Equal(TimeSpan.Zero, report.Unknown);
    }

    [Fact]
    public void AnHourDownInADay_IsMeasuredAgainstObservedTime()
    {
        var report = UptimeCalculator.Calculate(
            "svc",
            [Segment(PulseCheckerHealth.Healthy, 0, 23), Segment(PulseCheckerHealth.Unhealthy, 23, 24)],
            Noon,
            Noon.AddHours(24));

        Assert.Equal(TimeSpan.FromHours(23), report.Healthy);
        Assert.Equal(TimeSpan.FromHours(1), report.Unhealthy);
        Assert.Equal(23d / 24d * 100d, report.UptimePercentage!.Value, 10);
    }

    /// <summary>
    /// A segment starting before the window contributes only the part inside it -- otherwise a
    /// checker healthy for a year would report a year of uptime in a one-day report.
    /// </summary>
    [Fact]
    public void ASegmentStartingBeforeTheWindow_IsClippedToIt()
    {
        var report = UptimeCalculator.Calculate(
            "svc", [Segment(PulseCheckerHealth.Healthy, -100, 6)], Noon, Noon.AddHours(24));

        Assert.Equal(TimeSpan.FromHours(6), report.Healthy);
    }

    /// <summary>
    /// An open segment is measured to the window's end, not to now, or a report about last month
    /// would keep growing every time it was run.
    /// </summary>
    [Fact]
    public void AnOpenSegment_IsClippedToTheWindowEnd()
    {
        var report = UptimeCalculator.Calculate(
            "svc", [Segment(PulseCheckerHealth.Healthy, 0)], Noon, Noon.AddHours(24));

        Assert.Equal(TimeSpan.FromHours(24), report.Healthy);
        Assert.Equal(TimeSpan.Zero, report.Unknown);
    }

    [Fact]
    public void ASegmentEntirelyOutsideTheWindow_ContributesNothing()
    {
        var report = UptimeCalculator.Calculate(
            "svc", [Segment(PulseCheckerHealth.Unhealthy, -50, -40)], Noon, Noon.AddHours(24));

        Assert.Equal(TimeSpan.Zero, report.Observed);
        Assert.Null(report.UptimePercentage);
    }

    /// <summary>
    /// Time the application was not running is time nothing was watching. Counting it as downtime
    /// would report an outage for every deployment; counting it as uptime would claim a component
    /// was fine over a period nobody looked at it.
    /// </summary>
    [Fact]
    public void AGapWhileNothingWasRunning_IsUnknownRatherThanEither()
    {
        var report = UptimeCalculator.Calculate(
            "svc",
            [Segment(PulseCheckerHealth.Healthy, 0, 6), Segment(PulseCheckerHealth.Healthy, 18, 24)],
            Noon,
            Noon.AddHours(24));

        Assert.Equal(TimeSpan.FromHours(12), report.Healthy);
        Assert.Equal(TimeSpan.FromHours(12), report.Unknown);

        // Twelve hours observed, all healthy -- the unobserved half is excluded, not counted down.
        Assert.Equal(100d, report.UptimePercentage);
    }

    [Fact]
    public void ACheckerThatNeverRan_HasNoUptimeRatherThanZeroOrAHundred()
    {
        var report = UptimeCalculator.Calculate("svc", [], Noon, Noon.AddHours(24));

        Assert.Null(report.UptimePercentage);
        Assert.Null(report.Met(99.9));
        Assert.Equal(TimeSpan.FromHours(24), report.Unknown);
    }

    [Theory]
    [InlineData(99.0, true)]
    [InlineData(99.9, false)]
    public void MetComparesAgainstTheTarget(double target, bool expected)
    {
        var report = UptimeCalculator.Calculate(
            "svc",
            [Segment(PulseCheckerHealth.Healthy, 0, 23.9), Segment(PulseCheckerHealth.Unhealthy, 23.9, 24)],
            Noon,
            Noon.AddHours(24));

        Assert.Equal(expected, report.Met(target));
    }

    [Fact]
    public void SuspiciousTime_IsNotCountedAsUptime()
    {
        var report = UptimeCalculator.Calculate(
            "svc",
            [Segment(PulseCheckerHealth.Healthy, 0, 12), Segment(PulseCheckerHealth.Suspicious, 12, 24)],
            Noon,
            Noon.AddHours(24));

        Assert.Equal(TimeSpan.FromHours(12), report.Suspicious);
        Assert.Equal(50d, report.UptimePercentage);
    }

    [Fact]
    public void AWindowEndingBeforeItStarts_IsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => UptimeCalculator.Calculate("svc", [], Noon.AddHours(24), Noon));
    }

    /// <summary>
    /// Overlapping segments would account for more time than the window holds, and unknown time
    /// cannot be negative.
    /// </summary>
    [Fact]
    public void OverlappingSegments_DoNotProduceNegativeUnknownTime()
    {
        var report = UptimeCalculator.Calculate(
            "svc",
            [Segment(PulseCheckerHealth.Healthy, 0, 24), Segment(PulseCheckerHealth.Unhealthy, 0, 24)],
            Noon,
            Noon.AddHours(24));

        Assert.Equal(TimeSpan.Zero, report.Unknown);
    }
}

/// <summary>
/// The store keeps transitions, not checks. A checker running every second produces 86,400 results
/// a day and perhaps four transitions, and only one of those is small enough to keep for a year.
/// </summary>
public class UptimeStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Noon = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RecordingATransition_ClosesThePreviousSegment()
    {
        var store = new InMemoryUptimeStore();

        await store.RecordAsync("svc", PulseCheckerHealth.Healthy, Noon, Ct);
        await store.RecordAsync("svc", PulseCheckerHealth.Unhealthy, Noon.AddHours(1), Ct);

        var segments = await store.GetSegmentsAsync("svc", Noon, Noon.AddHours(2), Ct);

        Assert.Equal(2, segments.Count);
        Assert.Equal(Noon.AddHours(1), segments[0].EndedAt);
        Assert.True(segments[1].IsOpen);
    }

    /// <summary>
    /// Recording the same health again is not a transition. Without this a caller would have to
    /// track what came before, and one stretch would be split into thousands of segments.
    /// </summary>
    [Fact]
    public async Task RecordingTheSameHealthAgain_DoesNotStartANewSegment()
    {
        var store = new InMemoryUptimeStore();

        await store.RecordAsync("svc", PulseCheckerHealth.Healthy, Noon, Ct);
        await store.RecordAsync("svc", PulseCheckerHealth.Healthy, Noon.AddMinutes(1), Ct);
        await store.RecordAsync("svc", PulseCheckerHealth.Healthy, Noon.AddMinutes(2), Ct);

        Assert.Single(await store.GetSegmentsAsync("svc", Noon, Noon.AddHours(1), Ct));
    }

    [Fact]
    public async Task SegmentsForOneChecker_DoNotAppearForAnother()
    {
        var store = new InMemoryUptimeStore();

        await store.RecordAsync("first", PulseCheckerHealth.Unhealthy, Noon, Ct);
        await store.RecordAsync("second", PulseCheckerHealth.Healthy, Noon, Ct);

        var segments = await store.GetSegmentsAsync("first", Noon, Noon.AddHours(1), Ct);

        Assert.Equal(PulseCheckerHealth.Unhealthy, Assert.Single(segments).Health);
    }

    [Fact]
    public async Task ACheckerWithNoHistory_ReturnsNothingRatherThanThrowing()
    {
        var store = new InMemoryUptimeStore();

        Assert.Empty(await store.GetSegmentsAsync("never-ran", Noon, Noon.AddHours(1), Ct));
    }

    [Fact]
    public async Task RecordedTransitions_ProduceAReportThatAddsUp()
    {
        var store = new InMemoryUptimeStore();

        await store.RecordAsync("svc", PulseCheckerHealth.Healthy, Noon, Ct);
        await store.RecordAsync("svc", PulseCheckerHealth.Unhealthy, Noon.AddHours(20), Ct);
        await store.RecordAsync("svc", PulseCheckerHealth.Healthy, Noon.AddHours(22), Ct);

        var segments = await store.GetSegmentsAsync("svc", Noon, Noon.AddHours(24), Ct);
        var report = UptimeCalculator.Calculate("svc", segments, Noon, Noon.AddHours(24));

        Assert.Equal(TimeSpan.FromHours(22), report.Healthy);
        Assert.Equal(TimeSpan.FromHours(2), report.Unhealthy);
        Assert.Equal(TimeSpan.Zero, report.Unknown);
    }
}

/// <summary>
/// The recorder must not be able to hurt a check, for the same reasons alerting must not: a store
/// that is slow, remote or briefly unavailable is not the component being monitored.
/// </summary>
public class UptimeRecorderTests
{
    private sealed class HangingStore : IUptimeStore
    {
        public Task RecordAsync(string checkerName, PulseCheckerHealth health, DateTime at, CancellationToken cancellationToken = default)
            => Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);

        public Task<IReadOnlyList<UptimeSegment>> GetSegmentsAsync(string checkerName, DateTime from, DateTime to, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UptimeSegment>>([]);
    }

    private sealed class ThrowingStore : IUptimeStore
    {
        public int Attempts { get; private set; }

        public Task RecordAsync(string checkerName, PulseCheckerHealth health, DateTime at, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new InvalidOperationException("store is down");
        }

        public Task<IReadOnlyList<UptimeSegment>> GetSegmentsAsync(string checkerName, DateTime from, DateTime to, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UptimeSegment>>([]);
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
    public async Task AHealthChange_IsRecorded()
    {
        var store = new InMemoryUptimeStore();
        var checker = new FakePulseChecker("recorded");
        var recorder = new UptimeRecorder([checker], store);

        await ((IHostedService)recorder).StartAsync(CancellationToken.None);

        try
        {
            checker.RaiseStateChanged(PulseCheckerHealth.Unhealthy);

            Assert.True(await WaitUntilAsync(
                () => store.GetSegmentsAsync("recorded", DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1))
                    .GetAwaiter().GetResult().Count > 0,
                TimeSpan.FromSeconds(5)));
        }
        finally
        {
            await ((IHostedService)recorder).StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// StateChanged fires on every check, not on every change, so a one-second checker raises 86,400
    /// events a day of which perhaps four are transitions. The store would ignore the repeats
    /// anyway, but the queue would not: without filtering at the source it fills with no-ops and
    /// starts dropping the transitions that matter. A hanging store stalls the drain so the queue
    /// is the only thing absorbing them, which is exactly the situation this guards.
    /// </summary>
    [Fact]
    public async Task RepeatedNonTransitions_NeverReachTheQueue()
    {
        var checker = new FakePulseChecker("not-flooded");
        var recorder = new UptimeRecorder([checker], new HangingStore());

        await ((IHostedService)recorder).StartAsync(CancellationToken.None);

        try
        {
            // Well past the queue's capacity, all of them the same health after the first.
            for (var i = 0; i < 3000; i++)
            {
                checker.RaiseStateChanged(PulseCheckerHealth.Unhealthy);
            }

            await Task.Delay(200);

            Assert.Equal(0, recorder.DroppedCount);
        }
        finally
        {
            await ((IHostedService)recorder).StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task AStoreThatHangs_DoesNotDelayTheCheck()
    {
        var checker = new FakePulseChecker("not-delayed");
        var recorder = new UptimeRecorder([checker], new HangingStore());

        await ((IHostedService)recorder).StartAsync(CancellationToken.None);

        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            checker.RaiseStateChanged(PulseCheckerHealth.Unhealthy);
            clock.Stop();

            Assert.True(
                clock.Elapsed < TimeSpan.FromSeconds(1),
                $"raising the event took {clock.Elapsed}, so the store was on the check's thread");
        }
        finally
        {
            await ((IHostedService)recorder).StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// One failed write loses one transition. It must not end recording for the whole process.
    /// </summary>
    [Fact]
    public async Task AStoreThatThrows_DoesNotStopLaterTransitions()
    {
        var store = new ThrowingStore();
        var checker = new FakePulseChecker("keeps-going");
        var recorder = new UptimeRecorder([checker], store);

        await ((IHostedService)recorder).StartAsync(CancellationToken.None);

        try
        {
            checker.RaiseStateChanged(PulseCheckerHealth.Unhealthy);
            Assert.True(await WaitUntilAsync(() => store.Attempts >= 1, TimeSpan.FromSeconds(5)));

            checker.RaiseStateChanged(PulseCheckerHealth.Healthy);
            Assert.True(await WaitUntilAsync(() => store.Attempts >= 2, TimeSpan.FromSeconds(5)),
                "the recorder stopped after the store threw once");
        }
        finally
        {
            await ((IHostedService)recorder).StopAsync(CancellationToken.None);
        }
    }
}
