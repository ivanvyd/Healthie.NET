using Healthie.PulseChecking;
using Healthie.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Healthie.DependencyInjection;

public static class StartupExtensions
{
    public static IServiceCollection AddHealthie(this IServiceCollection services,
        Assembly[] scanAssembliesForPulseCheckers)
    {
        services.AddSingleton<IPulsesScheduler, PulsesScheduler>();
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<IPulsesScheduler>());
        services.AddSingleton<IAsyncPulsesScheduler, AsyncPulsesScheduler>();
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<IAsyncPulsesScheduler>());

        for (int i = 0; i < scanAssembliesForPulseCheckers.Length; i++)
        {
            Assembly scanAssembly = scanAssembliesForPulseCheckers[i];

            services.RegisterCheckers(scanAssembly, typeof(IPulseChecker), typeof(PulseChecker));
            services.RegisterCheckers(scanAssembly, typeof(IAsyncPulseChecker), typeof(AsyncPulseChecker));
        }

        return services;
    }

    private static void RegisterCheckers(this IServiceCollection services,
        Assembly scanAssembly,
        Type checkerInterfaceType,
        Type checkerAbstractClassType)
    {
        var interfaces = scanAssembly
            .GetTypes()
            .Where(type => !type.IsAbstract
                && !type.IsInterface
                && Array.Exists(type.GetInterfaces(), interfaceType => interfaceType == checkerInterfaceType)
                && checkerAbstractClassType.IsAssignableFrom(type))
            .Select(checkerImplementationType => new
            {
                CheckerInterfaceType = checkerImplementationType
                    .GetInterfaces()
                    .Single(interfaceType => interfaceType == checkerInterfaceType),
                CheckerImplementationType = checkerImplementationType,
            });

        foreach (var handler in interfaces)
        {
            services.AddSingleton(handler.CheckerInterfaceType, handler.CheckerImplementationType);
        }
    }
}
