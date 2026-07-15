using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;

namespace Healthie.Tests.Unit;

/// <summary>
/// State equality drives change detection: a checker raises StateChanged only when the newly
/// written state differs from what was stored. Comparing history by reference made that
/// decision meaningless, so these tests pin value semantics.
/// </summary>
public class PulseCheckerStateTests
{
    private static readonly DateTime ExecutedAt = new(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Equals_ForSeparateInstancesHoldingTheSameValues_ReturnsTrue()
    {
        var left = new PulseCheckerState(PulseInterval.Every5Seconds, 2);
        var right = new PulseCheckerState(PulseInterval.Every5Seconds, 2);

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Equals_ForEqualHistoriesInSeparateLists_ReturnsTrue()
    {
        var entry = new PulseCheckerHistoryEntry(PulseCheckerHealth.Healthy, "ok", ExecutedAt);
        var left = new PulseCheckerState { History = [entry] };
        var right = new PulseCheckerState { History = [entry] };

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Equals_WhenHistoryDiffers_ReturnsFalse()
    {
        var withHistory = new PulseCheckerState
        {
            History = [new PulseCheckerHistoryEntry(PulseCheckerHealth.Unhealthy, "boom", ExecutedAt)],
        };
        var withoutHistory = new PulseCheckerState();

        Assert.NotEqual(withHistory, withoutHistory);
    }

    [Fact]
    public void Equals_WhenHistoryOrderDiffers_ReturnsFalse()
    {
        var first = new PulseCheckerHistoryEntry(PulseCheckerHealth.Healthy, "ok", ExecutedAt);
        var second = new PulseCheckerHistoryEntry(PulseCheckerHealth.Unhealthy, "boom", ExecutedAt);

        Assert.NotEqual(
            new PulseCheckerState { History = [first, second] },
            new PulseCheckerState { History = [second, first] });
    }

    [Theory]
    [MemberData(nameof(DifferingStates))]
    public void Equals_WhenAnyValueDiffers_ReturnsFalse(PulseCheckerState modified)
    {
        Assert.NotEqual(new PulseCheckerState(), modified);
    }

    public static TheoryData<PulseCheckerState> DifferingStates() =>
    [
        new PulseCheckerState { ConsecutiveFailureCount = 1 },
        new PulseCheckerState { UnhealthyThreshold = 1 },
        new PulseCheckerState { Interval = PulseInterval.Every30Seconds },
        new PulseCheckerState { IsActive = false },
        new PulseCheckerState { IsHistoryEnabled = false },
        new PulseCheckerState { LastExecutionDateTime = ExecutedAt },
        new PulseCheckerState { LastResult = new PulseCheckerResult(PulseCheckerHealth.Unhealthy, "boom") },
    ];

    // `with` copies the History reference, so a snapshot taken before a trigger appends to
    // history would otherwise show the appended entry too.
    [Fact]
    public void With_WhenHistoryIsCopied_ProducesASnapshotUnaffectedByLaterAppends()
    {
        var state = new PulseCheckerState();
        var snapshot = state with { History = [.. state.History] };

        state.History.Add(new PulseCheckerHistoryEntry(PulseCheckerHealth.Unhealthy, "boom", ExecutedAt));

        Assert.Empty(snapshot.History);
        Assert.Single(state.History);
        Assert.NotEqual(snapshot, state);
    }
}
