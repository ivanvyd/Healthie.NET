using Healthie.Abstractions.Scheduling;
using Healthie.Scheduling.Quartz.Converters;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Quartz.Impl;

namespace Healthie.Scheduling.Quartz;

public static class StartupExtensions
{
    public static IServiceCollection AddHealthieQuartz(this IServiceCollection services)
    {
        // Configure Quartz
        services.AddSingleton<ISchedulerFactory>(provider =>
        {
            // Create a scheduler factory with an in-memory job store
            var props = new System.Collections.Specialized.NameValueCollection
            {
                { "quartz.serializer.type", "binary" },
                { "quartz.scheduler.instanceName", "HealthieQuartzScheduler" },
                { "quartz.jobStore.type", "Quartz.Simpl.RAMJobStore, Quartz" },
                { "quartz.threadPool.threadCount", "3" }
            };

            return new StdSchedulerFactory(props);
        });

        // Register our implementations
        services.AddSingleton<ICronConverter, QuartzCronConverter>();
        services.AddSingleton<IPulseScheduler, QuartzPulseScheduler>();
        services.AddSingleton<IAsyncPulseScheduler, AsyncQuartzPulseScheduler>();

        return services;
    }
}
