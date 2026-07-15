using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.DependencyInjection;

namespace Healthie.Tests.Unit;

public class InMemoryStateProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetStateAsync_WhenNothingIsStoredForTheName_ReturnsDefault()
    {
        var provider = new InMemoryStateProvider();

        var state = await provider.GetStateAsync<PulseCheckerState>("absent", Ct);

        Assert.Null(state);
    }

    [Fact]
    public async Task GetStateAsync_AfterSetStateAsync_RoundTripsAllValues()
    {
        var provider = new InMemoryStateProvider();
        var executedAt = new DateTime(2026, 7, 15, 10, 30, 0, DateTimeKind.Utc);
        var stored = new PulseCheckerState(PulseInterval.Every5Seconds, unhealthyThreshold: 3)
        {
            ConsecutiveFailureCount = 2,
            IsActive = false,
            LastExecutionDateTime = executedAt,
            LastResult = new PulseCheckerResult(PulseCheckerHealth.Suspicious, "flaky"),
            History = [new PulseCheckerHistoryEntry(PulseCheckerHealth.Unhealthy, "boom", executedAt)],
        };

        await provider.SetStateAsync("checker", stored, Ct);
        var loaded = await provider.GetStateAsync<PulseCheckerState>("checker", Ct);

        Assert.NotNull(loaded);
        Assert.Equal(PulseInterval.Every5Seconds, loaded.Interval);
        Assert.Equal(3u, loaded.UnhealthyThreshold);
        Assert.Equal(2, loaded.ConsecutiveFailureCount);
        Assert.False(loaded.IsActive);
        Assert.Equal(executedAt, loaded.LastExecutionDateTime);
        Assert.Equal(PulseCheckerHealth.Suspicious, loaded.LastResult?.Health);
        Assert.Equal("flaky", loaded.LastResult?.Message);
        Assert.Equal(PulseCheckerHealth.Unhealthy, Assert.Single(loaded.History).Health);
    }

    // The state-change event compares the previously stored state against the incoming one.
    // A provider that handed back the live stored instance would make those two the same
    // object, the comparison would always report "no change", and StateChanged would never
    // fire. These two tests pin the copy semantics that behavior depends on.
    [Fact]
    public async Task GetStateAsync_ReturnsACopy_SoMutatingItLeavesStoredStateUntouched()
    {
        var provider = new InMemoryStateProvider();
        await provider.SetStateAsync("checker", new PulseCheckerState(), Ct);

        var first = await provider.GetStateAsync<PulseCheckerState>("checker", Ct);
        first!.ConsecutiveFailureCount = 99;
        first.History.Add(new PulseCheckerHistoryEntry(PulseCheckerHealth.Unhealthy, "boom", DateTime.UtcNow));

        var second = await provider.GetStateAsync<PulseCheckerState>("checker", Ct);

        Assert.NotSame(first, second);
        Assert.Equal(0, second!.ConsecutiveFailureCount);
        Assert.Empty(second.History);
    }

    [Fact]
    public async Task SetStateAsync_StoresACopy_SoMutatingTheSourceAfterwardsIsNotObserved()
    {
        var provider = new InMemoryStateProvider();
        var state = new PulseCheckerState();

        await provider.SetStateAsync("checker", state, Ct);
        state.ConsecutiveFailureCount = 42;
        state.History.Add(new PulseCheckerHistoryEntry(PulseCheckerHealth.Unhealthy, "boom", DateTime.UtcNow));

        var loaded = await provider.GetStateAsync<PulseCheckerState>("checker", Ct);

        Assert.Equal(0, loaded!.ConsecutiveFailureCount);
        Assert.Empty(loaded.History);
    }

    [Fact]
    public async Task SetStateAsync_WhenCalledTwiceForTheSameName_KeepsTheLatestValue()
    {
        var provider = new InMemoryStateProvider();

        await provider.SetStateAsync("checker", new PulseCheckerState { ConsecutiveFailureCount = 1 }, Ct);
        await provider.SetStateAsync("checker", new PulseCheckerState { ConsecutiveFailureCount = 7 }, Ct);

        var loaded = await provider.GetStateAsync<PulseCheckerState>("checker", Ct);

        Assert.Equal(7, loaded!.ConsecutiveFailureCount);
    }

    [Fact]
    public async Task GetStateAsync_KeepsStatesForDifferentNamesSeparate()
    {
        var provider = new InMemoryStateProvider();

        await provider.SetStateAsync("a", new PulseCheckerState { ConsecutiveFailureCount = 1 }, Ct);
        await provider.SetStateAsync("b", new PulseCheckerState { ConsecutiveFailureCount = 2 }, Ct);

        Assert.Equal(1, (await provider.GetStateAsync<PulseCheckerState>("a", Ct))!.ConsecutiveFailureCount);
        Assert.Equal(2, (await provider.GetStateAsync<PulseCheckerState>("b", Ct))!.ConsecutiveFailureCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetStateAsync_WhenNameIsBlank_Throws(string name)
    {
        var provider = new InMemoryStateProvider();

        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetStateAsync<PulseCheckerState>(name, Ct));
    }

    [Fact]
    public async Task GetStateAsync_WhenCancelled_Throws()
    {
        var provider = new InMemoryStateProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.GetStateAsync<PulseCheckerState>("checker", cts.Token));
    }
}
