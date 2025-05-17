using Healthie.Abstractions.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Quartz.Impl;

namespace Healthie.Scheduling.Quartz;

public static class StartupExtensions
{
    public static IServiceCollection AddHealthieQuartz(this IServiceCollection services)
    {
        // TODO: add providers
        services.AddSingleton<ISchedulerFactory>(provider =>
        {
            var props = new System.Collections.Specialized.NameValueCollection
            {
                { "quartz.serializer.type", "binary" },
                { "quartz.scheduler.instanceName", "HealthieQuartzScheduler" },
                { "quartz.jobStore.type", "Quartz.Simpl.RAMJobStore, Quartz" },
                { "quartz.threadPool.threadCount", "3" }
            };

            return new StdSchedulerFactory(props);
        });

        services.AddSingleton<IPulseScheduler, QuartzPulseScheduler>();
        services.AddSingleton<IAsyncPulseScheduler, AsyncQuartzPulseScheduler>();

        return services;
    }
}
