using Healthie.Abstractions.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Quartz.Impl;
using System.Collections.Specialized;

namespace Healthie.Scheduling.Quartz;

public static class StartupExtensions
{
    public static IServiceCollection AddHealthieQuartz(this IServiceCollection services)
    {
        services.AddSingleton<ISchedulerFactory>(provider =>
        {
            // TODO: with `RAMJobStore` (in memory), it cannot be used in a distributed env.
            // Consider using a persistent store and a clustering.
            // TODO: handle the proper store depending on state provider using `IJobStore`.
            NameValueCollection props = new()
            {
                [StdSchedulerFactory.PropertySchedulerInstanceName] = "HealthieQuartzScheduler",
                [StdSchedulerFactory.PropertyJobStoreType] = "Quartz.Simpl.RAMJobStore, Quartz",
                ["quartz.serializer.type"] = "binary",
                ["quartz.threadPool.threadCount"] = "3",
            };

            return new StdSchedulerFactory(props);
        });


        services.AddSingleton<IPulseScheduler, QuartzPulseScheduler>();
        services.AddSingleton<IAsyncPulseScheduler, AsyncQuartzPulseScheduler>();

        return services;
    }
}
