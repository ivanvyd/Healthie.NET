using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Insights;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;
using Healthie.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.Tests.Unit;

/// <summary>
/// The in-process metrics collector.
/// </summary>
/// <remarks>
/// It reads instruments that are <c>internal</c> to another assembly, by meter name, through a
/// <see cref="System.Diagnostics.Metrics.MeterListener"/>. Nothing about that is checked by the
/// compiler: rename an instrument or change a tag and this keeps building and silently counts
/// nothing. These run real checks and assert the numbers moved.
/// </remarks>
public class MetricsInsightsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Checker(IStateProvider provider, PulseCheckerHealth health, string name)
        : PulseChecker(provider, PulseInterval.EveryMinute)
    {
        public override string Name => name;

        public override Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PulseCheckerResult(health, "measured"));
    }

    [Fact]
    public void AddHealthieMetrics_RegistersTheCollector_AndNothingElseDoes()
    {
        var services = new ServiceCollection();
        services.AddHealthie(typeof(MetricsInsightsTests).Assembly);

        using (var without = services.BuildServiceProvider())
        {
            Assert.Null(without.GetService<IMetricsInsights>());
        }

        services.AddHealthieMetrics();

        using var with = services.BuildServiceProvider();

        Assert.NotNull(with.GetService<IMetricsInsights>());
    }

    /// <summary>
    /// The listener is the whole feature: if it is not attached to the right meter, or the
    /// instrument names drift, everything below reads zero and the board shows an empty panel.
    /// </summary>
    [Fact]
    public async Task RunningChecks_MovesTheCounters()
    {
        using var metrics = new MeterMetricsInsights();

        Assert.Equal(0, metrics.Snapshot(Ct).Checks);

        var provider = new InMemoryStateProvider();
        using var healthy = new Checker(provider, PulseCheckerHealth.Healthy, "metrics-healthy");
        using var failing = new Checker(provider, PulseCheckerHealth.Unhealthy, "metrics-failing");

        await healthy.TriggerAsync(Ct);
        await healthy.TriggerAsync(Ct);
        await failing.TriggerAsync(Ct);

        var snapshot = metrics.Snapshot(Ct);

        Assert.Equal(3, snapshot.Checks);
        Assert.Equal(2, snapshot.ResultsByHealth.GetValueOrDefault(PulseCheckerHealth.Healthy));
        Assert.Equal(1, snapshot.ResultsByHealth.GetValueOrDefault(PulseCheckerHealth.Unhealthy));

        // Two checkers went from nothing to a health, so both transitioned.
        Assert.True(snapshot.Transitions >= 2, $"expected at least 2 transitions, got {snapshot.Transitions}");

        Assert.NotNull(snapshot.MeanDuration);
        Assert.NotNull(snapshot.SlowestDuration);
        Assert.True(snapshot.SlowestDuration >= snapshot.MeanDuration);
    }

    /// <summary>
    /// Two thirds healthy is 66.67%, and a collector that has seen nothing reports no share rather
    /// than a confident zero -- the same distinction the uptime panel makes.
    /// </summary>
    [Fact]
    public async Task TheHealthyShare_IsOfChecksRun_AndIsNullBeforeAnyHaveRun()
    {
        using var metrics = new MeterMetricsInsights();

        Assert.Null(metrics.Snapshot(Ct).HealthyShare);

        var provider = new InMemoryStateProvider();
        using var healthy = new Checker(provider, PulseCheckerHealth.Healthy, "share-healthy");
        using var failing = new Checker(provider, PulseCheckerHealth.Unhealthy, "share-failing");

        await healthy.TriggerAsync(Ct);
        await healthy.TriggerAsync(Ct);
        await failing.TriggerAsync(Ct);

        Assert.Equal(66.67, metrics.Snapshot(Ct).HealthyShare!.Value, 1);
    }

    /// <summary>
    /// Overlaps are the figure that means something is wrong rather than something happened, and
    /// nothing else on the board reports them.
    /// </summary>
    [Fact]
    public void WithNothingOverlapping_HasOverlapsIsFalse()
    {
        using var metrics = new MeterMetricsInsights();
        var snapshot = metrics.Snapshot(Ct);

        Assert.Equal(0, snapshot.OverlappedTriggers);
        Assert.False(snapshot.HasOverlaps);
    }

    /// <summary>
    /// Disposal has to detach the listener. One left attached goes on receiving measurements from
    /// every checker in the process for the rest of its life.
    /// </summary>
    [Fact]
    public async Task OnceDisposed_ItStopsCounting()
    {
        var metrics = new MeterMetricsInsights();

        var provider = new InMemoryStateProvider();
        using var checker = new Checker(provider, PulseCheckerHealth.Healthy, "disposal-target");

        await checker.TriggerAsync(Ct);
        var before = metrics.Snapshot(Ct).Checks;
        Assert.True(before > 0);

        metrics.Dispose();

        await checker.TriggerAsync(Ct);

        Assert.Equal(before, metrics.Snapshot(Ct).Checks);
    }
}
