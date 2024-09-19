using Hangfire;
using Healthie.Hangfire.Converters;
using Healthie.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.Hangfire;

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

        // services.AddHangfireServer();

        services.AddSingleton<ICronConverter, CronConverter>();
        services.AddSingleton<IPulseScheduler, HangfirePulseScheduler>();

        return services;
    }
}
