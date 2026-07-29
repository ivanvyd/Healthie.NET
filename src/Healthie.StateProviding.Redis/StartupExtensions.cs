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
    /// A StackExchange.Redis configuration string, such as <c>localhost:6379</c>. Leave it out to use
    /// an <see cref="IConnectionMultiplexer"/> the application already registers -- for its own cache,
    /// or through <c>AddStackExchangeRedisCache</c>. Sharing that one is the better option where it
    /// exists: a second connection to the same server buys nothing.
    /// </param>
    /// <param name="keyPrefix">Prefixed to every key the provider owns.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// One method rather than one per case, because two overloads separated only by what a
    /// <see cref="string"/> means cannot be told apart at the call site. C# resolved
    /// <c>AddHealthieRedis("localhost:6379")</c> to the overload whose single string was the key
    /// prefix, so the connection string quietly became a prefix, no connection was registered, and
    /// the failure arrived later as a missing service -- from the very call the README documents.
    /// </para>
    /// <para>
    /// The connection is opened once and kept: an <see cref="IConnectionMultiplexer"/> is built to be
    /// shared for the life of the application and is expensive to create per call. It goes in with
    /// <c>TryAdd</c>, so an application that already has one keeps it.
    /// </para>
    /// <para>
    /// The provider is registered with <c>AddSingleton</c> rather than <c>TryAddSingleton</c> on
    /// purpose: the built-in in-memory provider registers with <c>TryAdd</c>, so whichever call comes
    /// first, this one wins.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddHealthieRedis(
        this IServiceCollection services,
        string? configuration = null,
        string keyPrefix = DefaultKeyPrefix)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);

        if (configuration is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

            services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(configuration));
        }

        return services.AddSingleton<IStateProvider>(provider =>
            new RedisStateProvider(provider.GetRequiredService<IConnectionMultiplexer>(), keyPrefix));
    }
}
