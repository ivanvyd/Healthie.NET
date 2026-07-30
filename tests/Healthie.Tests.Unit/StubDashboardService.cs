using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Dashboard.Services;

namespace Healthie.Tests.Unit;

/// <summary>
/// An <see cref="IHealthieDashboardService"/> whose every member refuses, so a test double can
/// override the few it actually needs.
/// </summary>
/// <remarks>
/// The same bargain as <see cref="StubContainer"/>: the interface has sixteen members and a test of
/// the component's lifecycle touches three. Refusing rather than returning a default is deliberate
/// -- a test that reaches an unstubbed member has left the path it meant to exercise and should say
/// so rather than quietly carry on.
/// </remarks>
internal abstract class StubDashboardService : IHealthieDashboardService
{
    private static NotSupportedException NotStubbed(string member) =>
        new($"{member} is not stubbed. Override it if the test under way is meant to reach it.");

    public virtual Task SubscribeToStateChangesAsync(Func<string, PulseCheckerState, Task> onStateChanged, CancellationToken cancellationToken = default) => throw NotStubbed(nameof(SubscribeToStateChangesAsync));

    public virtual Task UnsubscribeFromStateChangesAsync(Func<string, PulseCheckerState, Task> onStateChanged, CancellationToken cancellationToken = default) => throw NotStubbed(nameof(UnsubscribeFromStateChangesAsync));

    public virtual Task<Dictionary<string, PulseCheckerState>> GetAllStatesAsync(CancellationToken cancellationToken = default) => throw NotStubbed(nameof(GetAllStatesAsync));

    public virtual Task<Dictionary<string, string>> GetDisplayNamesAsync(CancellationToken cancellationToken = default) => throw NotStubbed(nameof(GetDisplayNamesAsync));

    public virtual Task TriggerAllAsync(CancellationToken cancellationToken = default) => throw NotStubbed(nameof(TriggerAllAsync));

    public virtual Task SetIntervalAsync(string name, PulseInterval interval, CancellationToken cancellationToken = default) => throw NotStubbed(nameof(SetIntervalAsync));

    public virtual Task SetThresholdAsync(string name, uint threshold, CancellationToken cancellationToken = default) => throw NotStubbed(nameof(SetThresholdAsync));

    public virtual Task SetTagsAsync(string name, IReadOnlyList<string> tags, CancellationToken cancellationToken = default) => throw NotStubbed(nameof(SetTagsAsync));

    public virtual Task SetPinnedAsync(string name, bool pinned, CancellationToken cancellationToken = default) => throw NotStubbed(nameof(SetPinnedAsync));

    public virtual Task SetGroupAsync(string name, string? group, CancellationToken cancellationToken = default) => throw NotStubbed(nameof(SetGroupAsync));

    public virtual Task StartAsync(string name, CancellationToken cancellationToken = default) => throw NotStubbed(nameof(StartAsync));

    public virtual Task StopAsync(string name, CancellationToken cancellationToken = default) => throw NotStubbed(nameof(StopAsync));

    public virtual Task TriggerAsync(string name, CancellationToken cancellationToken = default) => throw NotStubbed(nameof(TriggerAsync));

    public virtual Task ResetAsync(string name, CancellationToken cancellationToken = default) => throw NotStubbed(nameof(ResetAsync));

    public virtual Task StartAllAsync(CancellationToken cancellationToken = default) => throw NotStubbed(nameof(StartAllAsync));

    public virtual Task StopAllAsync(CancellationToken cancellationToken = default) => throw NotStubbed(nameof(StopAllAsync));

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
