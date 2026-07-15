using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Healthie.DependencyInjection;

/// <summary>
/// Runs an <see cref="IHealthCheck"/> as a pulse checker, so health checks written for
/// <c>Microsoft.Extensions.Diagnostics.HealthChecks</c> -- including the wide range of community
/// ones -- can be scheduled, given a failure threshold, and kept in history like any other checker.
/// </summary>
/// <remarks>
/// <para>
/// The two health models line up as follows.
/// </para>
/// <list type="table">
///   <listheader><term><see cref="HealthStatus"/></term><description><see cref="PulseCheckerHealth"/></description></listheader>
///   <item><term><see cref="HealthStatus.Healthy"/></term><description><see cref="PulseCheckerHealth.Healthy"/></description></item>
///   <item><term><see cref="HealthStatus.Degraded"/></term><description><see cref="PulseCheckerHealth.Suspicious"/></description></item>
///   <item><term><see cref="HealthStatus.Unhealthy"/></term><description><see cref="PulseCheckerHealth.Unhealthy"/></description></item>
/// </list>
/// <para>
/// A health check reports on the state it is in right now and is given no memory of previous runs.
/// The unhealthy threshold is applied on top by the pulse checker, so a check that fails once does
/// not have to be treated as an outage.
/// </para>
/// <para>
/// The two middle states do not mean the same thing, which is worth knowing when moving checks
/// across. <see cref="HealthStatus.Degraded"/> is a severity -- working, but impaired --
/// whereas <see cref="PulseCheckerHealth.Suspicious"/> means a failure that has not yet been
/// confirmed by enough consecutive failures. A degraded result is therefore counted as a failure,
/// and with the default threshold of zero it is reported as
/// <see cref="PulseCheckerHealth.Unhealthy"/> on its first occurrence. Give the checker a threshold
/// of at least one to keep a degraded check reading as suspicious.
/// </para>
/// </remarks>
public sealed class HealthCheckPulseChecker : PulseChecker
{
    private readonly IHealthCheck _healthCheck;
    private readonly HealthCheckRegistration _registration;

    /// <summary>
    /// Initializes a new instance of the <see cref="HealthCheckPulseChecker"/> class.
    /// </summary>
    /// <param name="stateProvider">The state provider used to manage pulse checker state.</param>
    /// <param name="healthCheck">The health check to run.</param>
    /// <param name="registration">
    /// The registration describing the health check, supplying its name, tags, and the status it
    /// reports on failure.
    /// </param>
    /// <param name="interval">The interval at which the health check runs.</param>
    /// <param name="unhealthyThreshold">
    /// The number of consecutive failures tolerated before the checker is considered unhealthy.
    /// </param>
    public HealthCheckPulseChecker(
        IStateProvider stateProvider,
        IHealthCheck healthCheck,
        HealthCheckRegistration registration,
        PulseInterval interval = PulseInterval.EveryMinute,
        uint unhealthyThreshold = 0)
        : base(stateProvider, interval, unhealthyThreshold)
    {
        _healthCheck = healthCheck ?? throw new ArgumentNullException(nameof(healthCheck));
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
    }

    /// <inheritdoc />
    public override string Name => _registration.Name;

    /// <inheritdoc />
    public override string DisplayName => _registration.Name;

    /// <inheritdoc />
    public override async Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var context = new HealthCheckContext { Registration = _registration };

        var result = await _healthCheck.CheckHealthAsync(context, cancellationToken).ConfigureAwait(false);

        return new PulseCheckerResult(ToPulseCheckerHealth(result.Status), DescribeResult(result));
    }

    private static PulseCheckerHealth ToPulseCheckerHealth(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => PulseCheckerHealth.Healthy,
        HealthStatus.Degraded => PulseCheckerHealth.Suspicious,
        HealthStatus.Unhealthy => PulseCheckerHealth.Unhealthy,
        _ => PulseCheckerHealth.Unhealthy,
    };

    /// <summary>
    /// Describes a health check result, preferring its own description and falling back to the
    /// exception it reported.
    /// </summary>
    private static string? DescribeResult(HealthCheckResult result)
        => string.IsNullOrWhiteSpace(result.Description)
            ? result.Exception?.Message
            : result.Description;
}
