using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;
using Healthie.StateProviding.Relational;
using Npgsql;
using System.Data.Common;

namespace Healthie.Tests.Unit;

/// <summary>
/// The relational provider against the engines it ships for, rather than against SQLite standing in
/// for them.
/// </summary>
/// <remarks>
/// <para>
/// The dialects differ exactly where concurrency lives: PostgreSQL resolves a create with
/// <c>ON CONFLICT DO NOTHING</c>, SQL Server with <c>UPDLOCK, HOLDLOCK</c> on the existence check,
/// and SQLite serialises writers with a file lock that hides the difference between a correct
/// statement and a lucky one. Everything below had only ever been argued for on those two engines.
/// </para>
/// <para>
/// Skipped rather than failed where there is no container runtime, so the suite still passes on a
/// machine without Docker.
/// </para>
/// </remarks>
public abstract class RealDatabaseProviderTests : IAsyncLifetime
{
    private const string Table = "healthie_pulse_state";

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string? _unavailable;
    private Func<DbConnection>? _connect;

    /// <summary>Why the engine is unusable, from the fixture that owns it.</summary>
    protected abstract string? FixtureUnavailable { get; }

    /// <summary>Opens a connection to the engine the fixture started.</summary>
    protected abstract DbConnection Connect();

    /// <summary>The SQL this engine speaks.</summary>
    protected abstract RelationalDialect Dialect { get; }

    /// <summary>
    /// Creates the table. Runs per test, against the container the fixture started once.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        if (FixtureUnavailable is not null)
        {
            _unavailable = FixtureUnavailable;
            return;
        }

        try
        {
            _connect = Connect;
            await new RelationalStateProviderInitializer(_connect, Dialect, Table).InitializeAsync(Ct);
        }
        catch (Exception ex)
        {
            _unavailable = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private bool Unavailable()
    {
        Assert.SkipWhen(_unavailable is not null, $"The database is not reachable. {_unavailable}");
        return false;
    }

    private RelationalStateProvider Provider() => new(_connect!, Dialect, Table);

    /// <summary>
    /// A key unique to the running test.
    /// </summary>
    /// <remarks>
    /// The container is shared across the class now, so two tests using the same checker name would
    /// see each other's writes. Scoping by test name keeps them apart without a container each.
    /// </remarks>
    private string Key(string name) => $"{GetType().Name}-{TestContext.Current.TestMethod?.MethodName}-{name}";

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

    /// <summary>
    /// The upsert has to insert and then replace, on an engine where two writers can arrive at once.
    /// </summary>
    [Fact]
    public async Task WritingTheSameCheckerTwice_Replaces()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();
        await provider.SetStateAsync(Key("twice"), new PulseCheckerState(PulseInterval.EverySecond), Ct);
        await provider.SetStateAsync(Key("twice"), new PulseCheckerState(PulseInterval.Every5Minutes), Ct);

        Assert.Equal(
            PulseInterval.Every5Minutes,
            (await provider.GetStateAsync<PulseCheckerState>(Key("twice"), Ct))!.Interval);
    }

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

    /// <summary>
    /// The statement this exists for: <c>ON CONFLICT DO NOTHING</c> on PostgreSQL, and an existence
    /// check under <c>UPDLOCK, HOLDLOCK</c> on SQL Server. Both are meant to resolve the race in one
    /// step; the portable form they replaced could let two writers both insert and hand one a
    /// primary key violation instead of a refusal.
    /// </summary>
    [Fact]
    public async Task WhenManyWritersRaceToCreate_ExactlyOneWinsAndNoneThrow()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();

        // Repeated over fresh keys, because one round is not enough to land in the window. The
        // portable check-then-insert this replaced survives a single round of eight comfortably --
        // measured, not assumed -- and only shows itself over many.
        const int Rounds = 40;
        const int Writers = 8;

        for (var round = 0; round < Rounds; round++)
        {
            var name = Key($"contended-create-{round}");
            using var readyToRace = new Barrier(Writers);

            var attempts = await Task.WhenAll(Enumerable.Range(0, Writers).Select(i => Task.Run(
                async () =>
                {
                    readyToRace.SignalAndWait(Ct);
                    return await provider.TrySetStateAsync(
                        name,
                        new PulseCheckerState { Group = $"writer-{i}" },
                        IStateProvider.AbsentVersion,
                        Ct);
                },
                Ct)));

            // A lost create must be reported as a refusal. The racy form hands the loser a primary
            // key violation instead, which surfaces here as the await throwing.
            Assert.Equal(1, attempts.Count(won => won));
            Assert.NotNull(await provider.GetStateAsync<PulseCheckerState>(name, Ct));
        }
    }

    /// <summary>The same race on the conditional update, which is the ordinary path once state exists.</summary>
    [Fact]
    public async Task WhenManyWritersRaceFromOneRead_ExactlyOneWins()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();
        await provider.SetStateAsync(Key("contended-update"), new PulseCheckerState(), Ct);

        var entry = await provider.GetStateEntryAsync<PulseCheckerState>(Key("contended-update"), Ct);

        const int Writers = 8;
        using var readyToRace = new Barrier(Writers);

        var attempts = await Task.WhenAll(Enumerable.Range(0, Writers).Select(i => Task.Run(
            async () =>
            {
                readyToRace.SignalAndWait(Ct);
                return await provider.TrySetStateAsync(
                    Key("contended-update"),
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

    [Fact]
    public async Task GetStatesAsync_ReadsManyInOneQuery()
    {
        if (Unavailable())
        {
            return;
        }

        var provider = Provider();
        await provider.SetStateAsync(Key("bulk-a"), new PulseCheckerState(PulseInterval.EverySecond), Ct);
        await provider.SetStateAsync(Key("bulk-b"), new PulseCheckerState(PulseInterval.Every5Minutes), Ct);

        var states = await provider.GetStatesAsync<PulseCheckerState>([Key("bulk-a"), Key("bulk-b"), Key("bulk-absent")], Ct);

        Assert.Equal(2, states.Count);
        Assert.Equal(PulseInterval.EverySecond, states[Key("bulk-a")].Interval);
        Assert.Equal(PulseInterval.Every5Minutes, states[Key("bulk-b")].Interval);
    }

    /// <summary>Running the initializer again must not disturb a table that is already correct.</summary>
    [Fact]
    public async Task RunningTheInitializerTwice_IsHarmless()
    {
        if (Unavailable())
        {
            return;
        }

        await new RelationalStateProviderInitializer(_connect!, Dialect, Table).InitializeAsync(Ct);

        var provider = Provider();
        await provider.SetStateAsync(Key("after-reinit"), new PulseCheckerState(), Ct);

        Assert.True((await provider.GetStateEntryAsync<PulseCheckerState>(Key("after-reinit"), Ct))!.IsVersioned);
    }
}

/// <summary>The provider against a real PostgreSQL.</summary>
public sealed class PostgresStateProviderTests(PostgresFixture fixture)
    : RealDatabaseProviderTests, IClassFixture<PostgresFixture>
{
    protected override RelationalDialect Dialect => RelationalDialect.PostgreSql;

    protected override string? FixtureUnavailable => fixture.Unavailable;

    protected override DbConnection Connect() => new NpgsqlConnection(fixture.ConnectionString);
}

/// <summary>The provider against a real SQL Server.</summary>
public sealed class SqlServerStateProviderTests(SqlServerFixture fixture)
    : RealDatabaseProviderTests, IClassFixture<SqlServerFixture>
{
    protected override RelationalDialect Dialect => RelationalDialect.SqlServer;

    protected override string? FixtureUnavailable => fixture.Unavailable;

    protected override DbConnection Connect() =>
        new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
}
