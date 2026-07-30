using Healthie.Abstractions.Scheduling;
using Healthie.Abstractions.Insights;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Healthie.LeaderElection;

/// <summary>
/// Extension methods for running the checks on one replica at a time.
/// </summary>
public static class StartupExtensions
{
    /// <summary>
    /// Runs pulse checks only on whichever replica currently holds the lease.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configure">Optionally sets the lease name, duration and renewal interval.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">No scheduler has been registered yet.</exception>
    /// <remarks>
    /// <para>
    /// Call this <em>after</em> the scheduler it should wrap -- <c>AddHealthie</c>, or one of the
    /// scheduling packages. It decorates whatever is registered at that point, so unlike every other
    /// <c>AddHealthie*</c> in this library it is not order-independent, and it says so rather than
    /// silently wrapping the built-in timer when you meant to wrap Quartz.
    /// </para>
    /// <para>
    /// Register a shared <see cref="ILeaseProvider"/> as well. The default keeps leases in memory,
    /// which makes every replica the leader of itself and leaves the problem exactly where it was.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddHealthieLeaderElection(
        this IServiceCollection services,
        Action<LeaderElectionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var inner = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IPulseScheduler))
            ?? throw new InvalidOperationException(
                "No IPulseScheduler is registered yet. Call AddHealthieLeaderElection after AddHealthie " +
                "or a scheduling package, because it wraps the scheduler that is registered at that point.");

        var options = new LeaderElectionOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<ILeaseProvider, InMemoryLeaseProvider>();

        services.AddSingleton(provider => new LeaderElectedPulseScheduler(
            Resolve(provider, inner),
            provider.GetService<Microsoft.Extensions.Logging.ILogger<LeaderElectedPulseScheduler>>()));

        services.AddSingleton<IPulseScheduler>(provider => provider.GetRequiredService<LeaderElectedPulseScheduler>());
        services.AddHostedService<LeaderElectionService>();

        // So a board served by a follower says so, instead of showing everything idle and looking
        // broken.
        services.TryAddSingleton<ILeadershipInsights>(provider => new LeadershipInsights(
            provider.GetRequiredService<LeaderElectedPulseScheduler>(),
            provider.GetRequiredService<LeaderElectionOptions>()));

        return services;
    }

    /// <summary>
    /// Builds the scheduler that was registered before this one, from its own descriptor.
    /// </summary>
    /// <remarks>
    /// Resolving <c>IPulseScheduler</c> from the container would find this decorator and recurse.
    /// The captured descriptor is the only way back to what it is decorating.
    /// </remarks>
    private static IPulseScheduler Resolve(IServiceProvider provider, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is IPulseScheduler instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is { } factory)
        {
            return (IPulseScheduler)factory(provider);
        }

        return (IPulseScheduler)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
    }
}
