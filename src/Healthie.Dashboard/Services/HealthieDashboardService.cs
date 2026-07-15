using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Microsoft.Extensions.Logging;

namespace Healthie.Dashboard.Services;

/// <summary>
/// Internal service that wraps <see cref="IPulsesScheduler"/> for dashboard component communication.
/// Manages event subscriptions and provides error handling with logging.
/// </summary>
internal sealed class HealthieDashboardService(
    IPulsesScheduler pulsesScheduler,
    ILogger<HealthieDashboardService>? logger = null) : IHealthieDashboardService
{
    private readonly List<Func<string, PulseCheckerState, Task>> _handlers = [];
    private readonly List<(IPulseChecker Checker, EventHandler<PulseCheckerStateChangedEventArgs> Handler)> _subscriptions = [];
    private readonly SemaphoreSlim _handlersLock = new(1, 1);

    /// <inheritdoc />
    public async Task<Dictionary<string, PulseCheckerState>> GetAllStatesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await pulsesScheduler.GetPulsesStatesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to retrieve pulse checker states.");
            return new Dictionary<string, PulseCheckerState>();
        }
    }

    /// <inheritdoc />
    public async Task SetIntervalAsync(string name, PulseInterval interval,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await pulsesScheduler.SetIntervalAsync(name, interval, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to set interval for checker '{CheckerName}'.", name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SetThresholdAsync(string name, uint threshold,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await pulsesScheduler.SetUnhealthyThresholdAsync(name, threshold, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to set threshold for checker '{CheckerName}'.", name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SetTagsAsync(string name, IReadOnlyList<string> tags,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await pulsesScheduler.SetTagsAsync(name, tags, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to set tags for checker '{CheckerName}'.", name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SetPinnedAsync(string name, bool pinned,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await pulsesScheduler.SetPinnedAsync(name, pinned, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to pin checker '{CheckerName}'.", name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SetGroupAsync(string name, string? group,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await pulsesScheduler.SetGroupAsync(name, group, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to set the group for checker '{CheckerName}'.", name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            await pulsesScheduler.ActivateAsync(name, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to start checker '{CheckerName}'.", name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            await pulsesScheduler.DeactivateAsync(name, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to stop checker '{CheckerName}'.", name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task TriggerAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            var checkers = await pulsesScheduler.GetPulseCheckersAsync(cancellationToken)
                .ConfigureAwait(false);

            if (checkers.TryGetValue(name, out var checker))
            {
                await checker.TriggerAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw new ArgumentException($"Pulse checker '{name}' not found.", nameof(name));
            }
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            logger?.LogError(ex, "Failed to trigger checker '{CheckerName}'.", name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ResetAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            await pulsesScheduler.ResetAsync(name, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to reset checker '{CheckerName}'.", name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StartAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var checkers = await pulsesScheduler.GetPulseCheckersAsync(cancellationToken)
                .ConfigureAwait(false);

            await Task.WhenAll(checkers.Select(c => ActivateCheckerAsync(c.Key, cancellationToken)))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to start all checkers.");
            throw;
        }
    }

    private async Task ActivateCheckerAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            await pulsesScheduler.ActivateAsync(name, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to start checker '{CheckerName}'.", name);
        }
    }

    /// <inheritdoc />
    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var checkers = await pulsesScheduler.GetPulseCheckersAsync(cancellationToken)
                .ConfigureAwait(false);

            await Task.WhenAll(checkers.Select(c => DeactivateCheckerAsync(c.Key, cancellationToken)))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to stop all checkers.");
            throw;
        }
    }

    private async Task DeactivateCheckerAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            await pulsesScheduler.DeactivateAsync(name, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to stop checker '{CheckerName}'.", name);
        }
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, string>> GetDisplayNamesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var checkers = await pulsesScheduler.GetPulseCheckersAsync(cancellationToken)
                .ConfigureAwait(false);

            return checkers.ToDictionary(c => c.Key, c => c.Value.DisplayName);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to retrieve display names.");
            return new Dictionary<string, string>();
        }
    }

    /// <inheritdoc />
    public async Task TriggerAllAsync(CancellationToken cancellationToken = default)
    {
        var checkers = await pulsesScheduler.GetPulseCheckersAsync(cancellationToken)
            .ConfigureAwait(false);

        await Task.WhenAll(checkers.Keys.Select(name => TriggerCheckerAsync(name, cancellationToken)))
            .ConfigureAwait(false);
    }

    private async Task TriggerCheckerAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            await TriggerAsync(name, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to trigger checker '{CheckerName}'.", name);
        }
    }

    /// <inheritdoc />
    public async Task SubscribeToStateChangesAsync(
        Func<string, PulseCheckerState, Task> onStateChanged,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onStateChanged);

        await _handlersLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _handlers.Add(onStateChanged);

            // The checkers are attached to once, however many handlers register.
            if (_subscriptions.Count > 0)
            {
                return;
            }

            var checkers = await pulsesScheduler.GetPulseCheckersAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var (name, checker) in checkers)
            {
                EventHandler<PulseCheckerStateChangedEventArgs> handler = (_, args) =>
                    _ = NotifyAsync(name, args.NewState);

                checker.StateChanged += handler;
                _subscriptions.Add((checker, handler));
            }
        }
        finally
        {
            _handlersLock.Release();
        }
    }

    /// <summary>
    /// Hands a state change to every subscriber.
    /// </summary>
    /// <remarks>
    /// Raised from whichever thread ran the check, and a subscriber that throws must not take down
    /// that thread, so each is isolated.
    /// </remarks>
    private async Task NotifyAsync(string name, PulseCheckerState state)
    {
        foreach (var handler in _handlers.ToArray())
        {
            try
            {
                await handler(name, state).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "A subscriber threw while handling a state change for '{CheckerName}'.", name);
            }
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        foreach (var (checker, handler) in _subscriptions)
        {
            try
            {
                checker.StateChanged -= handler;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to unsubscribe from checker StateChanged event during disposal.");
            }
        }

        _subscriptions.Clear();
        _handlers.Clear();
        _handlersLock.Dispose();

        return ValueTask.CompletedTask;
    }
}
