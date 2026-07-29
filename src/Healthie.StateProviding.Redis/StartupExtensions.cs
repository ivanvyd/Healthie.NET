using Healthie.Abstractions.StateProviding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Healthie.StateProviding.Redis;

/// <summary>
/// Extension methods for registering the Redis state provider with dependency injection.
/// </summary>
public static class StartupExtensions
{
    /// <summary>The key prefix used when none is given.</summary>
    public const string DefaultKeyPrefix = "healthie:state:";

    /// <summary>
    /// Registers a state provider backed by Redis.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configuration">
    /// A StackExchange.Redis configuration string, such as <c>localhost:6379</c>.
    /// </param>
    /// <param name="keyPrefix">Prefixed to every key the provider owns.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <remarks>
    /// Opens the connection here, once, and keeps it: an <see cref="IConnectionMultiplexer"/> is
    /// designed to be shared for the life of the application and is expensive to build per call.
    /// Registered as a singleton so the container disposes it on shutdown.
    /// </remarks>
    public static IServiceCollection AddHealthieRedis(
        this IServiceCollection services,
        string configuration,
        string keyPrefix = DefaultKeyPrefix)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

        services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(configuration));

        return services.AddHealthieRedis(keyPrefix);
    }

    /// <summary>
    /// Registers a state provider backed by a Redis connection the application already owns.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="keyPrefix">Prefixed to every key the provider owns.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// For an application that already registers an <see cref="IConnectionMultiplexer"/> -- for its
    /// own cache, or through <c>AddStackExchangeRedisCache</c>. Sharing it is the point: a second
    /// connection to the same server buys nothing.
    /// </para>
    /// <para>
    /// Registered with <c>AddSingleton</c> rather than <c>TryAddSingleton</c> on purpose: the
    /// built-in in-memory provider registers with <c>TryAdd</c>, so whichever call comes first, this
    /// one wins.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddHealthieRedis(
        this IServiceCollection services,
        string keyPrefix = DefaultKeyPrefix)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);

        return services.AddSingleton<IStateProvider>(provider =>
            new RedisStateProvider(provider.GetRequiredService<IConnectionMultiplexer>(), keyPrefix));
    }
}
