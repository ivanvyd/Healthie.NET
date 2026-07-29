using Healthie.Abstractions.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Coravel;
using Coravel.Scheduling.Schedule.Interfaces;

namespace Healthie.Scheduling.Coravel;

/// <summary>
/// Extension methods for running pulse checks on Coravel's scheduler.
/// </summary>
public static class StartupExtensions
{
    /// <summary>The name Coravel uses to stop this package's tick overlapping itself.</summary>
    private const string TickName = "healthie-pulse-tick";

    /// <summary>
    /// Replaces the pulse scheduler with one that runs on Coravel.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <remarks>
    /// Adds Coravel's own scheduler if it is not already there. Call
    /// <see cref="UseHealthiePulseScheduler"/> on the built application to start the tick.
    /// <para>
    /// Registered with <c>AddSingleton</c> rather than <c>TryAddSingleton</c> on purpose: the
    /// built-in timer registers with <c>TryAdd</c>, so whichever call comes first, this one wins and
    /// registration stays order-independent.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddHealthieCoravel(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScheduler();
        services.AddSingleton<CoravelPulseScheduler>();
        services.AddSingleton<IPulseScheduler>(provider => provider.GetRequiredService<CoravelPulseScheduler>());

        return services;
    }

    /// <summary>
    /// Starts the Coravel job that runs due pulse checks.
    /// </summary>
    /// <param name="services">The built application's service provider.</param>
    /// <returns>The service provider for fluent chaining.</returns>
    /// <remarks>
    /// One job for every checker rather than one each, because Coravel has no way to remove a
    /// scheduled job and checkers are scheduled and unscheduled while the application runs. It ticks
    /// every second and asks the scheduler what is due; a checker asking for less often than that is
    /// simply not due on most ticks.
    /// <para>
    /// <c>PreventOverlapping</c> keeps a slow tick from being started again on top of itself.
    /// </para>
    /// </remarks>
    public static IServiceProvider UseHealthiePulseScheduler(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.UseScheduler(scheduler =>
        {
            scheduler
                .ScheduleAsync(() => services.GetRequiredService<CoravelPulseScheduler>().TickAsync())
                .EverySecond()
                .PreventOverlapping(TickName);
        });

        return services;
    }
}
