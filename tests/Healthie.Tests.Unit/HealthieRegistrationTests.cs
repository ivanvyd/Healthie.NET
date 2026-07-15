using Healthie.Abstractions;
using Healthie.Abstractions.Scheduling;
using Healthie.Abstractions.StateProviding;
using Healthie.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.Tests.Unit;

public class HealthieRegistrationTests
{
    private static readonly System.Reflection.Assembly TestAssembly = typeof(HealthieRegistrationTests).Assembly;

    [Fact]
    public void AddHealthie_WithoutAnyStateProvider_ResolvesCheckersUsingTheInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddHealthie(TestAssembly);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryStateProvider>(provider.GetRequiredService<IStateProvider>());
        Assert.NotEmpty(provider.GetServices<IPulseChecker>());
    }

    [Fact]
    public void AddHealthie_RegistersTheTimerSchedulerByDefault()
    {
        var services = new ServiceCollection();
        services.AddHealthie(TestAssembly);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<TimerPulseScheduler>(provider.GetRequiredService<IPulseScheduler>());
    }

    [Fact]
    public void AddHealthie_WhenTheSameAssemblyIsScannedTwice_RegistersEachCheckerOnce()
    {
        var services = new ServiceCollection();
        services.AddHealthie(TestAssembly, TestAssembly);

        using var provider = services.BuildServiceProvider();
        var checkers = provider.GetServices<IPulseChecker>().ToList();

        Assert.Equal(checkers.Count, checkers.Select(c => c.Name).Distinct().Count());

        // Checkers are keyed by name to serve the states endpoint; duplicates surfaced there
        // as a duplicate-key failure rather than at registration.
        var byName = checkers.ToDictionary(c => c.Name);
        Assert.NotEmpty(byName);
    }

    [Fact]
    public void AddHealthie_DiscoversEveryConcreteCheckerInTheScannedAssembly()
    {
        var services = new ServiceCollection();
        services.AddHealthie(TestAssembly);

        using var provider = services.BuildServiceProvider();
        var checkerTypes = provider.GetServices<IPulseChecker>().Select(c => c.GetType()).ToList();

        Assert.Contains(typeof(AlwaysHealthyPulseChecker), checkerTypes);
        Assert.Contains(typeof(AlwaysUnhealthyPulseChecker), checkerTypes);
    }

    [Fact]
    public void AddHealthie_WhenAStateProviderIsAlreadyRegistered_KeepsIt()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStateProvider, CustomStateProvider>();
        services.AddHealthie(TestAssembly);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<CustomStateProvider>(provider.GetRequiredService<IStateProvider>());
    }

    [Fact]
    public void AddHealthie_WhenAStateProviderIsRegisteredAfterwards_TheLaterOneWins()
    {
        var services = new ServiceCollection();
        services.AddHealthie(TestAssembly);
        services.AddSingleton<IStateProvider, CustomStateProvider>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<CustomStateProvider>(provider.GetRequiredService<IStateProvider>());
    }

    [Fact]
    public void AddHealthie_WhenASchedulerIsAlreadyRegistered_KeepsIt()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPulseScheduler, CustomPulseScheduler>();
        services.AddHealthie(TestAssembly);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<CustomPulseScheduler>(provider.GetRequiredService<IPulseScheduler>());
    }

    [Fact]
    public void AddHealthie_WhenASchedulerIsRegisteredAfterwards_TheLaterOneWins()
    {
        var services = new ServiceCollection();
        services.AddHealthie(TestAssembly);
        services.AddSingleton<IPulseScheduler, CustomPulseScheduler>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<CustomPulseScheduler>(provider.GetRequiredService<IPulseScheduler>());
    }

    [Fact]
    public void AddHealthie_AppliesConfiguredOptions()
    {
        var services = new ServiceCollection();
        services.AddHealthie(o => o.MaxHistoryLength = 5, TestAssembly);

        using var provider = services.BuildServiceProvider();

        Assert.Equal(5u, provider.GetRequiredService<HealthieOptions>().MaxHistoryLength);
    }

    // Checkers and the timer scheduler are singletons whose disposal is synchronous work. The
    // container refuses to dispose a service that is only asynchronously disposable, so a host
    // that builds a provider and disposes it synchronously would throw on shutdown.
    [Fact]
    public void BuildServiceProvider_WhenDisposedSynchronously_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddHealthie(TestAssembly);

        var provider = services.BuildServiceProvider();
        _ = provider.GetServices<IPulseChecker>().ToList();
        _ = provider.GetRequiredService<IPulseScheduler>();

        var exception = Record.Exception(provider.Dispose);

        Assert.Null(exception);
    }

    [Fact]
    public async Task BuildServiceProvider_WhenDisposedAsynchronously_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddHealthie(TestAssembly);

        var provider = services.BuildServiceProvider();
        _ = provider.GetServices<IPulseChecker>().ToList();
        _ = provider.GetRequiredService<IPulseScheduler>();

        await provider.DisposeAsync();
    }

    [Fact]
    public void AddHealthie_WhenCalledTwice_RegistersASingleSchedulerHostedService()
    {
        var services = new ServiceCollection();
        services.AddHealthie(TestAssembly);
        services.AddHealthie(TestAssembly);

        using var provider = services.BuildServiceProvider();
        var schedulers = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<PulsesScheduler>()
            .ToList();

        Assert.Single(schedulers);
    }
}
