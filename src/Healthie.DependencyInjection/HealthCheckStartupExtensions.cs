using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.StateProviding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Healthie.DependencyInjection;

/// <summary>
/// Extension methods for monitoring <see cref="IHealthCheck"/> implementations as pulse checkers.
/// </summary>
public static class HealthCheckStartupExtensions
{
    /// <summary>
    /// Monitors every health check registered so far with <c>AddHealthChecks()</c> as a pulse
    /// checker, so existing health checks gain scheduling, a failure threshold, history, and the
    /// dashboard without being rewritten.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="interval">The interval at which the health checks run.</param>
    /// <param name="unhealthyThreshold">
    /// The number of consecutive failures tolerated before a checker is considered unhealthy.
    /// </param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <remarks>
    /// Call this after the <c>AddHealthChecks()</c> calls that register the health checks: it reads
    /// the registrations present at the time it is called, and cannot see ones added afterwards.
    /// Each health check keeps its registered name, which is what identifies the resulting checker
    /// in storage, in the API, and on the dashboard.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <c>null</c>.</exception>
    public static IServiceCollection AddHealthieForHealthChecks(
        this IServiceCollection services,
        PulseInterval interval = PulseInterval.EveryMinute,
        uint unhealthyThreshold = 0)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var registration in ReadHealthCheckRegistrations(services))
        {
            services.AddHealthieHealthCheck(registration, interval, unhealthyThreshold);
        }

        return services;
    }

    /// <summary>
    /// Monitors a single <see cref="IHealthCheck"/> as a pulse checker.
    /// </summary>
    /// <typeparam name="THealthCheck">The health check to monitor.</typeparam>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="name">The name identifying the checker in storage, the API, and the dashboard.</param>
    /// <param name="interval">The interval at which the health check runs.</param>
    /// <param name="unhealthyThreshold">
    /// The number of consecutive failures tolerated before the checker is considered unhealthy.
    /// </param>
    /// <param name="failureStatus">
    /// The status reported when the health check fails. Defaults to
    /// <see cref="HealthStatus.Unhealthy"/>; use <see cref="HealthStatus.Degraded"/> for a check
    /// whose failure should read as suspicious rather than an outage.
    /// </param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is empty or whitespace.</exception>
    public static IServiceCollection AddHealthieHealthCheck<THealthCheck>(
        this IServiceCollection services,
        string name,
        PulseInterval interval = PulseInterval.EveryMinute,
        uint unhealthyThreshold = 0,
        HealthStatus failureStatus = HealthStatus.Unhealthy)
        where THealthCheck : class, IHealthCheck
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        services.TryAddSingleton<THealthCheck>();

        var registration = new HealthCheckRegistration(
            name,
            serviceProvider => serviceProvider.GetRequiredService<THealthCheck>(),
            failureStatus,
            tags: null);

        return services.AddHealthieHealthCheck(registration, interval, unhealthyThreshold);
    }

    /// <summary>
    /// Monitors the health check described by a registration as a pulse checker.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="registration">The registration describing the health check.</param>
    /// <param name="interval">The interval at which the health check runs.</param>
    /// <param name="unhealthyThreshold">
    /// The number of consecutive failures tolerated before the checker is considered unhealthy.
    /// </param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="registration"/> is <c>null</c>.
    /// </exception>
    public static IServiceCollection AddHealthieHealthCheck(
        this IServiceCollection services,
        HealthCheckRegistration registration,
        PulseInterval interval = PulseInterval.EveryMinute,
        uint unhealthyThreshold = 0)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registration);

        services.AddSingleton<IPulseChecker>(serviceProvider => new HealthCheckPulseChecker(
            serviceProvider.GetRequiredService<IStateProvider>(),
            registration.Factory(serviceProvider),
            registration,
            interval,
            unhealthyThreshold));

        return services;
    }

    /// <summary>
    /// Reads the health check registrations added to the service collection so far.
    /// </summary>
    /// <remarks>
    /// <c>AddHealthChecks()</c> records each registration by configuring
    /// <see cref="HealthCheckServiceOptions"/>, and those options are only assembled once the
    /// container is built. Replaying the configuration here reads them without building a container,
    /// which the dependency injection guidance rules out at registration time.
    /// </remarks>
    private static IReadOnlyList<HealthCheckRegistration> ReadHealthCheckRegistrations(IServiceCollection services)
    {
        var options = new HealthCheckServiceOptions();

        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(IConfigureOptions<HealthCheckServiceOptions>) &&
                descriptor.ImplementationInstance is IConfigureOptions<HealthCheckServiceOptions> configure)
            {
                configure.Configure(options);
            }
        }

        return [.. options.Registrations];
    }
}
