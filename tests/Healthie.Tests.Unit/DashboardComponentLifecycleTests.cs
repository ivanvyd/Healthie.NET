using Bunit;
using Healthie.Abstractions.Models;
using Healthie.Dashboard;
using Healthie.Dashboard.Components;
using Healthie.Dashboard.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.Tests.Unit;

/// <summary>
/// The dashboard component's own lifecycle: that it subscribes when it is built and unsubscribes
/// when it is torn down.
/// </summary>
/// <remarks>
/// The service-level unsubscribe was covered; the component calling it was not, and that is the half
/// that leaks. A host routing to the dashboard inside its own layout builds a new component every
/// time the user navigates back to it, on the same circuit, so a component that never unsubscribes
/// leaves a handler behind on every visit.
/// </remarks>
public sealed class DashboardComponentLifecycleTests : IDisposable
{
    /// <summary>Records what the component asked of the service, and refuses everything else.</summary>
    private sealed class RecordingDashboardService : StubDashboardService
    {
        public List<Func<string, PulseCheckerState, Task>> Subscribed { get; } = [];

        public List<Func<string, PulseCheckerState, Task>> Unsubscribed { get; } = [];

        public override Task SubscribeToStateChangesAsync(
            Func<string, PulseCheckerState, Task> onStateChanged,
            CancellationToken cancellationToken = default)
        {
            Subscribed.Add(onStateChanged);
            return Task.CompletedTask;
        }

        public override Task UnsubscribeFromStateChangesAsync(
            Func<string, PulseCheckerState, Task> onStateChanged,
            CancellationToken cancellationToken = default)
        {
            Unsubscribed.Add(onStateChanged);
            return Task.CompletedTask;
        }

        public override Task<Dictionary<string, PulseCheckerState>> GetAllStatesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new Dictionary<string, PulseCheckerState>(StringComparer.Ordinal));

        public override Task<Dictionary<string, string>> GetDisplayNamesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private readonly BunitContext _context = new();
    private readonly RecordingDashboardService _service = new();

    public DashboardComponentLifecycleTests()
    {
        _context.Services.AddSingleton<IHealthieDashboardService>(_service);
        _context.Services.AddSingleton(new HealthieUIOptions());
        _context.Services.AddSingleton<HealthieThemeState>();
        _context.Services.AddSingleton<DashboardStateHandoff>();

        // PersistentComponentState is built by the framework rather than newed up, so it is
        // registered the way ASP.NET Core registers it: from the manager that owns it.
        _context.Services.AddLogging();
        _context.Services.AddSingleton<ComponentStatePersistenceManager>();
        _context.Services.AddSingleton(sp =>
            sp.GetRequiredService<ComponentStatePersistenceManager>().State);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public void RenderingTheDashboard_Subscribes()
    {
        _context.Render<HealthieDashboard>();

        Assert.Single(_service.Subscribed);
        Assert.Empty(_service.Unsubscribed);
    }

    /// <summary>
    /// The half that was missing. The handler it hands back has to be the one it subscribed with, or
    /// the service cannot remove it and the leak stands.
    /// </summary>
    [Fact]
    public async Task DisposingTheDashboard_UnsubscribesTheHandlerItSubscribed()
    {
        var rendered = _context.Render<HealthieDashboard>();

        Assert.Single(_service.Subscribed);

        await rendered.Instance.DisposeAsync();

        Assert.Single(_service.Unsubscribed);
        Assert.Equal(_service.Subscribed[0], _service.Unsubscribed[0]);
    }

    /// <summary>
    /// Mounting the dashboard twice on one circuit is the case this exists for: navigating away and
    /// back builds a second component, and the first must have taken its handler with it.
    /// </summary>
    [Fact]
    public async Task NavigatingAwayAndBack_LeavesOneSubscriptionBehindNotTwo()
    {
        var first = _context.Render<HealthieDashboard>();
        await first.Instance.DisposeAsync();

        var second = _context.Render<HealthieDashboard>();

        Assert.Equal(2, _service.Subscribed.Count);
        Assert.Single(_service.Unsubscribed);

        // One subscribed and not yet unsubscribed: the component now on screen.
        var outstanding = _service.Subscribed.Count - _service.Unsubscribed.Count;
        Assert.Equal(1, outstanding);

        await second.Instance.DisposeAsync();
        Assert.Equal(_service.Subscribed.Count, _service.Unsubscribed.Count);
    }
}
