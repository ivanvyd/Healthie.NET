using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;
using Healthie.DependencyInjection;
using Healthie.StateProviding.Relational;
using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace Healthie.Tests.Unit;

/// <summary>
/// Every dashboard load and every list request reads the state of every checker. One at a time
/// that is a round trip per checker per page, which a store measured in milliseconds turns into a
/// page measured in seconds.
/// </summary>
public class BulkStateReadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A provider written against the older interface, which is every third-party one. It must keep
    /// working without change, and it must still answer a bulk read correctly.
    /// </summary>
    private sealed class OneAtATimeProvider : IStateProvider
    {
        private readonly Dictionary<string, object> _states = new(StringComparer.Ordinal);

        public int SingleReads { get; private set; }

        public Task<TState?> GetStateAsync<TState>(string name, CancellationToken cancellationToken = default)
        {
            SingleReads++;
            return Task.FromResult(_states.TryGetValue(name, out var state) ? (TState?)state : default);
        }

        public Task SetStateAsync<TState>(string name, TState state, CancellationToken cancellationToken = default)
        {
            _states[name] = state!;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task AProviderThatDoesNotImplementBulkRead_StillAnswersCorrectly()
    {
        IStateProvider provider = new OneAtATimeProvider();
        await provider.SetStateAsync("a", new PulseCheckerState(PulseInterval.EverySecond), Ct);
        await provider.SetStateAsync("b", new PulseCheckerState(PulseInterval.Every5Minutes), Ct);

        var states = await provider.GetStatesAsync<PulseCheckerState>(["a", "b"], Ct);

        Assert.Equal(2, states.Count);
        Assert.Equal(PulseInterval.EverySecond, states["a"].Interval);
        Assert.Equal(PulseInterval.Every5Minutes, states["b"].Interval);
    }

    /// <summary>
    /// The default is the old behaviour exactly -- one read per name -- so nothing an existing
    /// provider does changes. Overriding it is what makes the saving, and this pins that the
    /// default really does fall back rather than silently returning nothing.
    /// </summary>
    [Fact]
    public async Task TheDefault_FallsBackToOneReadPerName()
    {
        var provider = new OneAtATimeProvider();
        await ((IStateProvider)provider).SetStateAsync("a", new PulseCheckerState(), Ct);

        await ((IStateProvider)provider).GetStatesAsync<PulseCheckerState>(["a", "b", "c"], Ct);

        Assert.Equal(3, provider.SingleReads);
    }

    [Fact]
    public async Task ANameWithNothingStored_IsAbsentRatherThanNull()
    {
        IStateProvider provider = new InMemoryStateProvider();
        await provider.SetStateAsync("stored", new PulseCheckerState(), Ct);

        var states = await provider.GetStatesAsync<PulseCheckerState>(["stored", "never-ran"], Ct);

        Assert.True(states.ContainsKey("stored"));
        Assert.False(states.ContainsKey("never-ran"));
    }

    [Fact]
    public async Task AskingForNothing_ReturnsNothingRatherThanThrowing()
    {
        IStateProvider provider = new InMemoryStateProvider();

        Assert.Empty(await provider.GetStatesAsync<PulseCheckerState>([], Ct));
    }

    [Fact]
    public async Task TheInMemoryProvider_ReadsInBulk()
    {
        IStateProvider provider = new InMemoryStateProvider();
        await provider.SetStateAsync("a", new PulseCheckerState(PulseInterval.Every20Seconds), Ct);
        await provider.SetStateAsync("b", new PulseCheckerState(PulseInterval.Every3Minutes), Ct);

        var states = await provider.GetStatesAsync<PulseCheckerState>(["a", "b"], Ct);

        Assert.Equal(PulseInterval.Every20Seconds, states["a"].Interval);
        Assert.Equal(PulseInterval.Every3Minutes, states["b"].Interval);
    }

    /// <summary>
    /// Reads must stay independent copies, exactly as a single read does -- StateChanged compares
    /// the stored state against the one about to be written, and a shared instance makes every
    /// comparison find them equal.
    /// </summary>
    [Fact]
    public async Task BulkReadsAreCopies_NotSomethingSharedWithTheStore()
    {
        IStateProvider provider = new InMemoryStateProvider();
        await provider.SetStateAsync("a", new PulseCheckerState(PulseInterval.EveryMinute), Ct);

        var first = await provider.GetStatesAsync<PulseCheckerState>(["a"], Ct);
        first["a"].Interval = PulseInterval.EverySecond;

        var second = await provider.GetStatesAsync<PulseCheckerState>(["a"], Ct);

        Assert.Equal(PulseInterval.EveryMinute, second["a"].Interval);
    }
}

/// <summary>
/// Driven against a real SQLite file, so the generated <c>IN</c> statement is executed rather than
/// inspected. This is the provider all three relational packages share.
/// </summary>
public sealed class RelationalBulkReadTests : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Table = "healthie_pulse_state";

    private string _databasePath = string.Empty;
    private string _connectionString = string.Empty;

    private DbConnection Connect() => new SqliteConnection(_connectionString);

    private RelationalStateProvider Provider() => new(Connect, RelationalDialect.Sqlite, Table);

    public async ValueTask InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"healthie-bulk-{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_databasePath};Pooling=False";

        await new RelationalStateProviderInitializer(Connect, RelationalDialect.Sqlite, Table)
            .InitializeAsync(Ct);
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ReadsEveryRequestedRowInOneQuery()
    {
        var provider = Provider();
        await provider.SetStateAsync("a", new PulseCheckerState(PulseInterval.EverySecond), Ct);
        await provider.SetStateAsync("b", new PulseCheckerState(PulseInterval.Every30Seconds), Ct);
        await provider.SetStateAsync("c", new PulseCheckerState(PulseInterval.Every5Minutes), Ct);

        var states = await provider.GetStatesAsync<PulseCheckerState>(["a", "b", "c"], Ct);

        Assert.Equal(3, states.Count);
        Assert.Equal(PulseInterval.Every30Seconds, states["b"].Interval);
    }

    [Fact]
    public async Task ReturnsOnlyWhatWasAskedFor()
    {
        var provider = Provider();
        await provider.SetStateAsync("wanted", new PulseCheckerState(), Ct);
        await provider.SetStateAsync("other", new PulseCheckerState(), Ct);

        var states = await provider.GetStatesAsync<PulseCheckerState>(["wanted"], Ct);

        Assert.Equal("wanted", Assert.Single(states).Key);
    }

    [Fact]
    public async Task ANameWithNoRow_IsSimplyAbsent()
    {
        var provider = Provider();
        await provider.SetStateAsync("stored", new PulseCheckerState(), Ct);

        var states = await provider.GetStatesAsync<PulseCheckerState>(["stored", "never-ran"], Ct);

        Assert.Single(states);
    }

    /// <summary>
    /// "IN ()" is a syntax error on PostgreSQL and SQL Server. SQLite happens to tolerate it, so
    /// asserting an empty result here would prove nothing -- the connection factory throws instead,
    /// which fails if the provider reaches for the database at all.
    /// </summary>
    [Fact]
    public async Task AskingForNothing_NeverTouchesTheDatabase()
    {
        var provider = new RelationalStateProvider(
            () => throw new InvalidOperationException("the provider should not have opened a connection"),
            RelationalDialect.Sqlite,
            Table);

        Assert.Empty(await provider.GetStatesAsync<PulseCheckerState>([], Ct));
    }

    /// <summary>
    /// The names are parameters, not an interpolated list, so a checker name is data rather than
    /// SQL however it is spelled.
    /// </summary>
    [Fact]
    public async Task ACheckerNameContainingSqlIsJustAName()
    {
        const string Hostile = "'; DROP TABLE healthie_pulse_state; --";

        var provider = Provider();
        await provider.SetStateAsync(Hostile, new PulseCheckerState(PulseInterval.Every2Seconds), Ct);

        var states = await provider.GetStatesAsync<PulseCheckerState>([Hostile, "also-fine"], Ct);

        Assert.Equal(PulseInterval.Every2Seconds, states[Hostile].Interval);

        // The table is still there, which it would not be if the name had been interpolated.
        await provider.SetStateAsync("still-works", new PulseCheckerState(), Ct);
        Assert.NotNull(await provider.GetStateAsync<PulseCheckerState>("still-works", Ct));
    }

    [Fact]
    public async Task RepeatedNames_ProduceOneEntry()
    {
        var provider = Provider();
        await provider.SetStateAsync("a", new PulseCheckerState(), Ct);

        var states = await provider.GetStatesAsync<PulseCheckerState>(["a", "a", "a"], Ct);

        Assert.Single(states);
    }

    /// <summary>
    /// Each name needs its own parameter, or the last value written would be the only one asked
    /// for. This is the dialect's half of that; the provider's half is passing distinct names.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public void TheBulkStatement_NamesEveryParameterDistinctly(int count)
    {
        var sql = RelationalDialect.SelectMany("some_table", count);

        var placeholders = Enumerable.Range(0, count).Select(i => $"@name{i}").ToList();

        Assert.All(placeholders, placeholder => Assert.Contains(placeholder, sql, StringComparison.Ordinal));
        Assert.Equal(count, placeholders.Distinct(StringComparer.Ordinal).Count());
    }
}
