using Healthie.Abstractions;
using Healthie.Abstractions.Initialization;
using Healthie.Abstractions.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Healthie.DependencyInjection;

/// <summary>
/// Extension methods for registering Healthie.NET services with dependency injection.
/// </summary>
public static class StartupExtensions
{
    /// <summary>
    /// Registers all Healthie.NET core services and discovers pulse checker implementations
    /// from the specified assemblies.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="scanAssembliesForPulseCheckers">
    /// One or more assemblies to scan for classes that implement <see cref="IPulseChecker"/>
    /// by inheriting from <see cref="PulseChecker"/>.
    /// </param>
    /// <returns>The service collection for fluent chaining.</returns>
    public static IServiceCollection AddHealthie(
        this IServiceCollection services,
        params Assembly[] scanAssembliesForPulseCheckers)
    {
        return services.AddHealthie(null, scanAssembliesForPulseCheckers);
    }

    /// <summary>
    /// Registers all Healthie.NET core services with custom options and discovers pulse checker implementations
    /// from the specified assemblies.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configure">An optional action to configure <see cref="HealthieOptions"/>.</param>
    /// <param name="scanAssembliesForPulseCheckers">
    /// One or more assemblies to scan for classes that implement <see cref="IPulseChecker"/>
    /// by inheriting from <see cref="PulseChecker"/>.
    /// </param>
    /// <returns>The service collection for fluent chaining.</returns>
    public static IServiceCollection AddHealthie(
        this IServiceCollection services,
        Action<HealthieOptions>? configure,
        params Assembly[] scanAssembliesForPulseCheckers)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new HealthieOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.AddHostedService<StateProviderInitializationService>();

        services.AddSingleton<IPulsesScheduler, PulsesScheduler>();
        services.AddHostedService(sp =>
            (PulsesScheduler)sp.GetRequiredService<IPulsesScheduler>());

        foreach (var assembly in scanAssembliesForPulseCheckers)
        {
            services.RegisterCheckers(assembly, typeof(IPulseChecker), typeof(PulseChecker));
        }

        services.AddSingleton<IPulseScheduler, TimerPulseScheduler>();

        return services;
    }

    private static void RegisterCheckers(
        this IServiceCollection services,
        Assembly scanAssembly,
        Type checkerInterfaceType,
        Type checkerAbstractClassType)
    {
        var implementations = scanAssembly
            .GetTypes()
            .Where(type => !type.IsAbstract
                && !type.IsInterface
                && checkerInterfaceType.IsAssignableFrom(type)
                && type.IsSubclassOf(checkerAbstractClassType));

        foreach (var implementation in implementations)
        {
            services.AddSingleton(checkerInterfaceType, implementation);
        }
    }
}
