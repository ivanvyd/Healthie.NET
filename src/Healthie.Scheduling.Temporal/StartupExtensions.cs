using Healthie.Abstractions.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.Scheduling.Temporal;

/// <summary>
/// Extension methods for scheduling pulse checks with Temporal.
/// </summary>
public static class StartupExtensions
{
    /// <summary>
    /// Replaces the pulse scheduler with one backed by Temporal schedules.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configure">Optionally sets the task queue and schedule prefix.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// The Temporal client is not created here: its address, namespace, TLS and API key are the
    /// application's decisions, and a library that picked them would be picking which cluster your
    /// workflows run on. Register an <c>ITemporalClient</c> yourself, and a worker on the same task
    /// queue with <see cref="PulseCheckerWorkflow"/> and <see cref="PulseCheckerActivities"/>
    /// registered.
    /// </para>
    /// <para>
    /// Registered with <c>AddSingleton</c> rather than <c>TryAddSingleton</c> on purpose: the
    /// built-in timer registers with <c>TryAdd</c>, so whichever call comes first, this one wins and
    /// registration stays order-independent.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddHealthieTemporal(
        this IServiceCollection services,
        Action<HealthieTemporalOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new HealthieTemporalOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<PulseCheckerActivities>();
        services.AddSingleton<IPulseScheduler, TemporalPulseScheduler>();

        return services;
    }
}
