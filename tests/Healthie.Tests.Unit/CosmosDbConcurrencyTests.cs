using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;
using Healthie.StateProviding.CosmosDb;
using Microsoft.Azure.Cosmos;
using System.Net;
using System.Reflection;

namespace Healthie.Tests.Unit;

/// <summary>
/// The CosmosDB provider's conditional write, driven through a container that models the ETag rules
/// the service applies: every write mints a new ETag, an <c>If-Match</c> against a stale one is
/// refused with <c>412</c>, and a second create for the same id is refused with <c>409</c>.
/// </summary>
/// <remarks>
/// A fake rather than a real container, because the guarantee under test is what the provider asks
/// for -- whether it sends the ETag it read, and whether it reads a refusal as a refusal rather
/// than letting it escape as an exception. That is decided entirely by the request the provider
/// builds, so a container that applies the documented rules answers it. It does not prove the
/// service behaves as documented; only a run against real CosmosDB does that, and none is wired up.
/// </remarks>
public class CosmosDbConcurrencyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>One stored document and the ETag it currently carries.</summary>
    private sealed record Stored(object Document, string ETag);

    private sealed class FakeResponse<T>(T resource, string etag) : ItemResponse<T>
    {
        public override T Resource => resource;

        public override string ETag => etag;

        public override HttpStatusCode StatusCode => HttpStatusCode.OK;

        public override Headers Headers => new();

        public override double RequestCharge => 0;

        public override string ActivityId => string.Empty;

        public override CosmosDiagnostics Diagnostics => null!;
    }

    /// <summary>
    /// The parts of a container this provider uses, with CosmosDB's concurrency rules and nothing
    /// else.
    /// </summary>
    private sealed class FakeContainer : StubContainer
    {
        private readonly Dictionary<string, Stored> _items = new(StringComparer.Ordinal);
        private int _etags;

        /// <summary>How many writes were sent without a condition attached.</summary>
        public int UnconditionalWrites { get; private set; }

        private string NextETag() => $"\"etag-{++_etags}\"";

        /// <summary>
        /// The document's <c>id</c>, which is what CosmosDB keys on.
        /// </summary>
        /// <remarks>
        /// Read by reflection because the document type is internal to the provider. Case-insensitive
        /// because the property is spelled <c>id</c>, in lower case, as the CosmosDB SDK requires.
        /// </remarks>
        private static string IdOf(object document) =>
            document.GetType()
                .GetProperty("id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                ?.GetValue(document) as string
                ?? throw new InvalidOperationException($"{document.GetType().Name} has no id to store it under.");

        public override Task<ItemResponse<T>> ReadItemAsync<T>(
            string id,
            PartitionKey partitionKey,
            ItemRequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            if (!_items.TryGetValue(id, out var stored))
            {
                throw new CosmosException("Not found", HttpStatusCode.NotFound, 0, string.Empty, 0);
            }

            return Task.FromResult<ItemResponse<T>>(new FakeResponse<T>((T)stored.Document, stored.ETag));
        }

        public override Task<ItemResponse<T>> UpsertItemAsync<T>(
            T item,
            PartitionKey? partitionKey = null,
            ItemRequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            var id = IdOf(item!);
            var condition = requestOptions?.IfMatchEtag;

            if (condition is null)
            {
                UnconditionalWrites++;
            }
            else if (!_items.TryGetValue(id, out var current) || current.ETag != condition)
            {
                // Exactly what the service answers when the document moved on: the write is refused.
                throw new CosmosException(
                    "Precondition failed", HttpStatusCode.PreconditionFailed, 0, string.Empty, 0);
            }

            var etag = NextETag();
            _items[id] = new Stored(item!, etag);

            return Task.FromResult<ItemResponse<T>>(new FakeResponse<T>(item, etag));
        }

        public override Task<ItemResponse<T>> CreateItemAsync<T>(
            T item,
            PartitionKey? partitionKey = null,
            ItemRequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            var id = IdOf(item!);

            if (_items.ContainsKey(id))
            {
                throw new CosmosException("Conflict", HttpStatusCode.Conflict, 0, string.Empty, 0);
            }

            var etag = NextETag();
            _items[id] = new Stored(item!, etag);

            return Task.FromResult<ItemResponse<T>>(new FakeResponse<T>(item, etag));
        }

    }

    private static CosmosDbStateProvider Provider(out FakeContainer container)
    {
        container = new FakeContainer();
        return new CosmosDbStateProvider(container);
    }

    [Fact]
    public void TheProvider_SaysItCanVersionAWrite()
    {
        Assert.True(Provider(out _).SupportsOptimisticConcurrency);
    }

    [Fact]
    public async Task AReadEntry_CarriesTheDocumentsETag()
    {
        var provider = Provider(out _);
        await provider.SetStateAsync("x", new PulseCheckerState(PulseInterval.EverySecond), Ct);

        var entry = await provider.GetStateEntryAsync<PulseCheckerState>("x", Ct);

        Assert.True(entry!.IsVersioned);
        Assert.Equal(PulseInterval.EverySecond, entry.Value.Interval);
    }

    [Fact]
    public async Task AWriteFromACurrentRead_Lands()
    {
        var provider = Provider(out _);
        await provider.SetStateAsync("x", new PulseCheckerState(), Ct);

        var entry = await provider.GetStateEntryAsync<PulseCheckerState>("x", Ct);
        entry!.Value.IsPinned = true;

        Assert.True(await provider.TrySetStateAsync("x", entry.Value, entry.Version, Ct));
        Assert.True((await provider.GetStateAsync<PulseCheckerState>("x", Ct))!.IsPinned);
    }

    /// <summary>
    /// The point of the whole feature: a 412 is a refusal to be reported, not an exception to
    /// escape. A checker turns a throw into a failed health check, which would report a healthy
    /// component as down for the sake of this library's own bookkeeping.
    /// </summary>
    [Fact]
    public async Task AWriteFromAStaleRead_IsRefusedRatherThanThrowing()
    {
        var provider = Provider(out _);
        await provider.SetStateAsync("x", new PulseCheckerState(PulseInterval.EverySecond), Ct);

        var stale = await provider.GetStateEntryAsync<PulseCheckerState>("x", Ct);

        // Somebody else writes, moving the ETag on.
        await provider.SetStateAsync("x", new PulseCheckerState(PulseInterval.Every5Minutes), Ct);

        Assert.False(await provider.TrySetStateAsync("x", stale!.Value, stale.Version, Ct));

        // And their write is still there.
        Assert.Equal(
            PulseInterval.Every5Minutes,
            (await provider.GetStateAsync<PulseCheckerState>("x", Ct))!.Interval);
    }

    [Fact]
    public async Task ACreateThatLosesToAnotherCreate_IsRefused()
    {
        var provider = Provider(out _);

        Assert.True(await provider.TrySetStateAsync(
            "x", new PulseCheckerState { Group = "first" }, IStateProvider.AbsentVersion, Ct));

        Assert.False(await provider.TrySetStateAsync(
            "x", new PulseCheckerState { Group = "second" }, IStateProvider.AbsentVersion, Ct));

        Assert.Equal("first", (await provider.GetStateAsync<PulseCheckerState>("x", Ct))!.Group);
    }

    /// <summary>
    /// A conditional write must actually carry its condition. A provider that dropped the ETag
    /// would pass every test above by writing unconditionally and never being refused.
    /// </summary>
    [Fact]
    public async Task AVersionedWrite_IsSentAsAConditionalRequest()
    {
        var provider = Provider(out var container);
        await provider.SetStateAsync("x", new PulseCheckerState(), Ct);

        var unconditionalSoFar = container.UnconditionalWrites;

        var entry = await provider.GetStateEntryAsync<PulseCheckerState>("x", Ct);
        await provider.TrySetStateAsync("x", entry!.Value, entry.Version, Ct);

        Assert.Equal(unconditionalSoFar, container.UnconditionalWrites);
    }

    [Fact]
    public async Task AnUnversionedWrite_IsStillUnconditional()
    {
        var provider = Provider(out var container);

        await provider.TrySetStateAsync("x", new PulseCheckerState(), expectedVersion: null, Ct);

        Assert.Equal(1, container.UnconditionalWrites);
    }
}
