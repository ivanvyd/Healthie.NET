using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Healthie.AI;
using Healthie.DependencyInjection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.Tests.Unit;

/// <summary>
/// Diagnostics go through IChatClient, so the provider is the host's choice and these tests need no
/// model, key, or network. The anomaly numbers are arithmetic and are asserted exactly.
/// </summary>
public class AiDiagnosticsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string UnhealthyChecker = "Healthie.Tests.Unit.AlwaysUnhealthyPulseChecker";

    private static async Task<IPulsesScheduler> CreateSchedulerAsync(int runs)
    {
        var services = new ServiceCollection();
        services.AddHealthie(typeof(AiDiagnosticsTests).Assembly);

        var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IPulsesScheduler>();
        var checker = (await scheduler.GetPulseCheckersAsync(Ct))[UnhealthyChecker];

        for (var run = 0; run < runs; run++)
        {
            await checker.TriggerAsync(Ct);
        }

        return scheduler;
    }

    [Fact]
    public async Task DiagnoseAsync_ReturnsWhatTheModelSaid()
    {
        var chatClient = new FakeChatClient("The database has refused every connection for ten minutes.");
        var diagnostician = new PulseDiagnostician(chatClient, await CreateSchedulerAsync(runs: 3));

        var diagnosis = await diagnostician.DiagnoseAsync(UnhealthyChecker, Ct);

        Assert.Equal(UnhealthyChecker, diagnosis.Name);
        Assert.Equal("The database has refused every connection for ten minutes.", diagnosis.Summary);
    }

    // The model is given the evidence, not asked to guess: the checker's state and every recorded
    // message have to reach it.
    [Fact]
    public async Task DiagnoseAsync_SendsTheCheckersStateAndHistoryToTheModel()
    {
        var chatClient = new FakeChatClient("summary");
        var diagnostician = new PulseDiagnostician(chatClient, await CreateSchedulerAsync(runs: 2));

        await diagnostician.DiagnoseAsync(UnhealthyChecker, Ct);

        var prompt = chatClient.LastUserMessage;
        Assert.Contains(UnhealthyChecker, prompt);
        Assert.Contains("Unhealthy", prompt);
        Assert.Contains("down", prompt);
        Assert.Contains("Consecutive failures: 2", prompt);
    }

    [Fact]
    public async Task DiagnoseAsync_TellsTheModelWhatItIsReading()
    {
        var chatClient = new FakeChatClient("summary");
        var diagnostician = new PulseDiagnostician(chatClient, await CreateSchedulerAsync(runs: 1));

        await diagnostician.DiagnoseAsync(UnhealthyChecker, Ct);

        Assert.Contains("health monitor", chatClient.LastSystemMessage);
    }

    [Fact]
    public async Task DiagnoseAsync_ForACheckerThatHasNeverRun_SaysSoWithoutCallingTheModel()
    {
        var chatClient = new FakeChatClient("should not be called");
        var diagnostician = new PulseDiagnostician(chatClient, await CreateSchedulerAsync(runs: 0));

        var diagnosis = await diagnostician.DiagnoseAsync(UnhealthyChecker, Ct);

        Assert.Equal(0, chatClient.CallCount);
        Assert.Contains("not recorded any runs", diagnosis.Summary);
    }

    [Fact]
    public async Task DiagnoseAsync_ForAnUnknownChecker_Throws()
    {
        var diagnostician = new PulseDiagnostician(new FakeChatClient("x"), await CreateSchedulerAsync(runs: 1));

        await Assert.ThrowsAsync<ArgumentException>(() => diagnostician.DiagnoseAsync("nope", Ct));
    }

    [Fact]
    public void AddHealthieAI_RegistersTheDiagnostician()
    {
        var services = new ServiceCollection();
        services.AddHealthie(typeof(AiDiagnosticsTests).Assembly);
        services.AddSingleton<IChatClient>(new FakeChatClient("x"));

        services.AddHealthieAI();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<PulseDiagnostician>(provider.GetRequiredService<IPulseDiagnostician>());
    }
}

public class FailureRateAnomalyDetectorTests
{
    private static readonly DateTime Start = new(2026, 7, 15, 9, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<PulseCheckerHistoryEntry> HistoryOf(params bool[] failures) =>
        [.. failures.Select((failed, index) => new PulseCheckerHistoryEntry(
            failed ? PulseCheckerHealth.Unhealthy : PulseCheckerHealth.Healthy,
            failed ? "boom" : "ok",
            Start.AddMinutes(index)))];

    [Fact]
    public void Detect_WhenFailuresStartAfterASteadyRun_ReportsAnAnomaly()
    {
        var history = HistoryOf(false, false, false, false, false, true, true, true, true, true);

        var report = new FailureRateAnomalyDetector().Detect(history);

        Assert.True(report.IsAnomalous);
        Assert.Equal(1.0, report.RecentFailureRate);
        Assert.Equal(0.0, report.EarlierFailureRate);
    }

    [Fact]
    public void Detect_WhenTheCheckerHasAlwaysBeenHealthy_ReportsNoAnomaly()
    {
        var report = new FailureRateAnomalyDetector().Detect(HistoryOf(false, false, false, false, false, false, false, false));

        Assert.False(report.IsAnomalous);
        Assert.Equal(0.0, report.RecentFailureRate);
    }

    // A checker that has always been broken is not suddenly anomalous.
    [Fact]
    public void Detect_WhenTheCheckerHasAlwaysBeenFailing_ReportsNoAnomaly()
    {
        var report = new FailureRateAnomalyDetector().Detect(HistoryOf(true, true, true, true, true, true, true, true));

        Assert.False(report.IsAnomalous);
        Assert.Equal(1.0, report.RecentFailureRate);
        Assert.Equal(1.0, report.EarlierFailureRate);
    }

    [Fact]
    public void Detect_WithTooLittleHistoryToCompare_ReportsNoAnomaly()
    {
        var report = new FailureRateAnomalyDetector().Detect(HistoryOf(true, true));

        Assert.False(report.IsAnomalous);
    }

    [Fact]
    public void Detect_WhenTheRiseIsBelowTheThreshold_ReportsNoAnomaly()
    {
        // Earlier 40% failures, recent 60%: a rise of 20 points, under the 30 point default.
        var history = HistoryOf(true, true, false, false, false, true, true, true, false, false);

        var report = new FailureRateAnomalyDetector().Detect(history);

        Assert.False(report.IsAnomalous);
    }
}

/// <summary>A chat client that records what it was asked and replies with a fixed answer.</summary>
internal sealed class FakeChatClient(string reply) : IChatClient
{
    public int CallCount { get; private set; }

    public string LastSystemMessage { get; private set; } = string.Empty;

    public string LastUserMessage { get; private set; } = string.Empty;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;

        var conversation = messages.ToList();
        LastSystemMessage = string.Concat(conversation.Where(m => m.Role == ChatRole.System).Select(m => m.Text));
        LastUserMessage = string.Concat(conversation.Where(m => m.Role == ChatRole.User).Select(m => m.Text));

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Diagnostics do not stream.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
