using Healthie.Abstractions;
using Healthie.Abstractions.Diagnostics;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;
using Healthie.DependencyInjection;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Healthie.Tests.Unit;

/// <summary>
/// A monitoring library that cannot be monitored is a gap, so these drive real checks and read what
/// a consumer's OpenTelemetry pipeline would read -- a <see cref="MeterListener"/> and an
/// <see cref="ActivityListener"/> attached by name. Asserting on the instrument fields instead
/// would pass even if nothing were ever recorded.
/// </summary>
public sealed class DiagnosticsTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A checker whose name is its own, so measurements from one test never reach another.
    /// </summary>
    /// <remarks>
    /// Takes only an <see cref="IStateProvider"/>, and carries everything else on settable
    /// properties, because <c>AddHealthie</c> scans this assembly and registers every non-abstract
    /// PulseChecker it finds. A constructor parameter the container cannot resolve fails every
    /// other test that scans, which is exactly what the first version of this did.
    /// </remarks>
    private sealed class NamedChecker(IStateProvider states)
        : PulseChecker(states, PulseInterval.EveryMinute, 0)
    {
        public string CheckerName { get; init; } = "unnamed";

        public PulseCheckerHealth Health { get; init; } = PulseCheckerHealth.Healthy;

        public Func<Task>? Before { get; init; }

        public override string Name => CheckerName;

        public override async Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            if (Before is not null)
            {
                await Before();
            }

            return new PulseCheckerResult(Health, Health.ToString());
        }
    }

    private readonly record struct Measurement(string Instrument, double Value, Dictionary<string, object?> Tags);

    private readonly List<Measurement> _measurements = [];
    private readonly List<Activity> _activities = [];
    private readonly MeterListener _meters = new();
    private readonly ActivityListener _activityListener;

    public DiagnosticsTests()
    {
        _meters.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == HealthieDiagnostics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _meters.SetMeasurementEventCallback<long>((i, v, tags, _) => Record(i, v, tags));
        _meters.SetMeasurementEventCallback<double>((i, v, tags, _) => Record(i, v, tags));
        _meters.Start();

        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == HealthieDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _activities.Add,
        };

        ActivitySource.AddActivityListener(_activityListener);
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copied = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            copied[tag.Key] = tag.Value;
        }

        lock (_measurements)
        {
            _measurements.Add(new Measurement(instrument.Name, value, copied));
        }
    }

    public void Dispose()
    {
        _meters.Dispose();
        _activityListener.Dispose();
    }

    private List<Measurement> For(string instrument, string checkerName)
    {
        lock (_measurements)
        {
            return [.. _measurements.Where(m =>
                m.Instrument == instrument
                && Equals(m.Tags.GetValueOrDefault(HealthieDiagnostics.CheckerNameTag), checkerName))];
        }
    }

    [Fact]
    public async Task ACheck_RecordsItsDurationAndItsResult()
    {
        var name = $"duration-{Guid.NewGuid():N}";
        using var checker = new NamedChecker(new InMemoryStateProvider()) { CheckerName = name };

        await checker.TriggerAsync(Ct);

        var duration = Assert.Single(For("healthie.check.duration", name));
        Assert.True(duration.Value >= 0, "duration should be recorded in seconds");

        var result = Assert.Single(For("healthie.check.results", name));
        Assert.Equal(1d, result.Value);
        Assert.Equal("Healthy", result.Tags[HealthieDiagnostics.ResultTag]);
    }

    [Fact]
    public async Task AFailedCheck_IsTaggedUnhealthy()
    {
        var name = $"unhealthy-{Guid.NewGuid():N}";
        using var checker = new NamedChecker(new InMemoryStateProvider()) { CheckerName = name, Health = PulseCheckerHealth.Unhealthy };

        await checker.TriggerAsync(Ct);

        Assert.Equal("Unhealthy", Assert.Single(For("healthie.check.results", name)).Tags[HealthieDiagnostics.ResultTag]);
    }

    /// <summary>
    /// The result counter climbs on every tick; this one moves only when something actually changed,
    /// which is what makes it the one worth alerting on.
    /// </summary>
    [Fact]
    public async Task ATransition_IsCountedOnceAndNotOnEveryTick()
    {
        var name = $"transition-{Guid.NewGuid():N}";
        using var checker = new NamedChecker(new InMemoryStateProvider()) { CheckerName = name, Health = PulseCheckerHealth.Unhealthy };

        await checker.TriggerAsync(Ct);
        var afterTheChange = For("healthie.check.transitions", name).Count;

        await checker.TriggerAsync(Ct);
        await checker.TriggerAsync(Ct);

        Assert.Equal(1, afterTheChange);
        Assert.Equal(3, For("healthie.check.results", name).Count);
        Assert.Equal(1, For("healthie.check.transitions", name).Count);
    }

    /// <summary>
    /// A checker whose check outlasts its interval keeps reporting healthy while running at a
    /// fraction of the rate it was asked to. This counter is the only place that shows.
    /// </summary>
    [Fact]
    public async Task ATriggerThatArrivesWhileTheLastIsRunning_IsCounted()
    {
        var name = $"overlap-{Guid.NewGuid():N}";
        var release = new TaskCompletionSource();
        using var checker = new NamedChecker(new InMemoryStateProvider()) { CheckerName = name, Before = () => release.Task };

        var running = checker.TriggerAsync(Ct);
        while (For("healthie.check.overlaps", name).Count == 0)
        {
            await checker.TriggerAsync(Ct);
        }

        release.SetResult();
        await running;

        Assert.NotEmpty(For("healthie.check.overlaps", name));
    }

    [Fact]
    public async Task ACheck_OpensAnActivityNamingTheCheckerAndItsResult()
    {
        var name = $"activity-{Guid.NewGuid():N}";
        using var checker = new NamedChecker(new InMemoryStateProvider()) { CheckerName = name, Health = PulseCheckerHealth.Suspicious };

        await checker.TriggerAsync(Ct);

        var activity = Assert.Single(_activities.Where(
            a => (string?)a.GetTagItem(HealthieDiagnostics.CheckerNameTag) == name));

        Assert.Equal("Healthie.Check", activity.OperationName);

        // The span carries the health that was stored, not the one the check returned. With the
        // default threshold of zero, any failure is promoted straight to Unhealthy, so a Suspicious
        // check is recorded as Unhealthy -- and the span agreeing with the state is the point.
        Assert.Equal("Unhealthy", activity.GetTagItem(HealthieDiagnostics.ResultTag));
    }

    /// <summary>
    /// A checker's tags are user-defined, editable from the dashboard and unbounded. Every distinct
    /// value would multiply the series a metrics backend keeps, so they must not become tags -- the
    /// name and group are bounded by how many checkers exist, and are all that may.
    /// </summary>
    [Fact]
    public async Task UserDefinedTags_NeverBecomeMetricTags()
    {
        var name = $"cardinality-{Guid.NewGuid():N}";
        var states = new InMemoryStateProvider();
        using var checker = new NamedChecker(states) { CheckerName = name };

        await checker.SetTagsAsync(["tenant-a", "region-eu", "build-5f3c9a1"], Ct);
        await checker.SetGroupAsync("data", Ct);
        await checker.TriggerAsync(Ct);

        var result = Assert.Single(For("healthie.check.results", name));

        Assert.Equal("data", result.Tags[HealthieDiagnostics.CheckerGroupTag]);

        string[] permitted =
        [
            HealthieDiagnostics.CheckerGroupTag,
            HealthieDiagnostics.CheckerNameTag,
            HealthieDiagnostics.ResultTag,
        ];

        Assert.Equal(
            permitted.Order(StringComparer.Ordinal),
            result.Tags.Keys.Order(StringComparer.Ordinal));
    }
}
