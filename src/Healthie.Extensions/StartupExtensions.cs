using Healthie.PulseChecking;
using Healthie.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Healthie.Extensions;

public static class StartupExtensions
{
    public static IServiceCollection AddHealthie(this IServiceCollection services,
        Assembly[] scanAssembliesForPulseCheckers)
    {
        services.AddSingleton<IPulsesScheduler, PulsesScheduler>();
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<IPulsesScheduler>());

        for (int i = 0; i < scanAssembliesForPulseCheckers.Length; i++)
        {
            Assembly scanAssembly = scanAssembliesForPulseCheckers[i];

            RegisterCheckers(services, scanAssembly, typeof(IPulseChecker));
        }

        return services;
    }

    private static void RegisterCheckers(IServiceCollection services, Assembly scanAssembly, Type handlerType)
    {
        var interfaces = scanAssembly
            .GetTypes()
            .Where(type => !type.IsAbstract
                && !type.IsInterface
                && Array.Exists(type.GetInterfaces(), interfaceType => interfaceType == handlerType)
                && typeof(PulseChecker).IsAssignableFrom(type))
            .Select(checkerImplementationType => new
            {
                CheckerInterfaceType = checkerImplementationType
                    .GetInterfaces()
                    .Single(interfaceType => interfaceType == handlerType),
                CheckerImplementationType = checkerImplementationType,
            });

        foreach (var handler in interfaces)
        {
            services.AddSingleton(handler.CheckerInterfaceType, handler.CheckerImplementationType);
        }
    }
}
