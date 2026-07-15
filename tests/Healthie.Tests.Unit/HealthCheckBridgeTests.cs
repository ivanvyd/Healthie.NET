using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Healthie.Tests.Unit;

/// <summary>
/// Health checks written for Microsoft.Extensions.Diagnostics.HealthChecks are monitored as pulse
/// checkers, so the existing ecosystem of health checks can be scheduled and given a failure
/// threshold rather than reimplemented.
/// </summary>
public class HealthCheckBridgeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IPulseChecker Resolve(ServiceProvider provider, string name)
        => provider.GetServices<IPulseChecker>().Single(c => c.Name == name);

    // The raw mapping is asserted through CheckAsync, which reports what the health check said
    // before the threshold has any say in it.
    [Theory]
    [InlineData(HealthStatus.Healthy, PulseCheckerHealth.Healthy)]
    [InlineData(HealthStatus.Degraded, PulseCheckerHealth.Suspicious)]
    [InlineData(HealthStatus.Unhealthy, PulseCheckerHealth.Unhealthy)]
    public async Task CheckAsync_MapsHealthStatusOntoPulseCheckerHealth(
        HealthStatus reported,
        PulseCheckerHealth expected)
    {
        var services = new ServiceCollection();
        services.AddHealthie();
        services.AddSingleton(new StubHealthCheck { Status = reported });
        services.AddHealthieHealthCheck<StubHealthCheck>("stub");

        using var provider = services.BuildServiceProvider();
        var checker = Resolve(provider, "stub");

        var result = await checker.CheckAsync(Ct);

        Assert.Equal(expected, result.Health);
    }

    [Fact]
    public async Task CheckAsync_CarriesTheHealthCheckDescriptionThrough()
    {
        var services = new ServiceCollection();
        services.AddHealthie();
        services.AddSingleton(new StubHealthCheck
        {
            Status = HealthStatus.Unhealthy,
            Description = "the database is not accepting connections",
        });
        services.AddHealthieHealthCheck<StubHealthCheck>("db");

        using var provider = services.BuildServiceProvider();

        var result = await Resolve(provider, "db").CheckAsync(Ct);

        Assert.Equal("the database is not accepting connections", result.Message);
    }

    [Fact]
    public async Task CheckAsync_WhenTheHealthCheckReportsOnlyAnException_CarriesItsMessageThrough()
    {
        var services = new ServiceCollection();
        services.AddHealthie();
        services.AddSingleton(new StubHealthCheck
        {
            Status = HealthStatus.Unhealthy,
            Exception = new InvalidOperationException("connection refused"),
        });
        services.AddHealthieHealthCheck<StubHealthCheck>("db");

        using var provider = services.BuildServiceProvider();

        var result = await Resolve(provider, "db").CheckAsync(Ct);

        Assert.Equal("connection refused", result.Message);
    }

    // Degraded and Suspicious do not mean the same thing: Degraded is "impaired but up", while
    // Suspicious means "failed, but not confirmed yet". A degraded check is therefore a failure,
    // and the default threshold of zero confirms it immediately. Anyone moving health checks across
    // needs a threshold of at least one to keep degraded reading as suspicious.
    [Fact]
    public async Task TriggerAsync_ForADegradedCheckWithTheDefaultThreshold_ReportsUnhealthy()
    {
        var services = new ServiceCollection();
        services.AddHealthie();
        services.AddSingleton(new StubHealthCheck { Status = HealthStatus.Degraded });
        services.AddHealthieHealthCheck<StubHealthCheck>("db");

        using var provider = services.BuildServiceProvider();
        var checker = Resolve(provider, "db");
        await checker.TriggerAsync(Ct);

        Assert.Equal(PulseCheckerHealth.Unhealthy, (await checker.GetStateAsync(Ct)).LastResult?.Health);
    }

    [Fact]
    public async Task TriggerAsync_ForADegradedCheckWithAThreshold_KeepsReportingSuspicious()
    {
        var services = new ServiceCollection();
        services.AddHealthie();
        services.AddSingleton(new StubHealthCheck { Status = HealthStatus.Degraded });
        services.AddHealthieHealthCheck<StubHealthCheck>("db", unhealthyThreshold: 1);

        using var provider = services.BuildServiceProvider();
        var checker = Resolve(provider, "db");
        await checker.TriggerAsync(Ct);

        Assert.Equal(PulseCheckerHealth.Suspicious, (await checker.GetStateAsync(Ct)).LastResult?.Health);
    }

    // A health check reports only on the state it is in right now. The threshold is what Healthie
    // adds on top, so a single blip does not have to read as an outage.
    [Fact]
    public async Task TriggerAsync_AppliesTheUnhealthyThresholdToAFailingHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddHealthie();
        services.AddSingleton(new StubHealthCheck { Status = HealthStatus.Unhealthy });
        services.AddHealthieHealthCheck<StubHealthCheck>("db", unhealthyThreshold: 2);

        using var provider = services.BuildServiceProvider();
        var checker = Resolve(provider, "db");

        await checker.TriggerAsync(Ct);
        Assert.Equal(PulseCheckerHealth.Suspicious, (await checker.GetStateAsync(Ct)).LastResult?.Health);

        await checker.TriggerAsync(Ct);
        await checker.TriggerAsync(Ct);
        Assert.Equal(PulseCheckerHealth.Unhealthy, (await checker.GetStateAsync(Ct)).LastResult?.Health);
    }

    [Fact]
    public void AddHealthieHealthCheck_GivesEachCheckerItsRegisteredName()
    {
        var services = new ServiceCollection();
        services.AddHealthie();
        services.AddHealthieHealthCheck<StubHealthCheck>("first");
        services.AddHealthieHealthCheck<AnotherStubHealthCheck>("second");

        using var provider = services.BuildServiceProvider();
        var names = provider.GetServices<IPulseChecker>().Select(c => c.Name).ToList();

        Assert.Contains("first", names);
        Assert.Contains("second", names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    // The point of the bridge: one call adopts everything already registered with AddHealthChecks.
    [Fact]
    public void AddHealthieForHealthChecks_MonitorsEveryRegisteredHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks()
            .AddCheck("sql", () => HealthCheckResult.Healthy())
            .AddCheck("redis", () => HealthCheckResult.Healthy())
            .AddCheck<StubHealthCheck>("custom");
        services.AddHealthie();

        services.AddHealthieForHealthChecks(PulseInterval.Every30Seconds);

        using var provider = services.BuildServiceProvider();
        var names = provider.GetServices<IPulseChecker>().Select(c => c.Name).ToList();

        Assert.Contains("sql", names);
        Assert.Contains("redis", names);
        Assert.Contains("custom", names);
    }

    [Fact]
    public async Task AddHealthieForHealthChecks_RunsTheAdoptedHealthChecks()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks()
            .AddCheck("degrading", () => HealthCheckResult.Degraded("running slow"));
        services.AddHealthie();
        services.AddHealthieForHealthChecks(unhealthyThreshold: 1);

        using var provider = services.BuildServiceProvider();
        var checker = Resolve(provider, "degrading");
        await checker.TriggerAsync(Ct);

        var state = await checker.GetStateAsync(Ct);
        Assert.Equal(PulseCheckerHealth.Suspicious, state.LastResult?.Health);
        Assert.Contains("running slow", state.LastResult?.Message);
    }

    [Fact]
    public async Task AddHealthieForHealthChecks_AppliesTheRequestedInterval()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddCheck("sql", () => HealthCheckResult.Healthy());
        services.AddHealthie();
        services.AddHealthieForHealthChecks(PulseInterval.Every5Seconds);

        using var provider = services.BuildServiceProvider();
        var checker = Resolve(provider, "sql");

        Assert.Equal(PulseInterval.Every5Seconds, (await checker.GetStateAsync(Ct)).Interval);
    }

    [Fact]
    public void AddHealthieForHealthChecks_WithNoRegisteredHealthChecks_AddsNothing()
    {
        var services = new ServiceCollection();
        services.AddHealthie();

        services.AddHealthieForHealthChecks();

        using var provider = services.BuildServiceProvider();
        Assert.Empty(provider.GetServices<IPulseChecker>());
    }
}

internal sealed class StubHealthCheck : IHealthCheck
{
    public HealthStatus Status { get; set; } = HealthStatus.Healthy;

    public string? Description { get; set; }

    public Exception? Exception { get; set; }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new HealthCheckResult(Status, Description, Exception));
}

internal sealed class AnotherStubHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(HealthCheckResult.Healthy());
}
