using Healthie.Abstractions;
using Healthie.Abstractions.Initialization;
using Healthie.Abstractions.Scheduling;
using Healthie.Abstractions.StateProviding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // Hosted services start in registration order, so state providers finish initializing
        // (creating a container, say) before the scheduler triggers the first check against them.
        // Registering the scheduler first would let checkers race that initialization, and a
        // checker that cannot reach its store records the failure as a failed health check.
        services.AddHostedService<StateProviderInitializationService>();

        services.TryAddSingleton<IPulsesScheduler, PulsesScheduler>();
        services.AddHostedService(sp =>
            (PulsesScheduler)sp.GetRequiredService<IPulsesScheduler>());

        foreach (var assembly in scanAssembliesForPulseCheckers)
        {
            services.RegisterCheckers(assembly, typeof(IPulseChecker), typeof(PulseChecker));
        }

        // Registered with TryAdd so a provider package wins regardless of the order in which
        // AddHealthie and AddHealthie{Quartz,CosmosDb,...} are called.
        services.TryAddSingleton<IPulseScheduler, TimerPulseScheduler>();
        services.TryAddSingleton<IStateProvider, InMemoryStateProvider>();

        return services;
    }

    private static void RegisterCheckers(
        this IServiceCollection services,
        Assembly scanAssembly,
        Type checkerInterfaceType,
        Type checkerAbstractClassType)
    {
        var implementations = GetLoadableTypes(scanAssembly)
            .Where(type => !type.IsAbstract
                && !type.IsInterface
                && checkerInterfaceType.IsAssignableFrom(type)
                && type.IsSubclassOf(checkerAbstractClassType));

        foreach (var implementation in implementations)
        {
            // TryAddEnumerable de-duplicates by implementation type, so scanning the same
            // assembly more than once cannot register a checker twice. Duplicates would
            // otherwise surface as a duplicate-key failure when checkers are keyed by name.
            services.TryAddEnumerable(ServiceDescriptor.Singleton(checkerInterfaceType, implementation));
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // A type failed to load (typically a missing transitive dependency). Register the
            // checkers that did load rather than failing host startup outright.
            return ex.Types.OfType<Type>();
        }
    }
}
