using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;
using Healthie.DependencyInjection;

namespace Healthie.Tests.Unit;

/// <summary>
/// Groups and tags: a checker sits in at most one group and carries any number of tags. Both are
/// declared in code as defaults and can be changed afterwards, and both live in state so that a
/// change outlives the process.
/// </summary>
public class GroupAndTagTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A checker that declares both a group and tags in code.</summary>
    private sealed class DescribedPulseChecker(IStateProvider stateProvider)
        : PulseChecker(stateProvider, PulseInterval.EveryMinute)
    {
        public override string DefaultGroup => "Data Stores";

        public override IReadOnlyList<string> DefaultTags => ["tier-1", "cache"];

        public override Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PulseCheckerResult(PulseCheckerHealth.Healthy, "ok"));
    }

    /// <summary>A checker that declares neither, which is the default.</summary>
    private sealed class PlainPulseChecker(IStateProvider stateProvider)
        : PulseChecker(stateProvider, PulseInterval.EveryMinute)
    {
        public override Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PulseCheckerResult(PulseCheckerHealth.Healthy, "ok"));
    }

    [Fact]
    public async Task GetStateAsync_WhenNothingIsStored_SeedsTheGroupAndTagsDeclaredInCode()
    {
        await using var checker = new DescribedPulseChecker(new InMemoryStateProvider());

        var state = await checker.GetStateAsync(Ct);

        Assert.Equal("Data Stores", state.Group);
        Assert.Equal(["cache", "tier-1"], state.Tags);
    }

    [Fact]
    public async Task GetStateAsync_WhenNothingIsDeclared_LeavesTheCheckerUngroupedAndUntagged()
    {
        await using var checker = new PlainPulseChecker(new InMemoryStateProvider());

        var state = await checker.GetStateAsync(Ct);

        Assert.Null(state.Group);
        Assert.Empty(state.Tags);
    }

    /// <summary>
    /// The defaults seed a checker that has never run. Once a group has been chosen on the
    /// dashboard, a restart must not quietly put the checker back where the code says it goes.
    /// </summary>
    [Fact]
    public async Task GetStateAsync_WhenAGroupWasAlreadyStored_KeepsItRatherThanReseedingTheDefault()
    {
        var stateProvider = new InMemoryStateProvider();
        await using (var first = new DescribedPulseChecker(stateProvider))
        {
            await first.SetGroupAsync("Moved By Hand", Ct);
        }

        await using var second = new DescribedPulseChecker(stateProvider);

        Assert.Equal("Moved By Hand", (await second.GetStateAsync(Ct)).Group);
    }

    [Fact]
    public async Task SetGroupAsync_StoresTheGroup()
    {
        await using var checker = new PlainPulseChecker(new InMemoryStateProvider());

        await checker.SetGroupAsync("Messaging", Ct);

        Assert.Equal("Messaging", (await checker.GetStateAsync(Ct)).Group);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SetGroupAsync_WhenGivenNothing_ClearsTheGroupRatherThanStoringABlankOne(string? group)
    {
        await using var checker = new DescribedPulseChecker(new InMemoryStateProvider());

        await checker.SetGroupAsync(group, Ct);

        Assert.Null((await checker.GetStateAsync(Ct)).Group);
    }

    [Fact]
    public async Task SetGroupAsync_TrimsTheGroupSoThatSpacingCannotCreateASecondOne()
    {
        await using var checker = new PlainPulseChecker(new InMemoryStateProvider());

        await checker.SetGroupAsync("  Messaging  ", Ct);

        Assert.Equal("Messaging", (await checker.GetStateAsync(Ct)).Group);
    }

    [Fact]
    public async Task SetGroupAsync_RaisesStateChangedOnlyWhenTheGroupActuallyChanges()
    {
        await using var checker = new PlainPulseChecker(new InMemoryStateProvider());
        var raised = 0;
        checker.StateChanged += (_, _) => raised++;

        await checker.SetGroupAsync("Messaging", Ct);
        await checker.SetGroupAsync("Messaging", Ct);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task SetTagsAsync_StoresTheTags()
    {
        await using var checker = new PlainPulseChecker(new InMemoryStateProvider());

        await checker.SetTagsAsync(["alpha", "beta"], Ct);

        Assert.Equal(["alpha", "beta"], (await checker.GetStateAsync(Ct)).Tags);
    }

    [Fact]
    public async Task SetTagsAsync_TrimsDropsBlanksDeduplicatesAndOrders()
    {
        await using var checker = new PlainPulseChecker(new InMemoryStateProvider());

        await checker.SetTagsAsync(["  zulu ", "alpha", "", "   ", "ALPHA", "zulu"], Ct);

        Assert.Equal(["alpha", "zulu"], (await checker.GetStateAsync(Ct)).Tags);
    }

    /// <summary>
    /// Tags are ordered when they are stored, so the order they were typed in is not a difference.
    /// A checker that re-declares the same tags must not report a state change on every check.
    /// </summary>
    [Fact]
    public async Task SetTagsAsync_RaisesStateChangedOnlyWhenTheTagsActuallyChange()
    {
        await using var checker = new PlainPulseChecker(new InMemoryStateProvider());
        var raised = 0;
        checker.StateChanged += (_, _) => raised++;

        await checker.SetTagsAsync(["beta", "alpha"], Ct);
        await checker.SetTagsAsync(["alpha", " beta "], Ct);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task SetTagsAsync_WhenGivenNull_Throws()
    {
        await using var checker = new PlainPulseChecker(new InMemoryStateProvider());

        await Assert.ThrowsAsync<ArgumentNullException>(() => checker.SetTagsAsync(null!, Ct));
    }

    [Fact]
    public async Task SetPinnedAsync_StoresThePin()
    {
        await using var checker = new PlainPulseChecker(new InMemoryStateProvider());

        await checker.SetPinnedAsync(true, Ct);

        Assert.True((await checker.GetStateAsync(Ct)).IsPinned);
    }

    [Fact]
    public async Task SetPinnedAsync_RaisesStateChangedOnlyWhenThePinActuallyChanges()
    {
        await using var checker = new PlainPulseChecker(new InMemoryStateProvider());
        var raised = 0;
        checker.StateChanged += (_, _) => raised++;

        await checker.SetPinnedAsync(true, Ct);
        await checker.SetPinnedAsync(true, Ct);

        Assert.Equal(1, raised);
    }

    /// <summary>
    /// State changes are detected by comparing an old state against a new one, and the two always
    /// come from separate reads. A record compares a list by reference, which would report two
    /// identical tag lists as different and raise a state change on every single check.
    /// </summary>
    [Fact]
    public void Equals_ComparesTagsByValueRatherThanByReference()
    {
        var left = new PulseCheckerState(PulseInterval.EveryMinute) { Tags = ["alpha", "beta"] };
        var right = new PulseCheckerState(PulseInterval.EveryMinute) { Tags = ["alpha", "beta"] };

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Equals_TreatsDifferentTagsAsDifferentStates()
    {
        var left = new PulseCheckerState(PulseInterval.EveryMinute) { Tags = ["alpha"] };
        var right = new PulseCheckerState(PulseInterval.EveryMinute) { Tags = ["beta"] };

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void Equals_TreatsADifferentGroupAsADifferentState()
    {
        var left = new PulseCheckerState(PulseInterval.EveryMinute) { Group = "Data Stores" };
        var right = new PulseCheckerState(PulseInterval.EveryMinute) { Group = "Messaging" };

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void Equals_TreatsADifferentPinAsADifferentState()
    {
        var left = new PulseCheckerState(PulseInterval.EveryMinute) { IsPinned = true };
        var right = new PulseCheckerState(PulseInterval.EveryMinute) { IsPinned = false };

        Assert.NotEqual(left, right);
    }
}
