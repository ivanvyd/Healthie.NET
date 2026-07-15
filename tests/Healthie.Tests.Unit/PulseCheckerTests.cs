using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;
using Healthie.DependencyInjection;

namespace Healthie.Tests.Unit;

public class PulseCheckerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ControllablePulseChecker CreateChecker(IStateProvider? stateProvider = null)
        => new(stateProvider ?? new InMemoryStateProvider());

    [Fact]
    public async Task StartAsync_WhenTheCheckerIsStopped_ActivatesItAndReportsTheChange()
    {
        await using var checker = CreateChecker();
        await checker.StopAsync(Ct);

        var started = await checker.StartAsync(Ct);

        Assert.True(started);
        Assert.True((await checker.GetStateAsync(Ct)).IsActive);
    }

    [Fact]
    public async Task StartAsync_WhenTheCheckerIsAlreadyActive_ReportsNoChange()
    {
        await using var checker = CreateChecker();

        var started = await checker.StartAsync(Ct);

        Assert.False(started);
        Assert.True((await checker.GetStateAsync(Ct)).IsActive);
    }

    [Fact]
    public async Task StopAsync_WhenTheCheckerIsActive_DeactivatesItAndReportsTheChange()
    {
        await using var checker = CreateChecker();

        var stopped = await checker.StopAsync(Ct);

        Assert.True(stopped);
        Assert.False((await checker.GetStateAsync(Ct)).IsActive);
    }

    [Fact]
    public async Task StopAsync_WhenTheCheckerIsAlreadyStopped_ReportsNoChange()
    {
        await using var checker = CreateChecker();
        await checker.StopAsync(Ct);

        var stopped = await checker.StopAsync(Ct);

        Assert.False(stopped);
    }

    // Start and Stop are a matched pair: both report true only when they changed the state.
    [Fact]
    public async Task StartAsync_AndStopAsync_ReportChangesUsingTheSameConvention()
    {
        await using var checker = CreateChecker();

        Assert.True(await checker.StopAsync(Ct));
        Assert.False(await checker.StopAsync(Ct));
        Assert.True(await checker.StartAsync(Ct));
        Assert.False(await checker.StartAsync(Ct));
    }

    [Fact]
    public async Task TriggerAsync_WhenTheCheckIsHealthy_RecordsAHealthyResult()
    {
        await using var checker = CreateChecker();
        checker.NextResult = new PulseCheckerResult(PulseCheckerHealth.Healthy, "ok");

        await checker.TriggerAsync(Ct);

        var state = await checker.GetStateAsync(Ct);
        Assert.Equal(PulseCheckerHealth.Healthy, state.LastResult?.Health);
        Assert.Equal(0, state.ConsecutiveFailureCount);
        Assert.NotNull(state.LastExecutionDateTime);
    }

    [Fact]
    public async Task TriggerAsync_WithTheDefaultThreshold_MarksAFailureUnhealthyImmediately()
    {
        await using var checker = CreateChecker();
        checker.NextResult = new PulseCheckerResult(PulseCheckerHealth.Unhealthy, "boom");

        await checker.TriggerAsync(Ct);

        var state = await checker.GetStateAsync(Ct);
        Assert.Equal(PulseCheckerHealth.Unhealthy, state.LastResult?.Health);
        Assert.Equal(1, state.ConsecutiveFailureCount);
    }

    [Fact]
    public async Task TriggerAsync_WhenFailuresAreWithinTheThreshold_DemotesUnhealthyToSuspicious()
    {
        await using var checker = new ControllablePulseChecker(new InMemoryStateProvider(), unhealthyThreshold: 2)
        {
            NextResult = new PulseCheckerResult(PulseCheckerHealth.Unhealthy, "boom"),
        };

        await checker.TriggerAsync(Ct);

        var state = await checker.GetStateAsync(Ct);
        Assert.Equal(PulseCheckerHealth.Suspicious, state.LastResult?.Health);
        Assert.Equal(1, state.ConsecutiveFailureCount);
    }

    [Fact]
    public async Task TriggerAsync_WhenFailuresCrossTheThreshold_BecomesUnhealthy()
    {
        await using var checker = new ControllablePulseChecker(new InMemoryStateProvider(), unhealthyThreshold: 2)
        {
            NextResult = new PulseCheckerResult(PulseCheckerHealth.Unhealthy, "boom"),
        };

        await checker.TriggerAsync(Ct);
        await checker.TriggerAsync(Ct);
        await checker.TriggerAsync(Ct);

        var state = await checker.GetStateAsync(Ct);
        Assert.Equal(PulseCheckerHealth.Unhealthy, state.LastResult?.Health);
        Assert.Equal(3, state.ConsecutiveFailureCount);
    }

    [Fact]
    public async Task TriggerAsync_AfterAHealthyResult_ResetsTheConsecutiveFailureCount()
    {
        await using var checker = CreateChecker();
        checker.NextResult = new PulseCheckerResult(PulseCheckerHealth.Unhealthy, "boom");
        await checker.TriggerAsync(Ct);

        checker.NextResult = new PulseCheckerResult(PulseCheckerHealth.Healthy, "ok");
        await checker.TriggerAsync(Ct);

        Assert.Equal(0, (await checker.GetStateAsync(Ct)).ConsecutiveFailureCount);
    }

    [Fact]
    public async Task TriggerAsync_WhenTheCheckThrows_RecordsTheFailureInsteadOfPropagating()
    {
        await using var checker = CreateChecker();
        checker.ThrowOnCheck = new InvalidOperationException("checker exploded");

        await checker.TriggerAsync(Ct);

        var state = await checker.GetStateAsync(Ct);
        Assert.Equal(PulseCheckerHealth.Unhealthy, state.LastResult?.Health);
        Assert.Contains("checker exploded", state.LastResult?.Message);
        Assert.Equal(1, state.ConsecutiveFailureCount);
    }

    [Fact]
    public async Task TriggerAsync_WhenHistoryIsEnabled_AppendsAnEntryPerRun()
    {
        await using var checker = CreateChecker();

        await checker.TriggerAsync(Ct);
        await checker.TriggerAsync(Ct);

        Assert.Equal(2, (await checker.GetHistoryAsync(Ct)).Count);
    }

    [Fact]
    public async Task TriggerAsync_WhenHistoryIsDisabled_RecordsNothing()
    {
        await using var checker = CreateChecker();
        await checker.SetHistoryEnabledAsync(false, Ct);

        await checker.TriggerAsync(Ct);

        Assert.Empty(await checker.GetHistoryAsync(Ct));
    }

    // The event carries the state as it was before the trigger. History is a mutable list, so a
    // snapshot that shared it would show the entry this very trigger appended.
    [Fact]
    public async Task TriggerAsync_RaisesStateChangedWithAnOldStateTakenBeforeTheRun()
    {
        await using var checker = CreateChecker();
        await checker.TriggerAsync(Ct);

        PulseCheckerStateChangedEventArgs? captured = null;
        checker.StateChanged += (_, args) => captured = args;

        await checker.TriggerAsync(Ct);

        Assert.NotNull(captured);
        Assert.Single(captured.OldState.History);
        Assert.Equal(2, captured.NewState.History.Count);
    }

    [Fact]
    public async Task SetIntervalAsync_WhenTheIntervalChanges_RaisesStateChanged()
    {
        await using var checker = CreateChecker();
        var raised = 0;
        checker.StateChanged += (_, _) => raised++;

        await checker.SetIntervalAsync(PulseInterval.Every30Seconds, Ct);

        Assert.Equal(1, raised);
        Assert.Equal(PulseInterval.Every30Seconds, (await checker.GetStateAsync(Ct)).Interval);
    }

    // Writing back an unchanged state must not look like a change just because the stored copy
    // and the incoming copy are different objects.
    [Fact]
    public async Task SetStateAsync_WhenTheStateIsUnchanged_DoesNotRaiseStateChanged()
    {
        await using var checker = CreateChecker();
        var state = await checker.GetStateAsync(Ct);
        await checker.SetStateAsync(state, Ct);

        var raised = 0;
        checker.StateChanged += (_, _) => raised++;

        await checker.SetStateAsync(await checker.GetStateAsync(Ct), Ct);

        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsACopy_SoCallersCannotMutateStoredHistory()
    {
        await using var checker = CreateChecker();
        await checker.TriggerAsync(Ct);

        var history = await checker.GetHistoryAsync(Ct);
        history.Clear();

        Assert.Single(await checker.GetHistoryAsync(Ct));
    }

    [Fact]
    public async Task ResetAsync_ClearsTheFailureCountAndReportsHealthy()
    {
        await using var checker = CreateChecker();
        checker.NextResult = new PulseCheckerResult(PulseCheckerHealth.Unhealthy, "boom");
        await checker.TriggerAsync(Ct);

        await checker.ResetAsync(Ct);

        var state = await checker.GetStateAsync(Ct);
        Assert.Equal(0, state.ConsecutiveFailureCount);
        Assert.Equal(PulseCheckerHealth.Healthy, state.LastResult?.Health);
    }

    [Fact]
    public async Task ClearHistoryAsync_RemovesEveryEntry()
    {
        await using var checker = CreateChecker();
        await checker.TriggerAsync(Ct);

        await checker.ClearHistoryAsync(Ct);

        Assert.Empty(await checker.GetHistoryAsync(Ct));
    }

    // An overlapping trigger returns immediately rather than running the check concurrently.
    [Fact]
    public async Task TriggerAsync_WhileAnotherTriggerIsRunning_SkipsTheOverlappingRun()
    {
        await using var checker = CreateChecker();
        using var blocked = new SemaphoreSlim(0, 1);
        checker.OnCheck = async () => await blocked.WaitAsync(Ct);

        var first = checker.TriggerAsync(Ct);
        await checker.WaitUntilCheckStartedAsync();

        await checker.TriggerAsync(Ct);

        blocked.Release();
        await first;

        Assert.Equal(1, checker.CheckCallCount);
    }

    // The monitored component failing is a health signal. This library's own storage failing is not:
    // recording it as a result would report a healthy component as down.
    // A storage blip must not be written into the checker's own history as a failed check: it would
    // tell operators a healthy component is down. The failure has to surface as an error instead.
    [Fact]
    public async Task TriggerAsync_WhenTheStateProviderFailsTransiently_DoesNotRecordItAsAFailedCheck()
    {
        var provider = new FaultyStateProvider();
        await using var checker = CreateChecker(provider);
        provider.FailNextWrites(1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => checker.TriggerAsync(Ct));

        var state = await checker.GetStateAsync(Ct);
        Assert.Null(state.LastResult);
        Assert.Equal(0, state.ConsecutiveFailureCount);
        Assert.Empty(state.History);
    }

    [Fact]
    public async Task TriggerAsync_WhenTheStateProviderFails_ReleasesTheLockForLaterTriggers()
    {
        var provider = new FaultyStateProvider();
        await using var checker = CreateChecker(provider);
        provider.FailNextWrites(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => checker.TriggerAsync(Ct));

        await checker.TriggerAsync(Ct);

        Assert.Equal(PulseCheckerHealth.Healthy, (await checker.GetStateAsync(Ct)).LastResult?.Health);
    }

    // Cancellation means the check is being torn down, not that the component failed.
    [Fact]
    public async Task TriggerAsync_WhenCancelled_DoesNotRecordAFailedCheck()
    {
        await using var checker = CreateChecker();
        using var cts = new CancellationTokenSource();
        checker.OnCheck = async () =>
        {
            await cts.CancelAsync();
            cts.Token.ThrowIfCancellationRequested();
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => checker.TriggerAsync(cts.Token));

        var state = await checker.GetStateAsync(Ct);
        Assert.Null(state.LastResult);
        Assert.Equal(0, state.ConsecutiveFailureCount);
    }

    // A check that times out on its own token is the component failing, not a teardown.
    [Fact]
    public async Task TriggerAsync_WhenTheCheckCancelsItsOwnWork_RecordsAFailedCheck()
    {
        await using var checker = CreateChecker();
        checker.ThrowOnCheck = new OperationCanceledException("the check timed out internally");

        await checker.TriggerAsync(Ct);

        Assert.Equal(PulseCheckerHealth.Unhealthy, (await checker.GetStateAsync(Ct)).LastResult?.Health);
    }

    [Fact]
    public async Task TriggerAsync_WhenTheCheckThrowsWithinTheThreshold_DemotesToSuspicious()
    {
        await using var checker = new ControllablePulseChecker(new InMemoryStateProvider(), unhealthyThreshold: 2)
        {
            ThrowOnCheck = new InvalidOperationException("checker exploded"),
        };

        await checker.TriggerAsync(Ct);

        var state = await checker.GetStateAsync(Ct);
        Assert.Equal(PulseCheckerHealth.Suspicious, state.LastResult?.Health);
        Assert.Contains("checker exploded", state.LastResult?.Message);
    }

    [Fact]
    public async Task Name_DefaultsToTheFullTypeName()
    {
        await using var checker = CreateChecker();

        Assert.Equal(typeof(ControllablePulseChecker).FullName, checker.Name);
        Assert.Equal(checker.Name, checker.DisplayName);
    }
}

/// <summary>A pulse checker whose result, timing, and failures the test drives.</summary>
/// <param name="name">
/// Overrides the checker's name. Two instances of this type otherwise share one name, which
/// collides wherever checkers are keyed by it.
/// </param>
internal sealed class ControllablePulseChecker(
    IStateProvider stateProvider,
    uint unhealthyThreshold = 0,
    string? name = null)
    : PulseChecker(stateProvider, PulseInterval.EveryMinute, unhealthyThreshold)
{
    private readonly TaskCompletionSource _checkStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override string Name => name ?? base.Name;

    public PulseCheckerResult NextResult { get; set; } = new(PulseCheckerHealth.Healthy, "ok");

    public Exception? ThrowOnCheck { get; set; }

    public Func<Task>? OnCheck { get; set; }

    public int CheckCallCount { get; private set; }

    public Task WaitUntilCheckStartedAsync() => _checkStarted.Task;

    public override async Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        CheckCallCount++;
        _checkStarted.TrySetResult();

        if (OnCheck is not null)
        {
            await OnCheck().ConfigureAwait(false);
        }

        return ThrowOnCheck is not null ? throw ThrowOnCheck : NextResult;
    }
}
