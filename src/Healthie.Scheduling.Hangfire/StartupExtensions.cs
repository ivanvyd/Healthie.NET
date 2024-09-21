using Hangfire;
using Healthie.Scheduling.Hangfire.Converters;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.Scheduling.Hangfire;

public static class StartupExtensions
{
    public static IServiceCollection AddHealthieHangfire(this IServiceCollection services)
    {
        services.AddHangfire(configuration =>
        {
            configuration.UseInMemoryStorage(new()
            {
                MaxExpirationTime = TimeSpan.FromDays(1),
                MaxStateHistoryLength = 10,
            });
        });

        services.AddHangfireServer();

        services.AddSingleton<ICronConverter, CronConverter>();
        services.AddSingleton<IPulseScheduler, HangfirePulseScheduler>();
        services.AddSingleton<IAsyncPulseScheduler, AsyncHangfirePulseScheduler>();

        return services;
    }
}
