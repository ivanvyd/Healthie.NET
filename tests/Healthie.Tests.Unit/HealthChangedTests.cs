using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;

namespace Healthie.Tests.Unit;

/// <summary>
/// <c>StateChanged</c> fires on every check, because a stored result always moves the execution
/// time. That is not a defect -- the dashboard redraws from it -- but it means every handler that
/// only cares about a component going down has to work out for itself whether the health moved.
/// Two packages were doing exactly that, identically, so the question now has one answer.
/// </summary>
public class HealthChangedTests
{
    private static PulseCheckerState With(PulseCheckerHealth? health) =>
        health is { } value
            ? new PulseCheckerState { LastResult = new PulseCheckerResult(value, string.Empty) }
            : new PulseCheckerState();

    private static PulseCheckerStateChangedEventArgs Change(
        PulseCheckerHealth? from,
        PulseCheckerHealth? to) => new(With(from), With(to));

    [Fact]
    public void ACheckRepeatingItsResult_IsNotAHealthChange()
    {
        var args = Change(PulseCheckerHealth.Healthy, PulseCheckerHealth.Healthy);

        Assert.False(args.HealthChanged);
        Assert.Equal(PulseCheckerHealth.Healthy, args.PreviousHealth);
        Assert.Equal(PulseCheckerHealth.Healthy, args.CurrentHealth);
    }

    [Fact]
    public void GoingUnhealthy_IsAHealthChange()
    {
        var args = Change(PulseCheckerHealth.Healthy, PulseCheckerHealth.Unhealthy);

        Assert.True(args.HealthChanged);
        Assert.Equal(PulseCheckerHealth.Healthy, args.PreviousHealth);
        Assert.Equal(PulseCheckerHealth.Unhealthy, args.CurrentHealth);
    }

    /// <summary>
    /// Nothing known to something known is a change: it is the first thing anyone learns about the
    /// component, and an alerting rule that ignored it would stay silent through a cold start into
    /// an outage.
    /// </summary>
    [Fact]
    public void TheFirstResult_IsAHealthChange()
    {
        var args = Change(null, PulseCheckerHealth.Unhealthy);

        Assert.True(args.HealthChanged);
        Assert.Null(args.PreviousHealth);
    }

    /// <summary>
    /// A state with no result is not a report of good health, so losing one is not a transition to
    /// anything. Treating it as one would fire an alert whose "current health" is nothing at all.
    /// </summary>
    [Fact]
    public void LosingAResult_IsNotAHealthChange()
    {
        var args = Change(PulseCheckerHealth.Unhealthy, null);

        Assert.False(args.HealthChanged);
    }

    [Fact]
    public void ASettingChangeThatKeepsTheResult_IsNotAHealthChange()
    {
        var result = new PulseCheckerResult(PulseCheckerHealth.Suspicious, "flapping");

        var args = new PulseCheckerStateChangedEventArgs(
            new PulseCheckerState { LastResult = result, Group = "before" },
            new PulseCheckerState { LastResult = result, Group = "after" });

        Assert.False(args.HealthChanged);
    }
}
