using Healthie.Abstractions.Scheduling;
using Healthie.Scheduling.Hangfire.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.Scheduling.Hangfire;

/// <summary>
/// Extension methods for registering the Hangfire pulse scheduler with dependency injection.
/// </summary>
public static class StartupExtensions
{
    /// <summary>
    /// Replaces the pulse scheduler with one backed by Hangfire recurring jobs.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// Hangfire itself is not configured here. Call <c>AddHangfire</c> and <c>AddHangfireServer</c>
    /// yourself: the storage, the retry policy and the dashboard are the application's decisions,
    /// and a library that picked them would be picking where your job data lives.
    /// </para>
    /// <para>
    /// Registered with <c>AddSingleton</c> rather than <c>TryAddSingleton</c> on purpose. The
    /// built-in timer registers with <c>TryAdd</c>, so whichever call comes first, this one wins and
    /// registration stays order-independent.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddHealthieHangfire(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Resolved by Hangfire's activator on each occurrence, so it has to be registered here
        // rather than constructed by the scheduler.
        services.AddScoped<PulseCheckerJob>();
        services.AddSingleton<IPulseScheduler, HangfirePulseScheduler>();

        return services;
    }
}
