using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;
using Healthie.StateProviding.CosmosDb;
using Microsoft.Azure.Cosmos;

namespace Healthie.Tests.Unit;

/// <summary>
/// The CosmosDB provider against the real emulator, rather than against a fake that models what the
/// service is documented to do.
/// </summary>
/// <remarks>
/// <para>
/// What the fake could never check: that CosmosDB actually answers <c>412</c> to an <c>If-Match</c>
/// on a moved document and <c>409</c> to a second create, and that the ETag the read hands back is
/// the one a write will accept. Those are the service's behaviour, not the provider's, and the
/// provider is built entirely on them.
/// </para>
/// <para>
/// The emulator serves a self-signed certificate, so the client is pointed at the callback
/// Testcontainers supplies for it. Skipped rather than failed where there is no container runtime.
/// </para>
/// </remarks>
public sealed class CosmosDbRealEmulatorTests(CosmosDbFixture fixture)
    : IAsyncLifetime, IClassFixture<CosmosDbFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CosmosClient? _client;
    private Container? _container_;
    private string? _unavailable;

    public async ValueTask InitializeAsync()
    {
        if (fixture.Unavailable is not null)
        {
            _unavailable = fixture.Unavailable;
            return;
        }

        try
        {
            _client = new CosmosClient(
                fixture.ConnectionString,
                new CosmosClientOptions
                {
                    ConnectionMode = ConnectionMode.Gateway,
                    HttpClientFactory = () => new HttpClient(fixture.HttpMessageHandler),
                });

            var database = await _client.CreateDatabaseIfNotExistsAsync("healthie", cancellationToken: Ct);

            // The partition key must be /id, which is what the provider's own initializer enforces.
            var response = await database.Database.CreateContainerIfNotExistsAsync(
                new ContainerProperties("state", "/id"),
                cancellationToken: Ct);

            _container_ = response.Container;
        }
        catch (Exception ex)
        {
            _unavailable = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }

    private bool Unavailable()
    {
        Assert.SkipWhen(_unavailable is not null, $"The CosmosDB emulator is not reachable. {_unavailable}");
        return false;
    }

    private CosmosDbStateProvider Provider() => new(_container_!);

    /// <summary>
    /// A key unique to the running test, because the emulator is shared across the class.
    /// </summary>
    private static string Key(string name) => $"{TestContext.Current.TestMethod?.MethodName}-{name}";

    [Fact]
    public async Task StateSurvivesARoundTrip()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();
        await provider.SetStateAsync(Key("round-trip"), new PulseCheckerState(PulseInterval.Every30Seconds), Ct);

        Assert.Equal(
            PulseInterval.Every30Seconds,
            (await provider.GetStateAsync<PulseCheckerState>(Key("round-trip"), Ct))!.Interval);
    }

    [Fact]
    public async Task NothingStored_ReadsAsNull()
    {
        if (Unavailable())
        {
            return;
        }

        Assert.Null(await Provider().GetStateAsync<PulseCheckerState>(Key("never-written"), Ct));
    }

    /// <summary>The ETag a read hands back has to be one a write will accept.</summary>
    [Fact]
    public async Task AWriteFromACurrentRead_Lands()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();
        await provider.SetStateAsync(Key("current"), new PulseCheckerState(), Ct);

        var entry = await provider.GetStateEntryAsync<PulseCheckerState>(Key("current"), Ct);
        Assert.True(entry!.IsVersioned);

        entry.Value.IsPinned = true;

        Assert.True(await provider.TrySetStateAsync(Key("current"), entry.Value, entry.Version, Ct));
        Assert.True((await provider.GetStateAsync<PulseCheckerState>(Key("current"), Ct))!.IsPinned);
    }

    /// <summary>
    /// The 412 the whole feature rests on: an <c>If-Match</c> against a document that has moved on
    /// must be refused, and refused as a result rather than as an exception escaping the provider.
    /// </summary>
    [Fact]
    public async Task AWriteFromAStaleRead_IsRefused()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();
        await provider.SetStateAsync(Key("stale"), new PulseCheckerState(PulseInterval.EverySecond), Ct);

        var stale = await provider.GetStateEntryAsync<PulseCheckerState>(Key("stale"), Ct);

        await provider.SetStateAsync(Key("stale"), new PulseCheckerState(PulseInterval.Every5Minutes), Ct);

        Assert.False(await provider.TrySetStateAsync(Key("stale"), stale!.Value, stale.Version, Ct));
        Assert.Equal(
            PulseInterval.Every5Minutes,
            (await provider.GetStateAsync<PulseCheckerState>(Key("stale"), Ct))!.Interval);
    }

    /// <summary>The 409: a second create for the same id is refused, not an overwrite.</summary>
    [Fact]
    public async Task ACreateThatLosesToAnotherCreate_IsRefused()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();

        Assert.True(await provider.TrySetStateAsync(
            Key("created"), new PulseCheckerState { Group = "first" }, IStateProvider.AbsentVersion, Ct));

        Assert.False(await provider.TrySetStateAsync(
            Key("created"), new PulseCheckerState { Group = "second" }, IStateProvider.AbsentVersion, Ct));

        Assert.Equal("first", (await provider.GetStateAsync<PulseCheckerState>(Key("created"), Ct))!.Group);
    }

    [Fact]
    public async Task EveryWrite_MovesTheETagOn()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();
        await provider.SetStateAsync(Key("moving"), new PulseCheckerState(), Ct);
        var first = (await provider.GetStateEntryAsync<PulseCheckerState>(Key("moving"), Ct))!.Version;

        await provider.SetStateAsync(Key("moving"), new PulseCheckerState(PulseInterval.Every2Seconds), Ct);
        var second = (await provider.GetStateEntryAsync<PulseCheckerState>(Key("moving"), Ct))!.Version;

        Assert.NotEqual(first, second);
    }

    /// <summary>Many writers from one read: the service must let exactly one through.</summary>
    [Fact]
    public async Task WhenManyWritersRaceFromOneRead_ExactlyOneWins()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();
        await provider.SetStateAsync(Key("contended"), new PulseCheckerState(), Ct);

        var entry = await provider.GetStateEntryAsync<PulseCheckerState>(Key("contended"), Ct);

        const int Writers = 8;
        using var readyToRace = new Barrier(Writers);

        var attempts = await Task.WhenAll(Enumerable.Range(0, Writers).Select(i => Task.Run(
            async () =>
            {
                readyToRace.SignalAndWait(Ct);
                return await provider.TrySetStateAsync(
                    Key("contended"),
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
        await provider.SetStateAsync(Key("removable"), new PulseCheckerState(), Ct);

        Assert.True(await provider.DeleteStateAsync(Key("removable"), Ct));
        Assert.False(await provider.DeleteStateAsync(Key("removable"), Ct));
    }

    /// <summary>
    /// Reading a state as a type it was not written as must throw rather than hand back something
    /// that is not what was stored.
    /// </summary>
    [Fact]
    public async Task ReadingAStateAsTheWrongType_Throws()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();
        await provider.SetStateAsync(Key("typed"), new PulseCheckerState(), Ct);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetStateAsync<PulseCheckerResult>(Key("typed"), Ct));

        Assert.Contains(Key("typed"), ex.Message, StringComparison.Ordinal);
    }
}
