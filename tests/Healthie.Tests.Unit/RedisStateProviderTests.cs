using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;
using Healthie.DependencyInjection;
using Healthie.StateProviding.Redis;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Healthie.Tests.Unit;

/// <summary>
/// Driven against a real Redis in a container, because the guarantees under test are Redis's own:
/// that a Lua script runs to completion without interleaving, and that <c>HSET</c> and <c>EXISTS</c>
/// inside one see a consistent view. A fake would only re-state what the provider already believes.
/// </summary>
/// <remarks>
/// Skipped rather than failed when there is no container runtime, so the suite still passes on a
/// machine without Docker -- the same bargain the CosmosDB combinations strike in the E2E project.
/// </remarks>
public sealed class RedisStateProviderTests(RedisFixture fixture)
    : IAsyncLifetime, IClassFixture<RedisFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IConnectionMultiplexer? _connection;
    private string? _unavailable;

    /// <summary>
    /// A provider under a prefix unique to the running test, because the server is shared across
    /// the class now.
    /// </summary>
    private RedisStateProvider Provider(string? prefix = null) =>
        new(_connection!, prefix ?? $"{TestContext.Current.TestMethod?.MethodName}:");

    public async ValueTask InitializeAsync()
    {
        if (fixture.Unavailable is not null)
        {
            _unavailable = fixture.Unavailable;
            return;
        }

        try
        {
            _connection = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        }
        catch (Exception ex)
        {
            // No Docker, no daemon, no network for the image: not a failing provider.
            _unavailable = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }

    private bool Unavailable()
    {
        Assert.SkipWhen(_unavailable is not null, $"Redis is not reachable. {_unavailable}");
        return false;
    }

    /// <summary>
    /// Through <c>AddHealthieRedis</c>, not by constructing the provider. Nothing did that before,
    /// which is how a registration that never registered the connection shipped: the documented call
    /// bound to the overload whose one string was the key prefix.
    /// </summary>
    [Fact]
    public async Task AddHealthieRedis_WithAConfigurationString_ResolvesAWorkingProvider()
    {
        if (Unavailable())
        {
            return;
        }

        var services = new ServiceCollection();
        services.AddHealthie(typeof(RedisStateProviderTests).Assembly);
        services.AddHealthieRedis(fixture.ConnectionString);

        await using var host = services.BuildServiceProvider();

        var provider = host.GetRequiredService<IStateProvider>();

        Assert.IsType<RedisStateProvider>(provider);

        await provider.SetStateAsync("via-di", new PulseCheckerState(PulseInterval.Every30Seconds), Ct);
        Assert.Equal(
            PulseInterval.Every30Seconds,
            (await provider.GetStateAsync<PulseCheckerState>("via-di", Ct))!.Interval);
    }

    /// <summary>
    /// The other shape: the application owns the connection and Healthie shares it.
    /// </summary>
    [Fact]
    public async Task AddHealthieRedis_WithAnExistingConnection_SharesIt()
    {
        if (Unavailable())
        {
            return;
        }

        var services = new ServiceCollection();
        services.AddHealthie(typeof(RedisStateProviderTests).Assembly);
        services.AddSingleton(_connection!);
        services.AddHealthieRedis();

        await using var host = services.BuildServiceProvider();

        await host.GetRequiredService<IStateProvider>()
            .SetStateAsync("shared", new PulseCheckerState(), Ct);

        // Read back through the prefix the registration defaults to, over the same connection.
        Assert.NotNull(await Provider(Healthie.StateProviding.Redis.StartupExtensions.DefaultKeyPrefix)
            .GetStateAsync<PulseCheckerState>("shared", Ct));
    }

    /// <summary>
    /// A prefix is still a prefix when it is the only thing passed, and it must be named to be that.
    /// </summary>
    [Fact]
    public async Task AddHealthieRedis_WithOnlyAPrefix_UsesTheRegisteredConnection()
    {
        if (Unavailable())
        {
            return;
        }

        var services = new ServiceCollection();
        services.AddHealthie(typeof(RedisStateProviderTests).Assembly);
        services.AddSingleton(_connection!);
        services.AddHealthieRedis(keyPrefix: "named-prefix:");

        await using var host = services.BuildServiceProvider();

        await host.GetRequiredService<IStateProvider>()
            .SetStateAsync("k", new PulseCheckerState(), Ct);

        // Written under the prefix that was asked for, and nowhere else.
        Assert.NotNull(await Provider("named-prefix:").GetStateAsync<PulseCheckerState>("k", Ct));
        Assert.Null(await Provider().GetStateAsync<PulseCheckerState>("k", Ct));
    }

    [Fact]
    public async Task StateSurvivesARoundTrip()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();
        await provider.SetStateAsync("x", new PulseCheckerState(PulseInterval.Every30Seconds), Ct);

        var state = await provider.GetStateAsync<PulseCheckerState>("x", Ct);

        Assert.Equal(PulseInterval.Every30Seconds, state!.Interval);
    }

    [Fact]
    public async Task NothingStored_ReadsAsNull()
    {
        if (Unavailable())
        {
            return;
        }

        Assert.Null(await Provider().GetStateAsync<PulseCheckerState>("never-written", Ct));
        Assert.Null(await Provider().GetStateEntryAsync<PulseCheckerState>("never-written", Ct));
    }

    [Fact]
    public async Task TheProvider_SaysItCanVersionAWrite()
    {
        if (Unavailable())
        {
            return;
        }

        Assert.True(Provider().SupportsOptimisticConcurrency);
    }

    [Fact]
    public async Task AReadEntry_CarriesAVersion()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();
        await provider.SetStateAsync("x", new PulseCheckerState(), Ct);

        Assert.True((await provider.GetStateEntryAsync<PulseCheckerState>("x", Ct))!.IsVersioned);
    }

    [Fact]
    public async Task EveryWrite_MovesTheVersionOn()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();
        await provider.SetStateAsync("x", new PulseCheckerState(), Ct);
        var first = (await provider.GetStateEntryAsync<PulseCheckerState>("x", Ct))!.Version;

        await provider.SetStateAsync("x", new PulseCheckerState(PulseInterval.Every2Seconds), Ct);
        var second = (await provider.GetStateEntryAsync<PulseCheckerState>("x", Ct))!.Version;

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task AWriteFromACurrentRead_Lands()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();
        await provider.SetStateAsync("x", new PulseCheckerState(), Ct);

        var entry = await provider.GetStateEntryAsync<PulseCheckerState>("x", Ct);
        entry!.Value.IsPinned = true;

        Assert.True(await provider.TrySetStateAsync("x", entry.Value, entry.Version, Ct));
        Assert.True((await provider.GetStateAsync<PulseCheckerState>("x", Ct))!.IsPinned);
    }

    /// <summary>
    /// The point of the whole thing: a write made from a state that has since moved on is refused
    /// rather than overwriting whoever moved it.
    /// </summary>
    [Fact]
    public async Task AWriteFromAStaleRead_IsRefused()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();
        await provider.SetStateAsync("x", new PulseCheckerState(PulseInterval.EverySecond), Ct);

        var stale = await provider.GetStateEntryAsync<PulseCheckerState>("x", Ct);

        // Somebody else writes in between.
        await provider.SetStateAsync("x", new PulseCheckerState(PulseInterval.Every5Minutes), Ct);

        Assert.False(await provider.TrySetStateAsync("x", stale!.Value, stale.Version, Ct));

        // And their write is still there.
        Assert.Equal(
            PulseInterval.Every5Minutes,
            (await provider.GetStateAsync<PulseCheckerState>("x", Ct))!.Interval);
    }

    [Fact]
    public async Task AConditionalWriteAgainstAMissingKey_IsRefusedRatherThanCreating()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();

        Assert.False(await provider.TrySetStateAsync("gone", new PulseCheckerState(), "made-up-version", Ct));
        Assert.Null(await provider.GetStateAsync<PulseCheckerState>("gone", Ct));
    }

    [Fact]
    public async Task ACreateThatLosesToAnotherCreate_IsRefused()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();

        Assert.True(await provider.TrySetStateAsync(
            "x", new PulseCheckerState { Group = "first" }, IStateProvider.AbsentVersion, Ct));

        Assert.False(await provider.TrySetStateAsync(
            "x", new PulseCheckerState { Group = "second" }, IStateProvider.AbsentVersion, Ct));

        Assert.Equal("first", (await provider.GetStateAsync<PulseCheckerState>("x", Ct))!.Group);
    }

    /// <summary>
    /// Many writers, one checker, all reading the same version and racing to write it. Exactly one
    /// may win. This is the property a Lua script buys that a read-then-write cannot.
    /// </summary>
    [Fact]
    public async Task WhenManyWritersRaceFromOneRead_ExactlyOneWins()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();
        await provider.SetStateAsync("contended", new PulseCheckerState(), Ct);

        var entry = await provider.GetStateEntryAsync<PulseCheckerState>("contended", Ct);

        const int Writers = 12;
        using var readyToRace = new Barrier(Writers);

        var attempts = await Task.WhenAll(Enumerable.Range(0, Writers).Select(i => Task.Run(
            async () =>
            {
                readyToRace.SignalAndWait(Ct);
                return await provider.TrySetStateAsync(
                    "contended",
                    new PulseCheckerState { Group = $"writer-{i}" },
                    entry!.Version,
                    Ct);
            },
            Ct)));

        Assert.Equal(1, attempts.Count(won => won));
    }

    [Fact]
    public async Task DeleteStateAsync_SaysWhetherThereWasAnythingToRemove()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();
        await provider.SetStateAsync("x", new PulseCheckerState(), Ct);

        Assert.True(await provider.DeleteStateAsync("x", Ct));
        Assert.False(await provider.DeleteStateAsync("x", Ct));
        Assert.Null(await provider.GetStateAsync<PulseCheckerState>("x", Ct));
    }

    [Fact]
    public async Task GetStatesAsync_ReadsManyAndOmitsWhatIsNotThere()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();
        await provider.SetStateAsync("a", new PulseCheckerState(PulseInterval.EverySecond), Ct);
        await provider.SetStateAsync("b", new PulseCheckerState(PulseInterval.Every5Minutes), Ct);

        var states = await provider.GetStatesAsync<PulseCheckerState>(["a", "b", "absent"], Ct);

        Assert.Equal(2, states.Count);
        Assert.Equal(PulseInterval.EverySecond, states["a"].Interval);
        Assert.Equal(PulseInterval.Every5Minutes, states["b"].Interval);
        Assert.False(states.ContainsKey("absent"));
    }

    [Fact]
    public async Task GetStatesAsync_ForNoNames_AsksRedisNothing()
    {
        if (Unavailable())
        {
            return;
        }

        Assert.Empty(await Provider().GetStatesAsync<PulseCheckerState>([], Ct));
    }

    /// <summary>
    /// The prefix is what keeps this provider's keys out of the way of the application's own, so two
    /// prefixes over one server must not see each other.
    /// </summary>
    [Fact]
    public async Task TwoPrefixes_DoNotSeeEachOther()
    {
        if (Unavailable())
        {
            return;
        }

        await Provider("one:").SetStateAsync("x", new PulseCheckerState(PulseInterval.EverySecond), Ct);

        Assert.Null(await Provider("two:").GetStateAsync<PulseCheckerState>("x", Ct));
    }

    [Fact]
    public async Task ReadingAStateAsTheWrongType_Throws()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();
        await provider.SetStateAsync("typed", new PulseCheckerState(), Ct);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetStateAsync<PulseCheckerResult>("typed", Ct));

        Assert.Contains("typed", ex.Message, StringComparison.Ordinal);
    }
}
