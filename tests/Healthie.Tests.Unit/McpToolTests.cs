using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Scheduling;
using Healthie.DependencyInjection;
using Healthie.Mcp;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Healthie.Tests.Unit;

/// <summary>
/// The MCP tools are what an AI agent sees, so they are covered directly: the read tools must report
/// what the checkers report, and the tools that change state must stay hidden unless they have been
/// deliberately turned on.
/// </summary>
public class McpToolTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string HealthyChecker = "Healthie.Tests.Unit.AlwaysHealthyPulseChecker";
    private const string UnhealthyChecker = "Healthie.Tests.Unit.AlwaysUnhealthyPulseChecker";

    private static async Task<IPulsesScheduler> CreateSchedulerAsync(bool runChecks = true)
    {
        var services = new ServiceCollection();
        services.AddHealthie(typeof(McpToolTests).Assembly);

        var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IPulsesScheduler>();

        if (runChecks)
        {
            foreach (var checker in (await scheduler.GetPulseCheckersAsync(Ct)).Values)
            {
                await checker.TriggerAsync(Ct);
            }
        }

        return scheduler;
    }

    [Fact]
    public async Task GetHealthStatesAsync_ReportsEveryChecker()
    {
        var tools = new HealthieTools(await CreateSchedulerAsync());

        var states = await tools.GetHealthStatesAsync(Ct);

        Assert.Contains(states, s => s.Name == HealthyChecker && s.Health == "Healthy");
        Assert.Contains(states, s => s.Name == UnhealthyChecker && s.Health == "Unhealthy");
    }

    [Fact]
    public async Task GetUnhealthyCheckersAsync_LeavesOutTheHealthyOnes()
    {
        var tools = new HealthieTools(await CreateSchedulerAsync());

        var unhealthy = await tools.GetUnhealthyCheckersAsync(Ct);

        Assert.DoesNotContain(unhealthy, s => s.Name == HealthyChecker);
        Assert.Contains(unhealthy, s => s.Name == UnhealthyChecker);
    }

    [Fact]
    public async Task GetUnhealthyCheckersAsync_BeforeAnyCheckHasRun_ReportsNothing()
    {
        var tools = new HealthieTools(await CreateSchedulerAsync(runChecks: false));

        Assert.Empty(await tools.GetUnhealthyCheckersAsync(Ct));
    }

    [Fact]
    public async Task GetCheckerAsync_ReportsTheCheckersConfiguration()
    {
        var tools = new HealthieTools(await CreateSchedulerAsync());

        var detail = await tools.GetCheckerAsync(UnhealthyChecker, Ct);

        Assert.Equal(UnhealthyChecker, detail.Name);
        Assert.Equal("Unhealthy", detail.Health);
        Assert.Equal(PulseInterval.EveryMinute.ToString(), detail.Interval);
        Assert.Equal(1, detail.ConsecutiveFailures);
    }

    // The model picks names out of another tool's output, so an unknown name has to say what to do
    // rather than surface an internal failure.
    [Fact]
    public async Task GetCheckerAsync_ForAnUnknownName_ExplainsHowToFindTheRealOnes()
    {
        var tools = new HealthieTools(await CreateSchedulerAsync());

        var exception = await Assert.ThrowsAsync<McpException>(() => tools.GetCheckerAsync("nope", Ct));

        Assert.Contains("nope", exception.Message);
        Assert.Contains("get_health_states", exception.Message);
    }

    [Fact]
    public async Task GetCheckHistoryAsync_ReturnsTheNewestEntriesFirst()
    {
        var scheduler = await CreateSchedulerAsync();
        var checker = (await scheduler.GetPulseCheckersAsync(Ct))[UnhealthyChecker];
        await checker.TriggerAsync(Ct);
        var tools = new HealthieTools(scheduler);

        var page = await tools.GetCheckHistoryAsync(UnhealthyChecker, limit: 20, offset: 0, Ct);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Entries.Count);
        Assert.True(page.Entries[0].ExecutedAt >= page.Entries[1].ExecutedAt);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task GetCheckHistoryAsync_PagesThroughHistory()
    {
        var scheduler = await CreateSchedulerAsync();
        var checker = (await scheduler.GetPulseCheckersAsync(Ct))[UnhealthyChecker];
        await checker.TriggerAsync(Ct);
        await checker.TriggerAsync(Ct);
        var tools = new HealthieTools(scheduler);

        var firstPage = await tools.GetCheckHistoryAsync(UnhealthyChecker, limit: 1, offset: 0, Ct);
        var secondPage = await tools.GetCheckHistoryAsync(UnhealthyChecker, limit: 1, offset: 1, Ct);

        Assert.True(firstPage.HasMore);
        Assert.Single(firstPage.Entries);
        Assert.NotEqual(firstPage.Entries[0].ExecutedAt, secondPage.Entries[0].ExecutedAt);
    }

    [Fact]
    public async Task RunCheckAsync_RunsTheCheckAndReportsTheFreshResult()
    {
        var scheduler = await CreateSchedulerAsync(runChecks: false);
        var tools = new HealthieActionTools(scheduler);

        var result = await tools.RunCheckAsync(UnhealthyChecker, Ct);

        Assert.Equal("Unhealthy", result.Health);
        Assert.NotNull(result.LastRanAt);
    }

    [Fact]
    public async Task ResetCheckerAsync_ClearsTheFailureStreak()
    {
        var scheduler = await CreateSchedulerAsync();
        var tools = new HealthieActionTools(scheduler);

        var result = await tools.ResetCheckerAsync(UnhealthyChecker, Ct);

        Assert.Equal("Healthy", result.Health);
        Assert.Equal(0, (await new HealthieTools(scheduler).GetCheckerAsync(UnhealthyChecker, Ct)).ConsecutiveFailures);
    }

    [Fact]
    public async Task RunCheckAsync_ForAnUnknownName_ExplainsHowToFindTheRealOnes()
    {
        var tools = new HealthieActionTools(await CreateSchedulerAsync());

        var exception = await Assert.ThrowsAsync<McpException>(() => tools.RunCheckAsync("nope", Ct));

        Assert.Contains("get_health_states", exception.Message);
    }

    // Anything that can reach the endpoint can call the tools it exposes, so a server must not offer
    // to run checks against someone's infrastructure until that has been asked for.
    [Fact]
    public void AddHealthieMcp_ByDefault_ExposesOnlyReadOnlyTools()
    {
        var services = new ServiceCollection();
        services.AddHealthie();

        services.AddHealthieMcp();

        Assert.DoesNotContain(RegisteredToolNames(services), name => name is "run_check" or "reset_checker");
    }

    [Fact]
    public void AddHealthieMcp_ByDefault_StillExposesTheReadOnlyTools()
    {
        var services = new ServiceCollection();
        services.AddHealthie();

        services.AddHealthieMcp();

        Assert.Contains("get_health_states", RegisteredToolNames(services));
    }

    [Fact]
    public void AddHealthieMcp_WhenMutationsAreAllowed_ExposesTheActionTools()
    {
        var services = new ServiceCollection();
        services.AddHealthie();

        services.AddHealthieMcp(options => options.AllowMutations = true);

        var names = RegisteredToolNames(services);
        Assert.Contains("run_check", names);
        Assert.Contains("reset_checker", names);
    }

    private static IReadOnlyList<string> RegisteredToolNames(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();

        return [.. provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name)];
    }
}
