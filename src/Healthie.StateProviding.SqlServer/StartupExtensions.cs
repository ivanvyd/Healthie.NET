using Healthie.Abstractions.StateProviding;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.StateProviding.SqlServer;

public static class StartupExtensions
{
    public static IServiceCollection AddHealthieSqlServer(
        this IServiceCollection services,
        string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentNullException(nameof(connectionString));
        }

        SqlServerStateProvider sqlServerStateProvider = new(connectionString);

        services.AddSingleton<IStateProvider>(sqlServerStateProvider);
        services.AddSingleton<IStateProviderInitializer>(sqlServerStateProvider);
        services.AddSingleton<IAsyncStateProvider>(sqlServerStateProvider);
        services.AddSingleton<IAsyncStateProviderInitializer>(sqlServerStateProvider);

        return services;
    }
}
