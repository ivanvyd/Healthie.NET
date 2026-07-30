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

    /// <summary>
    /// A real checker, so the instruments it reports through are the real ones.
    /// </summary>
    /// <remarks>
    /// Takes only a state provider, and carries what it reports as settable properties. Assembly
    /// scanning registers every concrete <see cref="PulseChecker"/> in this assembly, so a
    /// constructor the container cannot satisfy fails every registration, MCP and AI test in the
    /// suite rather than only this file's -- which is exactly what it did.
    /// </remarks>
    internal sealed class MeteredChecker(IStateProvider stateProvider)
        : PulseChecker(stateProvider, PulseInterval.EveryMinute)
    {
        public PulseCheckerHealth Reports { get; set; } = PulseCheckerHealth.Healthy;

        public string CheckerName { get; set; } = "metered";

        public override string Name => CheckerName;

        public override Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PulseCheckerResult(Reports, "measured"));
    }

    private static MeteredChecker Checker(IStateProvider provider, PulseCheckerHealth reports, string name) =>
        new(provider) { Reports = reports, CheckerName = name };

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
    /// <remarks>
    /// Asserted as deltas with a floor, not as exact totals. A <c>MeterListener</c> hears every
    /// measurement in the process -- that is the feature -- so any other test running a check at the
    /// same time lands in these counters too. Exact numbers passed alone and failed in the full
    /// suite, which is the worst way for a test to be wrong.
    /// </remarks>
    [Fact]
    public async Task RunningChecks_MovesTheCounters()
    {
        using var metrics = new MeterMetricsInsights();
        var before = metrics.Snapshot(Ct);

        var provider = new InMemoryStateProvider();
        using var healthy = Checker(provider, PulseCheckerHealth.Healthy, "metrics-healthy");
        using var failing = Checker(provider, PulseCheckerHealth.Unhealthy, "metrics-failing");

        await healthy.TriggerAsync(Ct);
        await healthy.TriggerAsync(Ct);
        await failing.TriggerAsync(Ct);

        var after = metrics.Snapshot(Ct);

        Assert.True(after.Checks - before.Checks >= 3, $"checks moved by {after.Checks - before.Checks}");
        Assert.True(Counted(after, PulseCheckerHealth.Healthy) - Counted(before, PulseCheckerHealth.Healthy) >= 2);
        Assert.True(Counted(after, PulseCheckerHealth.Unhealthy) - Counted(before, PulseCheckerHealth.Unhealthy) >= 1);

        // Two checkers went from nothing to a health, so both transitioned.
        Assert.True(after.Transitions - before.Transitions >= 2);

        Assert.NotNull(after.MeanDuration);
        Assert.NotNull(after.SlowestDuration);
        Assert.True(after.SlowestDuration >= after.MeanDuration);
    }

    private static long Counted(MetricsSnapshot snapshot, PulseCheckerHealth health) =>
        snapshot.ResultsByHealth.GetValueOrDefault(health);

    /// <summary>
    /// The healthy share is of the checks run, and is nothing at all before any have.
    /// </summary>
    /// <remarks>
    /// Against a snapshot built directly rather than one collected from the meter: the arithmetic is
    /// what is under test, and reading it off a live collector would only re-measure whatever else
    /// the suite happened to be running.
    /// </remarks>
    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(3, 2, 66.67)]
    [InlineData(4, 4, 100.0)]
    [InlineData(5, 0, 0.0)]
    public void TheHealthyShare_IsOfChecksRun_AndIsNothingBeforeAnyHaveRun(long checks, long healthy, double? expected)
    {
        var snapshot = new MetricsSnapshot(
            checks,
            new Dictionary<PulseCheckerHealth, long> { [PulseCheckerHealth.Healthy] = healthy },
            Transitions: 0,
            OverlappedTriggers: 0,
            MeanDuration: null,
            SlowestDuration: null,
            Since: DateTime.UtcNow);

        if (expected is null)
        {
            Assert.Null(snapshot.HealthyShare);
        }
        else
        {
            Assert.Equal(expected.Value, snapshot.HealthyShare!.Value, 1);
        }
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
        using var checker = Checker(provider, PulseCheckerHealth.Healthy, "disposal-target");

        await checker.TriggerAsync(Ct);
        var before = metrics.Snapshot(Ct).Checks;
        Assert.True(before > 0);

        metrics.Dispose();

        await checker.TriggerAsync(Ct);

        Assert.Equal(before, metrics.Snapshot(Ct).Checks);
    }
}
