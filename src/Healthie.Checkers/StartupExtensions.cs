using Healthie.Abstractions;
using Healthie.Abstractions.Scheduling;
using Healthie.Abstractions.StateProviding;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace Healthie.Checkers;

/// <summary>
/// Extension methods for registering the built-in pulse checkers.
/// </summary>
/// <remarks>
/// Registered one call at a time rather than found by assembly scanning, because each has to be
/// told what to watch. Every one takes a name: the same type is usually registered several times --
/// three endpoints, two drives -- and the name is what separates them in storage, in the API and on
/// the dashboard.
/// </remarks>
public static class StartupExtensions
{
    /// <summary>Watches an HTTP endpoint.</summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="name">The checker's name. Must be unique across all checkers.</param>
    /// <param name="url">The endpoint to request.</param>
    /// <param name="schedule">How often to check.</param>
    /// <param name="isAcceptable">Decides whether a status counts as healthy. Defaults to any 2xx.</param>
    /// <param name="unhealthyThreshold">Consecutive failures before the checker is unhealthy.</param>
    /// <remarks>
    /// Adds <c>IHttpClientFactory</c> if it is not already there. To configure the client Healthie
    /// uses -- its timeout, its handler, its retry policy -- name
    /// <see cref="HttpPulseChecker.HttpClientName"/> in your own <c>AddHttpClient</c> call.
    /// </remarks>
    public static IServiceCollection AddHealthieHttpChecker(
        this IServiceCollection services,
        string name,
        Uri url,
        PulseSchedule schedule,
        Func<HttpStatusCode, bool>? isAcceptable = null,
        uint unhealthyThreshold = 0)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient();

        return services.AddSingleton<IPulseChecker>(provider => new HttpPulseChecker(
            provider.GetRequiredService<IStateProvider>(),
            provider.GetRequiredService<IHttpClientFactory>(),
            name,
            url,
            schedule,
            isAcceptable,
            unhealthyThreshold: unhealthyThreshold));
    }

    /// <summary>Watches whether a TCP port accepts a connection.</summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="name">The checker's name. Must be unique across all checkers.</param>
    /// <param name="host">The host to connect to.</param>
    /// <param name="port">The port to connect to.</param>
    /// <param name="schedule">How often to check.</param>
    /// <param name="timeout">How long to wait for the connection. Defaults to five seconds.</param>
    /// <param name="unhealthyThreshold">Consecutive failures before the checker is unhealthy.</param>
    public static IServiceCollection AddHealthieTcpChecker(
        this IServiceCollection services,
        string name,
        string host,
        int port,
        PulseSchedule schedule,
        TimeSpan? timeout = null,
        uint unhealthyThreshold = 0)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddSingleton<IPulseChecker>(provider => new TcpPortPulseChecker(
            provider.GetRequiredService<IStateProvider>(), name, host, port, schedule, timeout, unhealthyThreshold));
    }

    /// <summary>Watches how long a TLS certificate has left.</summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="name">The checker's name. Must be unique across all checkers.</param>
    /// <param name="host">The host whose certificate to read.</param>
    /// <param name="schedule">How often to check. Daily is usually right.</param>
    /// <param name="warnWithin">How long before expiry to report suspicious. Defaults to 30 days.</param>
    /// <param name="port">The TLS port. Defaults to 443.</param>
    public static IServiceCollection AddHealthieCertificateChecker(
        this IServiceCollection services,
        string name,
        string host,
        PulseSchedule schedule,
        TimeSpan? warnWithin = null,
        int port = 443)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddSingleton<IPulseChecker>(provider => new CertificateExpiryPulseChecker(
            provider.GetRequiredService<IStateProvider>(), name, host, schedule, warnWithin, port));
    }

    /// <summary>Watches whether a hostname resolves.</summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="name">The checker's name. Must be unique across all checkers.</param>
    /// <param name="hostname">The hostname to resolve.</param>
    /// <param name="schedule">How often to check.</param>
    /// <param name="unhealthyThreshold">Consecutive failures before the checker is unhealthy.</param>
    public static IServiceCollection AddHealthieDnsChecker(
        this IServiceCollection services,
        string name,
        string hostname,
        PulseSchedule schedule,
        uint unhealthyThreshold = 0)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddSingleton<IPulseChecker>(provider => new DnsPulseChecker(
            provider.GetRequiredService<IStateProvider>(), name, hostname, schedule, unhealthyThreshold));
    }

    /// <summary>Watches how much free space a drive has left.</summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="name">The checker's name. Must be unique across all checkers.</param>
    /// <param name="driveName">The drive to inspect, as <see cref="System.IO.DriveInfo.Name"/> gives it.</param>
    /// <param name="schedule">How often to check.</param>
    /// <param name="warnBelowBytes">Free space below which the checker is suspicious. Defaults to 10 GiB.</param>
    /// <param name="criticalBelowBytes">Free space below which the checker is unhealthy. Defaults to 2 GiB.</param>
    public static IServiceCollection AddHealthieDiskSpaceChecker(
        this IServiceCollection services,
        string name,
        string driveName,
        PulseSchedule schedule,
        long warnBelowBytes = 10L * 1024 * 1024 * 1024,
        long criticalBelowBytes = 2L * 1024 * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddSingleton<IPulseChecker>(provider => new DiskSpacePulseChecker(
            provider.GetRequiredService<IStateProvider>(), name, driveName, schedule, warnBelowBytes, criticalBelowBytes));
    }
}
