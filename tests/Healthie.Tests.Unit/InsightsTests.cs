using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Insights;
using Healthie.Abstractions.StateProviding;
using Healthie.Alerting;
using Healthie.DependencyInjection;
using Healthie.LeaderElection;
using Healthie.Uptime;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.Tests.Unit;

/// <summary>
/// The read-only views the dashboard renders when a feature package is installed.
/// </summary>
/// <remarks>
/// The point of the contract is that the dashboard shows a panel because a service is registered,
/// not because a flag was set -- so what is asserted here is mostly presence and absence, and that
/// nothing appears in an application that installed nothing.
/// </remarks>
public class InsightsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddHealthie(typeof(InsightsTests).Assembly);
        configure(services);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The case that matters most: an application with none of the feature packages must resolve
    /// none of the contracts, so the board renders exactly what it rendered before.
    /// </summary>
    [Fact]
    public void WithNoFeaturePackages_NoInsightsAreRegistered()
    {
        using var provider = Build(_ => { });

        Assert.Null(provider.GetService<IUptimeInsights>());
        Assert.Null(provider.GetService<IAlertInsights>());
        Assert.Null(provider.GetService<ILeadershipInsights>());
        Assert.Null(provider.GetService<IDiagnosisInsights>());
    }

    [Fact]
    public void AddHealthieUptime_RegistersUptimeInsightsAndNothingElse()
    {
        using var provider = Build(services => services.AddHealthieUptime());

        Assert.NotNull(provider.GetService<IUptimeInsights>());
        Assert.Null(provider.GetService<IAlertInsights>());
        Assert.Null(provider.GetService<ILeadershipInsights>());
    }

    [Fact]
    public void AddHealthieAlerts_RegistersAlertInsights()
    {
        using var provider = Build(services => services.AddHealthieAlerts());

        Assert.NotNull(provider.GetService<IAlertInsights>());
        Assert.Null(provider.GetService<IUptimeInsights>());
    }

    [Fact]
    public void AddHealthieLeaderElection_RegistersLeadershipInsights()
    {
        using var provider = Build(services => services.AddHealthieLeaderElection());

        var leadership = provider.GetService<ILeadershipInsights>();

        Assert.NotNull(leadership);
        Assert.False(string.IsNullOrWhiteSpace(leadership.ReplicaId));
    }

    /// <summary>
    /// Nothing recorded is not the same as nothing observed, and neither is a zero. A checker that
    /// has never run reports no uptime rather than a confident 0%.
    /// </summary>
    [Fact]
    public async Task UptimeInsights_ForACheckerThatNeverRan_ReportsNothing()
    {
        using var provider = Build(services => services.AddHealthieUptime());

        var uptime = provider.GetRequiredService<IUptimeInsights>();

        Assert.Null(await uptime.GetUptimeAsync("never-ran", TimeSpan.FromHours(24), Ct));
    }

    [Fact]
    public async Task UptimeInsights_ReportsTheShareOfTheWindowSpentHealthy()
    {
        using var provider = Build(services => services.AddHealthieUptime());

        var store = provider.GetRequiredService<IUptimeStore>();
        var now = DateTime.UtcNow;

        // Transitions, which is how the recorder writes them: each one closes the segment before it.
        // Healthy for an hour, then unhealthy for the hour up to now, inside a four-hour window.
        await store.RecordAsync("split", PulseCheckerHealth.Healthy, now.AddHours(-2), Ct);
        await store.RecordAsync("split", PulseCheckerHealth.Unhealthy, now.AddHours(-1), Ct);

        var uptime = await provider.GetRequiredService<IUptimeInsights>()
            .GetUptimeAsync("split", TimeSpan.FromHours(4), Ct);

        Assert.NotNull(uptime);

        // Half of the observed time, not of the window: the two hours nothing was recorded are time
        // nobody was watching, and counting them either way would be a claim about nothing.
        Assert.Equal(50, uptime.Percentage, 0);

        Assert.NotNull(uptime.LongestOutage);
        Assert.Equal(1, uptime.LongestOutage.Value.TotalHours, 1);
    }

    /// <summary>
    /// A percentage cannot tell a hundred blips from one long outage, which is why the longest
    /// stretch is reported beside it.
    /// </summary>
    [Fact]
    public async Task UptimeInsights_ReportsTheLongestOutageNotTheTotal()
    {
        using var provider = Build(services => services.AddHealthieUptime());

        var store = provider.GetRequiredService<IUptimeStore>();
        var now = DateTime.UtcNow;

        await store.RecordAsync("blips", PulseCheckerHealth.Unhealthy, now.AddMinutes(-50), Ct);
        await store.RecordAsync("blips", PulseCheckerHealth.Healthy, now.AddMinutes(-45), Ct);
        await store.RecordAsync("blips", PulseCheckerHealth.Unhealthy, now.AddMinutes(-30), Ct);
        await store.RecordAsync("blips", PulseCheckerHealth.Healthy, now.AddMinutes(-10), Ct);

        var uptime = await provider.GetRequiredService<IUptimeInsights>()
            .GetUptimeAsync("blips", TimeSpan.FromHours(1), Ct);

        // 25 minutes unhealthy in total, but the longest single stretch is 20.
        Assert.Equal(20, uptime!.LongestOutage!.Value.TotalMinutes, 1);
    }

    [Fact]
    public async Task AlertHistory_KeepsTheNewestAndDropsTheOldest()
    {
        var history = new AlertHistory(capacity: 2);

        foreach (var i in Enumerable.Range(1, 3))
        {
            history.Record(Alert($"checker-{i}"), delivered: true);
        }

        var recent = (await history.GetAlertsAsync(0, 10, Ct)).Alerts;

        Assert.Equal(2, recent.Count);
        Assert.Equal("checker-3", recent[0].CheckerName);
        Assert.Equal("checker-2", recent[1].CheckerName);
    }

    /// <summary>
    /// An alert that fired and reached nobody is the failure worth seeing, so it is recorded as
    /// raised-but-undelivered rather than not recorded at all.
    /// </summary>
    [Fact]
    public async Task AlertHistory_RemembersWhetherAnAlertWasDelivered()
    {
        var history = new AlertHistory(capacity: 4);

        history.Record(Alert("delivered"), delivered: true);
        history.Record(Alert("failed"), delivered: false);

        var recent = (await history.GetAlertsAsync(0, 10, Ct)).Alerts;

        Assert.False(recent[0].Delivered);
        Assert.True(recent[1].Delivered);
    }

    /// <summary>
    /// Trimming used to happen before the enqueue, which made a capacity of zero the empty-queue
    /// case on every single call -- so the first alert raised took down the dispatcher's delivery
    /// loop rather than being discarded.
    /// </summary>
    [Fact]
    public async Task AlertHistory_WithNoRoomAtAll_DiscardsRatherThanThrows()
    {
        var history = new AlertHistory(capacity: 0);

        history.Record(Alert("nowhere"), delivered: true);

        Assert.Empty((await history.GetAlertsAsync(0, 10, Ct)).Alerts);
    }

    /// <summary>
    /// The point of writing the log through the state provider: an operator arriving after a
    /// redeploy is looking for what happened before it.
    /// </summary>
    [Fact]
    public async Task AlertHistory_OnADurableProvider_SurvivesTheProcessThatWroteIt()
    {
        // One provider, two histories: the second is the same application after a restart.
        var store = new InMemoryStateProvider();

        var before = new AlertHistory(capacity: 10, store);
        before.Record(Alert("checker-1"), delivered: true);
        before.Record(Alert("checker-2"), delivered: false);

        // The write is off the delivery path, so it is finished when the read can see it.
        await WaitForAsync(async () => (await new AlertHistory(10, store).GetAlertsAsync(0, 10, Ct)).Total == 2);

        var after = await new AlertHistory(capacity: 10, store).GetAlertsAsync(0, 10, Ct);

        Assert.Equal(2, after.Total);
        Assert.Equal("checker-2", after.Alerts[0].CheckerName);
        Assert.False(after.Alerts[0].Delivered);
    }

    /// <summary>
    /// A store slow enough to make the ordering of concurrent writes visible.
    /// </summary>
    /// <remarks>
    /// The first write is held up longer than the second, which is what a real database does under
    /// load and what a fast local disk does not. Without serialised persistence the older, shorter
    /// snapshot lands last and the newer alert is silently lost.
    /// </remarks>
    private sealed class SlowFirstWriteProvider : IStateProvider
    {
        private const int SlowWriteMs = 250;

        /// <summary>Comfortably past both writes, so the assert sees the state they settled on.</summary>
        public static readonly TimeSpan SettleFor = TimeSpan.FromMilliseconds(SlowWriteMs * 4);

        private readonly InMemoryStateProvider _inner = new();
        private int _writes;

        public async Task SetStateAsync<T>(string name, T state, CancellationToken cancellationToken = default)
        {
            var delay = Interlocked.Increment(ref _writes) == 1 ? SlowWriteMs : 10;
            await Task.Delay(delay, cancellationToken);

            await _inner.SetStateAsync(name, state, cancellationToken);
        }

        public Task<T?> GetStateAsync<T>(string name, CancellationToken cancellationToken = default) =>
            _inner.GetStateAsync<T>(name, cancellationToken);
    }

    /// <summary>
    /// Two alerts raised back to back must both survive. Nothing orders the writes they trigger, so
    /// a snapshot captured at call time could be written after a newer one and undo it.
    /// </summary>
    [Fact]
    public async Task AlertHistory_WhenTwoAlertsRaceToPersist_NeitherIsLost()
    {
        var store = new SlowFirstWriteProvider();
        var history = new AlertHistory(capacity: 10, store);

        history.Record(Alert("first"), delivered: true);
        history.Record(Alert("second"), delivered: true);

        // A fixed settle, deliberately not a poll for the answer. Polling until the store holds two
        // passes the moment the fast write lands and asserts before the slow one overwrites it --
        // which is the very failure under test, observed and then looked away from.
        await Task.Delay(SlowFirstWriteProvider.SettleFor, Ct);

        var reloaded = await new AlertHistory(capacity: 10, store).GetAlertsAsync(0, 10, Ct);

        Assert.Equal(2, reloaded.Total);
        Assert.Equal(["second", "first"], reloaded.Alerts.Select(alert => alert.CheckerName));
    }

    /// <summary>
    /// A page is a window on the whole history, and the total it reports is what a pager counts
    /// against -- not the size of the page it happens to be looking at.
    /// </summary>
    [Fact]
    public async Task AlertHistory_PagesOverEverythingItHolds()
    {
        var history = new AlertHistory(capacity: 10);

        foreach (var i in Enumerable.Range(1, 7))
        {
            history.Record(Alert($"checker-{i}"), delivered: true);
        }

        var first = await history.GetAlertsAsync(0, 3, Ct);
        var second = await history.GetAlertsAsync(3, 3, Ct);
        var last = await history.GetAlertsAsync(6, 3, Ct);

        Assert.Equal(7, first.Total);
        Assert.Equal(7, second.Total);

        Assert.Equal(3, first.Alerts.Count);
        Assert.Equal(3, second.Alerts.Count);
        Assert.Single(last.Alerts);

        // Newest first, and no alert appears on two pages.
        Assert.Equal("checker-7", first.Alerts[0].CheckerName);
        Assert.Equal("checker-4", second.Alerts[0].CheckerName);
        Assert.Equal("checker-1", last.Alerts[0].CheckerName);
    }

    /// <summary>
    /// "Nothing has alerted" and "nothing is configured to deliver" look identical on a board and
    /// mean opposite things, so a sink is listed from startup rather than from its first delivery.
    /// </summary>
    [Fact]
    public void AlertHistory_ListsASinkBeforeItHasDoneAnything()
    {
        var history = new AlertHistory(capacity: 4);

        Assert.Empty(history.Sinks);

        history.Register("SlackAlertSink");

        var sink = Assert.Single(history.Sinks);
        Assert.Equal("SlackAlertSink", sink.Name);
        Assert.Equal(0, sink.Delivered);
        Assert.True(sink.IsHealthy);
    }

    /// <summary>
    /// A sink that failed once and recovered is working, so the board must stop showing it in red.
    /// </summary>
    [Fact]
    public void AlertHistory_ASinkThatRecovers_StopsBeingReportedAsFailing()
    {
        var history = new AlertHistory(capacity: 4);

        history.RecordDelivery("WebhookAlertSink", error: "500 Internal Server Error");

        Assert.False(Assert.Single(history.Sinks).IsHealthy);

        history.RecordDelivery("WebhookAlertSink", error: null);

        var sink = Assert.Single(history.Sinks);

        Assert.True(sink.IsHealthy);
        Assert.Equal(1, sink.Delivered);
        Assert.Equal(1, sink.Failed);
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline && !await condition())
        {
            await Task.Delay(20, Ct);
        }
    }

    /// <summary>
    /// And the supported route there cannot reach zero: the option clamps, as MaxHistoryLength does,
    /// so a board configured with no history shows the last alert rather than nothing at all.
    /// </summary>
    [Fact]
    public void HistoryLength_BelowOne_IsClamped()
    {
        Assert.Equal(1, new HealthieAlertOptions { HistoryLength = 0 }.HistoryLength);
        Assert.Equal(1, new HealthieAlertOptions { HistoryLength = -5 }.HistoryLength);
    }

    [Fact]
    public void AlertHistory_CountsWhatNeverReachedTheQueue()
    {
        var history = new AlertHistory(capacity: 4);

        Assert.Equal(0, history.DroppedCount);

        history.RecordDropped();
        history.RecordDropped();

        Assert.Equal(2, history.DroppedCount);
    }

    private static Alert Alert(string name) => new(
        name,
        name,
        Group: null,
        Tags: [],
        PulseCheckerHealth.Healthy,
        PulseCheckerHealth.Unhealthy,
        "down",
        DateTime.UtcNow);
}
