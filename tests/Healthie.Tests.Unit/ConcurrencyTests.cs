using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;
using Healthie.DependencyInjection;
using Healthie.StateProviding.Relational;
using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace Healthie.Tests.Unit;

/// <summary>
/// The bug this closes was documented on CosmosDbStateProvider from the start: reading a state,
/// changing it and writing it back is three steps, so a check finishing in between wrote its result
/// over a setting change -- or had its result written over. A version on the write makes the loser
/// find out instead of guessing.
/// </summary>
public class OptimisticConcurrencyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A provider written against the original two-method interface.</summary>
    private sealed class UnversionedProvider : IStateProvider
    {
        private readonly Dictionary<string, object> _states = new(StringComparer.Ordinal);

        public Task<TState?> GetStateAsync<TState>(string name, CancellationToken cancellationToken = default)
            => Task.FromResult(_states.TryGetValue(name, out var s) ? (TState?)s : default);

        public Task SetStateAsync<TState>(string name, TState state, CancellationToken cancellationToken = default)
        {
            _states[name] = state!;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task AnUnversionedProvider_SaysSoRatherThanClaimingProtection()
    {
        IStateProvider provider = new UnversionedProvider();

        Assert.False(provider.SupportsOptimisticConcurrency);

        var entry = await provider.GetStateEntryAsync<PulseCheckerState>("absent", Ct);
        Assert.Null(entry);
    }

    /// <summary>
    /// The honest half: a provider that cannot honour a version refuses rather than writing
    /// unconditionally, because ignoring it would lose exactly the update it was passed to protect.
    /// </summary>
    [Fact]
    public async Task AnUnversionedProvider_RefusesAVersionedWrite()
    {
        IStateProvider provider = new UnversionedProvider();

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.TrySetStateAsync("x", new PulseCheckerState(), "some-version", Ct));

        Assert.Contains(nameof(UnversionedProvider), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnversionedProvider_StillAcceptsAnUnconditionalWrite()
    {
        IStateProvider provider = new UnversionedProvider();

        Assert.True(await provider.TrySetStateAsync("x", new PulseCheckerState(), expectedVersion: null, Ct));
    }

    [Fact]
    public async Task AVersionedProvider_ReturnsAVersionItCanWriteBackAgainst()
    {
        IStateProvider provider = new InMemoryStateProvider();
        await provider.SetStateAsync("x", new PulseCheckerState(PulseInterval.EverySecond), Ct);

        var entry = await provider.GetStateEntryAsync<PulseCheckerState>("x", Ct);

        Assert.True(provider.SupportsOptimisticConcurrency);
        Assert.True(entry!.IsVersioned);
        Assert.True(await provider.TrySetStateAsync("x", entry.Value, entry.Version, Ct));
    }

    /// <summary>
    /// The heart of it: a write made from a state that has since moved on is refused rather than
    /// silently overwriting whatever moved it.
    /// </summary>
    [Fact]
    public async Task AWriteFromAStaleRead_IsRefused()
    {
        IStateProvider provider = new InMemoryStateProvider();
        await provider.SetStateAsync("x", new PulseCheckerState(PulseInterval.EverySecond), Ct);

        var stale = await provider.GetStateEntryAsync<PulseCheckerState>("x", Ct);

        // Somebody else writes in between.
        await provider.SetStateAsync("x", new PulseCheckerState(PulseInterval.Every5Minutes), Ct);

        Assert.False(await provider.TrySetStateAsync("x", stale!.Value, stale.Version, Ct));

        // And the other writer's value is still there, unclobbered.
        var current = await provider.GetStateAsync<PulseCheckerState>("x", Ct);
        Assert.Equal(PulseInterval.Every5Minutes, current!.Interval);
    }

    [Fact]
    public async Task EveryWrite_MovesTheVersionOn()
    {
        IStateProvider provider = new InMemoryStateProvider();
        await provider.SetStateAsync("x", new PulseCheckerState(), Ct);
        var first = (await provider.GetStateEntryAsync<PulseCheckerState>("x", Ct))!.Version;

        await provider.SetStateAsync("x", new PulseCheckerState(PulseInterval.Every2Seconds), Ct);
        var second = (await provider.GetStateEntryAsync<PulseCheckerState>("x", Ct))!.Version;

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// The retry loop from the Azure SDK's conditional-request guidance, in one place rather than
    /// in every caller: read, reapply, write, and go round if somebody got in first.
    /// </summary>
    [Fact]
    public async Task UpdateStateAsync_ReappliesAgainstWhoeverWonAndDoesNotLoseTheirChange()
    {
        IStateProvider provider = new InMemoryStateProvider();
        await provider.SetStateAsync("x", new PulseCheckerState(), Ct);

        var interfered = false;

        var result = await provider.UpdateStateAsync<PulseCheckerState>(
            "x",
            state =>
            {
                // Interfere once, after the read but before the write, exactly as a check finishing
                // mid-edit would. The first attempt must lose, and the retry must build on the
                // interfering write rather than discard it.
                if (!interfered)
                {
                    interfered = true;
                    provider.SetStateAsync("x", new PulseCheckerState { Group = "written-by-someone-else" }, Ct)
                        .GetAwaiter().GetResult();
                }

                state.IsPinned = true;
            },
            () => new PulseCheckerState(),
            cancellationToken: Ct);

        Assert.True(result.IsPinned);
        Assert.Equal("written-by-someone-else", result.Group);
    }

    [Fact]
    public async Task UpdateStateAsync_GivesUpRatherThanSpinningForEver()
    {
        IStateProvider provider = new InMemoryStateProvider();
        await provider.SetStateAsync("x", new PulseCheckerState(), Ct);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.UpdateStateAsync<PulseCheckerState>(
                "x",
                state =>
                {
                    // Interferes on every attempt, so no write can ever land.
                    provider.SetStateAsync("x", new PulseCheckerState { Group = Guid.NewGuid().ToString() }, Ct)
                        .GetAwaiter().GetResult();
                    state.IsPinned = true;
                },
                () => new PulseCheckerState(),
                maxAttempts: 3,
                cancellationToken: Ct));

        Assert.Contains("3 attempts", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateStateAsync_CreatesTheStateWhenThereIsNone()
    {
        IStateProvider provider = new InMemoryStateProvider();

        var result = await provider.UpdateStateAsync(
            "brand-new",
            (PulseCheckerState state) => state.IsPinned = true,
            () => new PulseCheckerState(PulseInterval.Every3Seconds),
            cancellationToken: Ct);

        Assert.True(result.IsPinned);
        Assert.Equal(PulseInterval.Every3Seconds, result.Interval);
    }

    /// <summary>
    /// Against a provider that cannot version, the loop degrades to what the library did before --
    /// read, change, write. That is what an unversioned store can offer, not a silent downgrade.
    /// </summary>
    /// <summary>
    /// The create half. Until there is a row there is no version to compare, so the write would go
    /// through unconditionally -- and two writers both finding nothing would lose one of the two
    /// changes. <see cref="IStateProvider.AbsentVersion"/> is the "only if it is still missing"
    /// precondition HTTP spells <c>If-None-Match: *</c>.
    /// </summary>
    [Fact]
    public async Task ACreateThatLosesToAnotherCreate_IsRefused()
    {
        IStateProvider provider = new InMemoryStateProvider();

        Assert.True(await provider.TrySetStateAsync(
            "x", new PulseCheckerState { Group = "first" }, IStateProvider.AbsentVersion, Ct));

        Assert.False(await provider.TrySetStateAsync(
            "x", new PulseCheckerState { Group = "second" }, IStateProvider.AbsentVersion, Ct));

        Assert.Equal("first", (await provider.GetStateAsync<PulseCheckerState>("x", Ct))!.Group);
    }

    [Fact]
    public async Task UpdateStateAsync_StillWorksAgainstAnUnversionedProvider()
    {
        IStateProvider provider = new UnversionedProvider();

        var result = await provider.UpdateStateAsync(
            "x",
            (PulseCheckerState state) => state.IsPinned = true,
            () => new PulseCheckerState(),
            cancellationToken: Ct);

        Assert.True(result.IsPinned);
    }
}

/// <summary>
/// A setting change made from the dashboard while checks are running is the case the whole feature
/// exists for, so it is driven through a real checker rather than the provider alone.
/// </summary>
public class PulseCheckerConcurrencyTests
{
    /// <summary>
    /// A checker with a name of its own.
    /// </summary>
    /// <remarks>
    /// Takes only an <see cref="IStateProvider"/> and carries the name on a settable property,
    /// because <c>AddHealthie</c> scans this assembly and registers every non-abstract PulseChecker
    /// it finds. A constructor parameter the container cannot resolve breaks every other test that
    /// scans -- which is exactly what the first version of this did, for the second time.
    /// </remarks>
    private sealed class NamedTestChecker(IStateProvider states) : Healthie.Abstractions.PulseChecker(states)
    {
        public string CheckerName { get; init; } = "named-test-checker";

        public override string Name => CheckerName;

        public override Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PulseCheckerResult(PulseCheckerHealth.Healthy, "ok"));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASettingChange_SurvivesACheckWritingItsResultAtTheSameTime()
    {
        var states = new InMemoryStateProvider();
        using var checker = new AlwaysHealthyPulseChecker(states);

        await checker.SetUnhealthyThresholdAsync(3, Ct);

        // A check and an edit racing, repeatedly.
        for (var round = 0; round < 20; round++)
        {
            await Task.WhenAll(
                checker.TriggerAsync(Ct),
                checker.SetGroupAsync($"group-{round}", Ct));
        }

        var state = await checker.GetStateAsync(Ct);

        // Both survived: the edits landed, and the threshold set before them was never clobbered.
        Assert.Equal("group-19", state.Group);
        Assert.Equal(3u, state.UnhealthyThreshold);
        Assert.NotNull(state.LastResult);
    }

    /// <summary>
    /// Wraps a provider and lets one competing write slip in between a read and the write that
    /// follows it -- which is what a second replica, or the REST API on another instance, does.
    /// </summary>
    /// <remarks>
    /// A checker's semaphore already serialises its own check loop against its own setting changes,
    /// so a single instance cannot show this. Modelling the other writer explicitly makes the race
    /// deterministic instead of hoping two threads interleave the wrong way.
    /// </remarks>
    private sealed class InterferingProvider(IStateProvider inner, Action<IStateProvider> interfere) : IStateProvider
    {
        private bool _done;

        public bool SupportsOptimisticConcurrency => inner.SupportsOptimisticConcurrency;

        public Task<TState?> GetStateAsync<TState>(string name, CancellationToken cancellationToken = default)
            => inner.GetStateAsync<TState>(name, cancellationToken);

        public Task SetStateAsync<TState>(string name, TState state, CancellationToken cancellationToken = default)
            => inner.SetStateAsync(name, state, cancellationToken);

        public async Task<StateEntry<TState>?> GetStateEntryAsync<TState>(string name, CancellationToken cancellationToken = default)
        {
            var entry = await inner.GetStateEntryAsync<TState>(name, cancellationToken);

            if (!_done)
            {
                _done = true;
                interfere(inner);
            }

            return entry;
        }

        public Task<bool> TrySetStateAsync<TState>(string name, TState state, string? expectedVersion, CancellationToken cancellationToken = default)
            => inner.TrySetStateAsync(name, state, expectedVersion, cancellationToken);
    }

    /// <summary>
    /// The bug, exactly as CosmosDbStateProvider described it: another writer changes the state
    /// between this one's read and its write. Without a version the later write wins and the other
    /// change is gone; with one it is refused, reapplied, and both survive.
    /// </summary>
    [Fact]
    public async Task AnotherWriterBetweenTheReadAndTheWrite_DoesNotLoseItsChange()
    {
        var store = new InMemoryStateProvider();
        var provider = new InterferingProvider(
            store,
            inner => inner.SetStateAsync(
                "racy",
                new PulseCheckerState { UnhealthyThreshold = 7 },
                CancellationToken.None).GetAwaiter().GetResult());

        using var checker = new NamedTestChecker(provider) { CheckerName = "racy" };

        await checker.SetGroupAsync("set-by-the-dashboard", Ct);

        var state = await store.GetStateAsync<PulseCheckerState>("racy", Ct);

        Assert.Equal("set-by-the-dashboard", state!.Group);
        Assert.Equal(7u, state.UnhealthyThreshold);
    }

    [Fact]
    public async Task SettingSomethingToWhatItAlreadyIs_WritesNothing()
    {
        var states = new InMemoryStateProvider();
        using var checker = new AlwaysHealthyPulseChecker(states);

        await checker.SetPinnedAsync(true, Ct);
        var afterFirst = (await states.GetStateEntryAsync<PulseCheckerState>(checker.Name, Ct))!.Version;

        await checker.SetPinnedAsync(true, Ct);
        var afterSecond = (await states.GetStateEntryAsync<PulseCheckerState>(checker.Name, Ct))!.Version;

        Assert.Equal(afterFirst, afterSecond);
    }

    [Fact]
    public async Task StartAndStop_StillReportWhetherTheyChangedAnything()
    {
        using var checker = new AlwaysHealthyPulseChecker(new InMemoryStateProvider());

        Assert.False(await checker.StartAsync(Ct));   // already active
        Assert.True(await checker.StopAsync(Ct));
        Assert.False(await checker.StopAsync(Ct));    // already stopped
        Assert.True(await checker.StartAsync(Ct));
    }
}

/// <summary>
/// Driven against a real SQLite file, so the conditional UPDATE and the column migration are
/// executed rather than inspected. This is the provider PostgreSQL and SQL Server share.
/// </summary>
public sealed class RelationalConcurrencyTests : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Table = "healthie_pulse_state";

    private string _databasePath = string.Empty;
    private string _connectionString = string.Empty;

    private DbConnection Connect() => new SqliteConnection(_connectionString);

    private RelationalStateProvider Provider() => new(Connect, RelationalDialect.Sqlite, Table);

    public ValueTask InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"healthie-cc-{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_databasePath};Pooling=False";
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
        return ValueTask.CompletedTask;
    }

    private Task InitializeSchemaAsync() =>
        new RelationalStateProviderInitializer(Connect, RelationalDialect.Sqlite, Table).InitializeAsync(Ct);

    [Fact]
    public async Task AWriteFromAStaleRead_IsRefused()
    {
        await InitializeSchemaAsync();
        var provider = Provider();
        await provider.SetStateAsync("x", new PulseCheckerState(PulseInterval.EverySecond), Ct);

        var stale = await provider.GetStateEntryAsync<PulseCheckerState>("x", Ct);
        await provider.SetStateAsync("x", new PulseCheckerState(PulseInterval.Every5Minutes), Ct);

        Assert.False(await provider.TrySetStateAsync("x", stale!.Value, stale.Version, Ct));
        Assert.Equal(PulseInterval.Every5Minutes, (await provider.GetStateAsync<PulseCheckerState>("x", Ct))!.Interval);
    }

    [Fact]
    public async Task AWriteFromACurrentRead_Lands()
    {
        await InitializeSchemaAsync();
        var provider = Provider();
        await provider.SetStateAsync("x", new PulseCheckerState(), Ct);

        var entry = await provider.GetStateEntryAsync<PulseCheckerState>("x", Ct);
        entry!.Value.IsPinned = true;

        Assert.True(await provider.TrySetStateAsync("x", entry.Value, entry.Version, Ct));
        Assert.True((await provider.GetStateAsync<PulseCheckerState>("x", Ct))!.IsPinned);
    }

    [Fact]
    public async Task AConditionalWriteAgainstAMissingRow_IsRefusedRatherThanInserting()
    {
        await InitializeSchemaAsync();
        var provider = Provider();

        Assert.False(await provider.TrySetStateAsync("never-stored", new PulseCheckerState(), "made-up-version", Ct));
        Assert.Null(await provider.GetStateAsync<PulseCheckerState>("never-stored", Ct));
    }

    [Fact]
    public async Task ACreateThatLosesToAnotherCreate_IsRefused()
    {
        await InitializeSchemaAsync();
        var provider = Provider();

        Assert.True(await provider.TrySetStateAsync(
            "x", new PulseCheckerState { Group = "first" }, IStateProvider.AbsentVersion, Ct));

        Assert.False(await provider.TrySetStateAsync(
            "x", new PulseCheckerState { Group = "second" }, IStateProvider.AbsentVersion, Ct));

        Assert.Equal("first", (await provider.GetStateAsync<PulseCheckerState>("x", Ct))!.Group);
    }

    /// <summary>
    /// A table created before versioning existed has no version column. The initializer has to add
    /// it without losing the rows already there, and without an IF NOT EXISTS that SQLite lacks for
    /// ADD COLUMN.
    /// </summary>
    [Fact]
    public async Task ATablePredatingVersioning_IsMigratedWithoutLosingItsRows()
    {
        await using (var connection = Connect())
        {
            await connection.OpenAsync(Ct);
            await using var create = connection.CreateCommand();
            create.CommandText =
                $"CREATE TABLE {Table} (name TEXT NOT NULL PRIMARY KEY, state_type TEXT NULL, value TEXT NOT NULL);" +
                $"INSERT INTO {Table} (name, state_type, value) VALUES " +
                $"('legacy', 'Healthie.Abstractions.Models.PulseCheckerState', '{{\"Interval\":\"Every30Seconds\"}}');";
            await create.ExecuteNonQueryAsync(Ct);
        }

        await InitializeSchemaAsync();

        var provider = Provider();
        var entry = await provider.GetStateEntryAsync<PulseCheckerState>("legacy", Ct);

        Assert.Equal(PulseInterval.Every30Seconds, entry!.Value.Interval);

        // The pre-existing row has no version yet, and says so rather than inventing one.
        Assert.Null(entry.Version);
        Assert.False(entry.IsVersioned);
    }

    /// <summary>
    /// The upgrade path. Every row already in the table has no version until it is next written, and
    /// treating "stored but unversioned" as "not stored" refuses a write that can never succeed --
    /// so the very first setting change after an upgrade would fail, on every existing checker.
    /// </summary>
    [Fact]
    public async Task AnUnversionedRow_CanStillBeUpdated()
    {
        await using (var connection = Connect())
        {
            await connection.OpenAsync(Ct);
            await using var create = connection.CreateCommand();
            create.CommandText =
                $"CREATE TABLE {Table} (name TEXT NOT NULL PRIMARY KEY, state_type TEXT NULL, value TEXT NOT NULL);" +
                $"INSERT INTO {Table} (name, state_type, value) VALUES " +
                $"('legacy', 'Healthie.Abstractions.Models.PulseCheckerState', '{{\"Interval\":\"EveryMinute\"}}');";
            await create.ExecuteNonQueryAsync(Ct);
        }

        await InitializeSchemaAsync();

        var provider = Provider();

        var updated = await provider.UpdateStateAsync(
            "legacy",
            (PulseCheckerState state) => state.Group = "set-after-upgrading",
            () => new PulseCheckerState(),
            cancellationToken: Ct);

        // The change landed, on top of what was already stored rather than over it.
        Assert.Equal("set-after-upgrading", updated.Group);
        Assert.Equal(PulseInterval.EveryMinute, updated.Interval);

        // And the row is versioned from here on, so the next write is protected.
        Assert.True((await provider.GetStateEntryAsync<PulseCheckerState>("legacy", Ct))!.IsVersioned);
    }

    [Fact]
    public async Task RunningTheInitializerTwice_DoesNotAddTheColumnTwice()
    {
        await InitializeSchemaAsync();
        await InitializeSchemaAsync();

        var provider = Provider();
        await provider.SetStateAsync("x", new PulseCheckerState(), Ct);

        Assert.True((await provider.GetStateEntryAsync<PulseCheckerState>("x", Ct))!.IsVersioned);
    }

    [Fact]
    public async Task AMigratedRow_BecomesVersionedOnItsNextWrite()
    {
        await using (var connection = Connect())
        {
            await connection.OpenAsync(Ct);
            await using var create = connection.CreateCommand();
            create.CommandText =
                $"CREATE TABLE {Table} (name TEXT NOT NULL PRIMARY KEY, state_type TEXT NULL, value TEXT NOT NULL);" +
                $"INSERT INTO {Table} (name, state_type, value) VALUES " +
                $"('legacy', 'Healthie.Abstractions.Models.PulseCheckerState', '{{\"Interval\":\"EveryMinute\"}}');";
            await create.ExecuteNonQueryAsync(Ct);
        }

        await InitializeSchemaAsync();

        var provider = Provider();
        await provider.SetStateAsync("legacy", new PulseCheckerState(PulseInterval.Every2Minutes), Ct);

        Assert.True((await provider.GetStateEntryAsync<PulseCheckerState>("legacy", Ct))!.IsVersioned);
    }
}
