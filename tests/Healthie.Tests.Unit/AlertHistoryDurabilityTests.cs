using Healthie.Abstractions.Enums;
using Healthie.Abstractions.StateProviding;
using Healthie.Alerting;
using Healthie.StateProviding.Redis;
using Healthie.StateProviding.Relational;
using Npgsql;
using System.Data.Common;
using StackExchange.Redis;

namespace Healthie.Tests.Unit;

/// <summary>
/// The alert history against real durable providers.
/// </summary>
/// <remarks>
/// <para>
/// The claim being tested is the one on the box: a deployment on a durable state provider keeps its
/// alerts across a restart. Everything else about the history is covered against the in-memory
/// provider, which proves the logic and nothing about serialization -- and this is the one entry a
/// state provider holds that is not a <c>PulseCheckerState</c>, stored under a key that is not a
/// checker's name. Either could have been an assumption a provider quietly relied on.
/// </para>
/// <para>
/// Skipped rather than failed where there is no container runtime, like every other real-provider
/// test here.
/// </para>
/// </remarks>
public abstract class AlertHistoryDurabilityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected abstract string? FixtureUnavailable { get; }

    /// <summary>A provider over the real store, keyed so each test method gets its own space.</summary>
    protected abstract Task<IStateProvider> ProviderAsync();

    /// <summary>
    /// Two histories over one provider: the second is the same application after a redeploy.
    /// </summary>
    [Fact]
    public async Task AnAlertLog_SurvivesTheProcessThatWroteIt()
    {
        Assert.SkipWhen(FixtureUnavailable is not null, $"No container runtime. {FixtureUnavailable}");

        var store = await ProviderAsync();

        var before = new AlertHistory(capacity: 10, store);
        before.Record(AlertFor("checker-1", PulseCheckerHealth.Unhealthy), delivered: true);
        before.Record(AlertFor("checker-2", PulseCheckerHealth.Suspicious), delivered: false);

        // The write is deliberately off the delivery path, so it lands shortly after Record returns.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        var after = await new AlertHistory(10, store).GetAlertsAsync(0, 10, Ct);

        while (after.Total < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, Ct);
            after = await new AlertHistory(10, store).GetAlertsAsync(0, 10, Ct);
        }

        Assert.Equal(2, after.Total);

        // Newest first, and every field survived the round trip -- including the nullable enum and
        // the delivery flag, which are the two most likely to come back wrong through a serializer.
        Assert.Equal("checker-2", after.Alerts[0].CheckerName);
        Assert.Equal(PulseCheckerHealth.Suspicious, after.Alerts[0].CurrentHealth);
        Assert.Equal(PulseCheckerHealth.Healthy, after.Alerts[0].PreviousHealth);
        Assert.False(after.Alerts[0].Delivered);

        Assert.Equal("checker-1", after.Alerts[1].CheckerName);
        Assert.Equal(PulseCheckerHealth.Unhealthy, after.Alerts[1].CurrentHealth);
        Assert.True(after.Alerts[1].Delivered);
    }

    /// <summary>
    /// The log lives beside checker state in the same store, so it must not be mistaken for a
    /// checker: a dashboard reading every state would otherwise list it as one.
    /// </summary>
    [Fact]
    public async Task TheAlertLog_DoesNotReadBackAsACheckerState()
    {
        Assert.SkipWhen(FixtureUnavailable is not null, $"No container runtime. {FixtureUnavailable}");

        var store = await ProviderAsync();
        var history = new AlertHistory(capacity: 4, store);

        history.Record(AlertFor("checker-1", PulseCheckerHealth.Unhealthy), delivered: true);

        var deadline = DateTime.UtcNow.AddSeconds(15);

        while ((await new AlertHistory(4, store).GetAlertsAsync(0, 10, Ct)).Total == 0
               && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, Ct);
        }

        // Reading it as the wrong type must fail or come back empty -- never as a checker with a
        // plausible-looking default state.
        try
        {
            var asChecker = await store.GetStateAsync<Healthie.Abstractions.Models.PulseCheckerState>(
                "healthie.alerts.log", Ct);

            Assert.Null(asChecker?.LastResult);
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            // Refusing outright is the better answer of the two, and some providers do.
        }
    }

    private static Alert AlertFor(string name, PulseCheckerHealth health) => new(
        name,
        name,
        Group: "durability",
        Tags: ["persisted"],
        PulseCheckerHealth.Healthy,
        health,
        "stored and read back",
        DateTime.UtcNow);
}

/// <summary>The alert log against a real Redis.</summary>
public sealed class RedisAlertHistoryTests(RedisFixture fixture)
    : AlertHistoryDurabilityTests, IClassFixture<RedisFixture>
{
    protected override string? FixtureUnavailable => fixture.Unavailable;

    protected override async Task<IStateProvider> ProviderAsync()
    {
        var connection = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);

        return new RedisStateProvider(connection, $"{TestContext.Current.TestMethod?.MethodName}:");
    }
}

/// <summary>The alert log against a real PostgreSQL.</summary>
public sealed class PostgresAlertHistoryTests(PostgresFixture fixture)
    : AlertHistoryDurabilityTests, IClassFixture<PostgresFixture>
{
    protected override string? FixtureUnavailable => fixture.Unavailable;

    protected override async Task<IStateProvider> ProviderAsync()
    {
        var table = $"alerts_{Guid.NewGuid():N}";
        DbConnection Connect() => new NpgsqlConnection(fixture.ConnectionString);

        await new RelationalStateProviderInitializer(Connect, RelationalDialect.PostgreSql, table)
            .InitializeAsync(TestContext.Current.CancellationToken);

        return new RelationalStateProvider(Connect, RelationalDialect.PostgreSql, table);
    }
}
