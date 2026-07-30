using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Insights;
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

        var recent = await history.GetRecentAlertsAsync(10, Ct);

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

        var recent = await history.GetRecentAlertsAsync(10, Ct);

        Assert.False(recent[0].Delivered);
        Assert.True(recent[1].Delivered);
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
