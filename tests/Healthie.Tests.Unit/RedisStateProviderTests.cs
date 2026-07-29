using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;
using Healthie.StateProviding.Redis;
using StackExchange.Redis;
using Testcontainers.Redis;

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
public sealed class RedisStateProviderTests : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private RedisContainer? _redis;
    private IConnectionMultiplexer? _connection;
    private string? _unavailable;

    private RedisStateProvider Provider(string prefix = "test:") =>
        new(_connection!, prefix);

    public async ValueTask InitializeAsync()
    {
        try
        {
            _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();
            await _redis.StartAsync(Ct);
            _connection = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
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

        if (_redis is not null)
        {
            await _redis.DisposeAsync();
        }
    }

    private bool Unavailable()
    {
        Assert.SkipWhen(_unavailable is not null, $"Redis is not reachable. {_unavailable}");
        return false;
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
