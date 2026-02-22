using Healthie.Abstractions.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Healthie.Scheduling.Quartz;

/// <summary>
/// Extension methods for registering Healthie.NET Quartz scheduling services with dependency injection.
/// </summary>
public static class StartupExtensions
{
    /// <summary>
    /// Registers <see cref="QuartzPulseScheduler"/> as the <see cref="IPulseScheduler"/>
    /// implementation and configures Quartz.NET as the scheduling backend.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configureQuartz">
    /// An optional action to further configure the Quartz service collection (e.g., add persistent job stores,
    /// clustering, or custom thread pool settings). When <c>null</c>, Quartz is registered with default settings.
    /// </param>
    /// <returns>The <see cref="IServiceCollection"/> for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method registers Quartz via <c>AddQuartz</c>
    /// and the Quartz hosted service via <c>AddQuartzHostedService</c>, then registers the
    /// <see cref="QuartzPulseScheduler"/> as a singleton <see cref="IPulseScheduler"/>.
    /// </para>
    /// <para>
    /// Use this instead of <c>AddHealthieDefaultScheduler</c> when you need persistent job storage,
    /// CRON-based scheduling, or Quartz clustering.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddHealthie(typeof(Program).Assembly)
    ///         .AddHealthieQuartz();
    /// </code>
    /// </example>
    public static IServiceCollection AddHealthieQuartz(
        this IServiceCollection services,
        Action<IServiceCollectionQuartzConfigurator>? configureQuartz = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddQuartz(configureQuartz ?? (_ => { }));
        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
        });

        services.AddSingleton<IPulseScheduler, QuartzPulseScheduler>();

        return services;
    }
}
