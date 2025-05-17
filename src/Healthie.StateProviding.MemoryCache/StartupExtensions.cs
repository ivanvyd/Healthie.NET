using Healthie.Abstractions.StateProviding;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.StateProviding.MemoryCache;

public static class StartupExtensions
{
    public static IServiceCollection AddHealthieMemoryCache(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<IStateProvider, MemoryCacheStateProvider>();
        services.AddSingleton<IAsyncStateProvider, MemoryCacheStateProvider>();

        return services;
    }
}
