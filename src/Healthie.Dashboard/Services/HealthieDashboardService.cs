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
    private Action<string, PulseCheckerState>? _onStateChanged;
    private readonly List<(IPulseChecker Checker, EventHandler<PulseCheckerStateChangedEventArgs> Handler)> _subscriptions = [];

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
    public async Task<Dictionary<string, IPulseChecker>> GetAllCheckersAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await pulsesScheduler.GetPulseCheckersAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to retrieve pulse checkers.");
            return new Dictionary<string, IPulseChecker>();
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
    public async Task<List<PulseCheckerHistoryEntry>> GetHistoryAsync(string name,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await pulsesScheduler.GetHistoryAsync(name, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to get history for checker '{CheckerName}'.", name);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task ClearHistoryAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            await pulsesScheduler.ClearHistoryAsync(name, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to clear history for checker '{CheckerName}'.", name);
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

            foreach (var (name, _) in checkers)
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
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to start all checkers.");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var checkers = await pulsesScheduler.GetPulseCheckersAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var (name, _) in checkers)
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
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to stop all checkers.");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SetHistoryEnabledAsync(string name, bool enabled,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await pulsesScheduler.SetHistoryEnabledAsync(name, enabled, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to set history enabled for checker '{CheckerName}'.", name);
            throw;
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
    public void SubscribeToStateChanges(Action<string, PulseCheckerState> onStateChanged)
    {
        _onStateChanged = onStateChanged;
    }

    /// <summary>
    /// Subscribes to state change events on all registered pulse checkers.
    /// Should be called after the service is initialized and checkers are available.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    internal async Task InitializeSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var checkers = await pulsesScheduler.GetPulseCheckersAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var (name, checker) in checkers)
        {
            EventHandler<PulseCheckerStateChangedEventArgs> handler = (_, args) =>
            {
                _onStateChanged?.Invoke(name, args.NewState);
            };

            checker.StateChanged += handler;
            _subscriptions.Add((checker, handler));
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
        _onStateChanged = null;

        return ValueTask.CompletedTask;
    }
}
