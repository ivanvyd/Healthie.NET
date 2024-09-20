using Microsoft.Extensions.DependencyInjection;

namespace Healthie.Storage.MemoryCache;

public static class StartupExtensions
{
    public static IServiceCollection AddHealthieMemoryCache(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<IStateProvider, MemoryCacheStateProvider>();

        return services;
    }
}
