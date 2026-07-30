using Healthie.Abstractions.Insights;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Healthie.Uptime;

/// <summary>
/// Extension methods for registering uptime reporting with dependency injection.
/// </summary>
public static class StartupExtensions
{
    /// <summary>
    /// Records health changes so uptime can be reported over any window.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <remarks>
    /// Registers <see cref="InMemoryUptimeStore"/> with <c>TryAdd</c>, so a durable store
    /// registered before or after this call wins and registration stays order-independent -- the
    /// same rule the state providers follow.
    /// </remarks>
    public static IServiceCollection AddHealthieUptime(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IUptimeStore, InMemoryUptimeStore>();
        services.AddHostedService<UptimeRecorder>();

        // What the dashboard renders when this package is installed. Registered here rather than
        // referenced there, so installing a dashboard does not drag uptime in with it.
        services.TryAddSingleton<IUptimeInsights>(provider =>
            new UptimeInsights(provider.GetRequiredService<IUptimeStore>()));

        return services;
    }
}
