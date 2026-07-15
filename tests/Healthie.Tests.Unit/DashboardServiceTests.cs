using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Healthie.Abstractions.StateProviding;
using Healthie.Dashboard.Services;
using Healthie.DependencyInjection;

namespace Healthie.Tests.Unit;

/// <summary>
/// Covers the service the dashboard talks to. The dashboard's live updates all hang off
/// <see cref="IHealthieDashboardService.SubscribeToStateChangesAsync"/>, so a break here shows up
/// as a dashboard that silently stops updating.
/// </summary>
public class DashboardServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IHealthieDashboardService CreateService(params IPulseChecker[] checkers) =>
        new HealthieDashboardService(
            new PulsesScheduler(checkers, new CustomPulseScheduler(), new HealthieOptions()));

    /// <summary>A service over one real <see cref="PulseChecker"/>, for the paths that need one.</summary>
    private static (IHealthieDashboardService Service, ControllablePulseChecker Checker) CreateServiceWithRealChecker(
        params ControllablePulseChecker[] extra)
    {
        var checker = new ControllablePulseChecker(new InMemoryStateProvider());
        return (CreateService([checker, .. extra]), checker);
    }

    [Fact]
    public async Task SubscribeToStateChangesAsync_WhenACheckerChangesState_NotifiesTheSubscriber()
    {
        var (service, checker) = CreateServiceWithRealChecker();
        await using var _ = service;

        var received = new TaskCompletionSource<(string Name, PulseCheckerState State)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await service.SubscribeToStateChangesAsync((name, state) =>
        {
            received.TrySetResult((name, state));
            return Task.CompletedTask;
        }, Ct);

        checker.NextResult = new PulseCheckerResult(PulseCheckerHealth.Unhealthy, "down");
        await checker.TriggerAsync(Ct);

        var (notifiedName, notifiedState) = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        Assert.Equal(checker.Name, notifiedName);
        Assert.Equal(PulseCheckerHealth.Unhealthy, notifiedState.LastResult?.Health);
    }

    // The service attaches to each checker once however many handlers register. A second subscriber
    // must still be notified -- getting this wrong leaves a live-looking dashboard that never updates.
    [Fact]
    public async Task SubscribeToStateChangesAsync_WithSeveralSubscribers_NotifiesEveryOne()
    {
        var (service, checker) = CreateServiceWithRealChecker();
        await using var _ = service;

        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await service.SubscribeToStateChangesAsync((_, _) => { first.TrySetResult(); return Task.CompletedTask; }, Ct);
        await service.SubscribeToStateChangesAsync((_, _) => { second.TrySetResult(); return Task.CompletedTask; }, Ct);

        checker.NextResult = new PulseCheckerResult(PulseCheckerHealth.Unhealthy, "down");
        await checker.TriggerAsync(Ct);

        await Task.WhenAll(first.Task, second.Task).WaitAsync(TimeSpan.FromSeconds(5), Ct);
    }

    // A subscriber throws on the thread that ran the check, so an unguarded handler would take down
    // the checker's own trigger, not just its own update.
    [Fact]
    public async Task SubscribeToStateChangesAsync_WhenOneSubscriberThrows_StillNotifiesTheOthers()
    {
        var (service, checker) = CreateServiceWithRealChecker();
        await using var _ = service;

        var survivor = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await service.SubscribeToStateChangesAsync((_, _) => throw new InvalidOperationException("subscriber blew up"), Ct);
        await service.SubscribeToStateChangesAsync((_, _) => { survivor.TrySetResult(); return Task.CompletedTask; }, Ct);

        checker.NextResult = new PulseCheckerResult(PulseCheckerHealth.Unhealthy, "down");
        await checker.TriggerAsync(Ct);

        await survivor.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
    }

    // The circuit ending must detach the event handler, not merely stop calling subscribers: a
    // checker left holding a handler keeps the whole dashboard graph alive, one leak per page view.
    // Asserting on "no notifications arrive" would pass even if the handler stayed attached, since
    // disposal drops the subscribers too -- so this asserts on the checker's own subscriber count.
    [Fact]
    public async Task DisposeAsync_DetachesTheHandlerFromEveryChecker()
    {
        var checker = new FakePulseChecker("leaky");
        var service = CreateService(checker);

        await service.SubscribeToStateChangesAsync((_, _) => Task.CompletedTask, Ct);
        Assert.Equal(1, checker.SubscriberCount);

        await service.DisposeAsync();

        Assert.Equal(0, checker.SubscriberCount);
    }

    [Fact]
    public async Task TriggerAllAsync_RunsEveryChecker()
    {
        var first = new FakePulseChecker("first");
        var second = new FakePulseChecker("second");
        await using var service = CreateService(first, second);

        await service.TriggerAllAsync(Ct);

        Assert.Equal(1, first.TriggerCount);
        Assert.Equal(1, second.TriggerCount);
    }

    // A checker whose own storage is unreachable throws out of TriggerAsync. On a full sweep that
    // must not stop the other checkers from running, and must not surface to the operator as the
    // whole sweep failing.
    [Fact]
    public async Task TriggerAllAsync_WhenOneCheckerThrows_StillRunsTheRest()
    {
        var faulty = new FakePulseChecker("faulty") { ThrowOnTrigger = new InvalidOperationException("storage unreachable") };
        var healthy = new FakePulseChecker("healthy");
        await using var service = CreateService(faulty, healthy);

        await service.TriggerAllAsync(Ct);

        Assert.Equal(1, healthy.TriggerCount);
    }

    [Fact]
    public async Task GetAllStatesAsync_ReturnsAStateForEveryRegisteredChecker()
    {
        var second = new ControllablePulseChecker(new InMemoryStateProvider(), name: "second");
        var (service, first) = CreateServiceWithRealChecker(second);
        await using var _ = service;

        var states = await service.GetAllStatesAsync(Ct);

        Assert.Equal(2, states.Count);
        Assert.Contains(first.Name, states.Keys);
        Assert.Contains(second.Name, states.Keys);
    }

    // The dashboard reads history straight off the state rather than fetching it separately, so the
    // state it gets back has to carry it.
    [Fact]
    public async Task GetAllStatesAsync_ReturnsStatesCarryingTheirHistory()
    {
        var (service, checker) = CreateServiceWithRealChecker();
        await using var _ = service;

        await checker.TriggerAsync(Ct);
        await checker.TriggerAsync(Ct);

        var states = await service.GetAllStatesAsync(Ct);

        Assert.Equal(2, states[checker.Name].History.Count);
    }
}
