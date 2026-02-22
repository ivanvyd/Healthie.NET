using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Microsoft.Extensions.Hosting;

namespace Healthie.Abstractions.Scheduling;

/// <summary>
/// Background service that schedules and manages all registered pulse checkers.
/// </summary>
/// <remarks>
/// This service starts all active pulse checkers on application startup and provides
/// methods to manage individual checkers at runtime (set interval, activate, deactivate, etc.).
/// </remarks>
public class PulsesScheduler : BackgroundService, IPulsesScheduler
{
    private readonly IEnumerable<IPulseChecker> _pulseCheckers;
    private readonly IPulseScheduler _pulseScheduler;
    private readonly HealthieOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="PulsesScheduler"/> class.
    /// </summary>
    /// <param name="pulseCheckers">The collection of pulse checkers to schedule.</param>
    /// <param name="pulseScheduler">The scheduler responsible for individual pulse checks.</param>
    /// <param name="options">Global Healthie options.</param>
    public PulsesScheduler(IEnumerable<IPulseChecker> pulseCheckers, IPulseScheduler pulseScheduler, HealthieOptions options)
    {
        _pulseCheckers = pulseCheckers ?? throw new ArgumentNullException(nameof(pulseCheckers));
        _pulseScheduler = pulseScheduler ?? throw new ArgumentNullException(nameof(pulseScheduler));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public Task<Dictionary<string, IPulseChecker>> GetPulseCheckersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_pulseCheckers.ToDictionary(checker => checker.Name, checker => checker));
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, PulseCheckerState>> GetPulsesStatesAsync(CancellationToken cancellationToken = default)
    {
        var pulsesStates = await Task.WhenAll(_pulseCheckers.Select(async checker =>
        {
            var state = await checker.GetStateAsync(cancellationToken).ConfigureAwait(false);

            return new
            {
                checker.Name,
                State = state,
            };
        })).ConfigureAwait(false);

        return pulsesStates.ToDictionary(checker => checker.Name, checker => checker.State);
    }

    /// <inheritdoc />
    public async Task SetIntervalAsync(string name, PulseInterval interval, CancellationToken cancellationToken = default)
    {
        var pulseChecker = GetCheckerOrThrow(name);
        await pulseChecker.SetIntervalAsync(interval, cancellationToken).ConfigureAwait(false);
        await ScheduleAsync(pulseChecker, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetUnhealthyThresholdAsync(string name, uint threshold, CancellationToken cancellationToken = default)
    {
        var pulseChecker = GetCheckerOrThrow(name);
        await pulseChecker.SetUnhealthyThresholdAsync(threshold, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ResetAsync(string name, CancellationToken cancellationToken = default)
    {
        var pulseChecker = GetCheckerOrThrow(name);
        await pulseChecker.ResetAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ActivateAsync(string name, CancellationToken cancellationToken = default)
    {
        var pulseChecker = GetCheckerOrThrow(name);
        bool wasAlreadyActive = await pulseChecker.StartAsync(cancellationToken).ConfigureAwait(false);

        if (!wasAlreadyActive)
        {
            await ScheduleAsync(pulseChecker, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task DeactivateAsync(string name, CancellationToken cancellationToken = default)
    {
        var pulseChecker = GetCheckerOrThrow(name);
        bool isStopped = await pulseChecker.StopAsync(cancellationToken).ConfigureAwait(false);

        if (isStopped)
        {
            await _pulseScheduler.UnscheduleAsync(pulseChecker, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<List<PulseCheckerHistoryEntry>> GetHistoryAsync(string name, CancellationToken cancellationToken = default)
    {
        var pulseChecker = GetCheckerOrThrow(name);
        return await pulseChecker.GetHistoryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ClearHistoryAsync(string name, CancellationToken cancellationToken = default)
    {
        var pulseChecker = GetCheckerOrThrow(name);
        await pulseChecker.ClearHistoryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetHistoryEnabledAsync(string name, bool enabled, CancellationToken cancellationToken = default)
    {
        var pulseChecker = GetCheckerOrThrow(name);
        await pulseChecker.SetHistoryEnabledAsync(enabled, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var maxHistory = (int)Math.Clamp(_options.MaxHistoryLength, 1, 10);

        // Configure max history length on all checkers and trim existing histories
        await Task.WhenAll(_pulseCheckers.OfType<PulseChecker>().Select(async checker =>
        {
            checker.ConfiguredMaxHistoryLength = maxHistory;
            await checker.TrimHistoryAsync(stoppingToken).ConfigureAwait(false);
        })).ConfigureAwait(false);

        await Task.WhenAll(_pulseCheckers.Select(checker => ScheduleAsync(checker, stoppingToken))).ConfigureAwait(false);
    }

    private IPulseChecker GetCheckerOrThrow(string name)
    {
        return _pulseCheckers.FirstOrDefault(checker => checker.Name == name)
            ?? throw new ArgumentException($"Pulse checker with name '{name}' not found.", nameof(name));
    }

    private async Task ScheduleAsync(IPulseChecker checker, CancellationToken cancellationToken = default)
    {
        var state = await checker.GetStateAsync(cancellationToken).ConfigureAwait(false);

        if (!state.IsActive)
        {
            return;
        }

        await _pulseScheduler.ScheduleAsync(checker, state.Interval, cancellationToken).ConfigureAwait(false);
    }
}
