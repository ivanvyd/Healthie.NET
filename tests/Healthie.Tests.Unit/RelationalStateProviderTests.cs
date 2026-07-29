using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.StateProviding.Relational;
using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace Healthie.Tests.Unit;

/// <summary>
/// Driven against a real SQLite file rather than a fake connection. The provider is one piece of
/// code shared by the PostgreSQL, SQL Server and SQLite packages, so exercising it against a real
/// database exercises all three -- and SQLite is the one of them that needs nothing installed.
/// </summary>
public sealed class RelationalStateProviderTests : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Table = "healthie_pulse_state";

    private string _databasePath = string.Empty;
    private string _connectionString = string.Empty;

    private DbConnection Connect() => new SqliteConnection(_connectionString);

    private RelationalStateProvider Provider() =>
        new(Connect, RelationalDialect.Sqlite, Table);

    public async ValueTask InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"healthie-{Guid.NewGuid():N}.db");

        // Pooling off so the file is closed when the last connection is, and the test can delete it.
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
    public async Task GetStateAsync_ForACheckerThatHasNeverRun_ReturnsNull()
    {
        Assert.Null(await Provider().GetStateAsync<PulseCheckerState>("never-ran", Ct));
    }

    [Fact]
    public async Task SetStateAsync_ThenGetStateAsync_ReturnsTheSameValues()
    {
        var provider = Provider();
        var state = new PulseCheckerState(PulseInterval.Every30Seconds, 3)
        {
            LastResult = new PulseCheckerResult(PulseCheckerHealth.Unhealthy, "down"),
            Group = "data",
            Tags = ["cloud", "primary"],
            IsPinned = true,
        };

        await provider.SetStateAsync("round-trip", state, Ct);

        Assert.Equal(state, await provider.GetStateAsync<PulseCheckerState>("round-trip", Ct));
    }

    /// <summary>
    /// The upsert is the half of the dialect most likely to be wrong, and a second write is the
    /// only thing that exercises it -- an insert-only implementation passes every other test here.
    /// </summary>
    [Fact]
    public async Task SetStateAsync_WrittenTwice_KeepsTheSecondWrite()
    {
        var provider = Provider();

        await provider.SetStateAsync("rewritten", new PulseCheckerState(PulseInterval.EverySecond), Ct);
        await provider.SetStateAsync("rewritten", new PulseCheckerState(PulseInterval.Every5Minutes), Ct);

        var stored = await provider.GetStateAsync<PulseCheckerState>("rewritten", Ct);

        Assert.Equal(PulseInterval.Every5Minutes, stored!.Interval);
    }

    /// <summary>
    /// StateChanged compares the state that was stored against the one about to be written, so a
    /// read has to hand back an independent object. Returning a shared instance would make every
    /// comparison find them equal and the event would never fire.
    /// </summary>
    [Fact]
    public async Task GetStateAsync_ReturnsACopy_NotSomethingSharedWithTheStore()
    {
        var provider = Provider();
        await provider.SetStateAsync("independent", new PulseCheckerState(PulseInterval.EveryMinute), Ct);

        var first = await provider.GetStateAsync<PulseCheckerState>("independent", Ct);
        first!.Interval = PulseInterval.EverySecond;

        var second = await provider.GetStateAsync<PulseCheckerState>("independent", Ct);

        Assert.Equal(PulseInterval.EveryMinute, second!.Interval);
    }

    [Fact]
    public async Task GetStateAsync_WhenTheRowWasWrittenAsAnotherType_Throws()
    {
        var provider = Provider();
        await provider.SetStateAsync("mistyped", new PulseCheckerState(), Ct);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetStateAsync<PulseCheckerResult>("mistyped", Ct));

        Assert.Contains("mistyped", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoCheckers_DoNotOverwriteEachOther()
    {
        var provider = Provider();

        await provider.SetStateAsync("first", new PulseCheckerState(PulseInterval.EverySecond), Ct);
        await provider.SetStateAsync("second", new PulseCheckerState(PulseInterval.Every5Minutes), Ct);

        Assert.Equal(PulseInterval.EverySecond,
            (await provider.GetStateAsync<PulseCheckerState>("first", Ct))!.Interval);
        Assert.Equal(PulseInterval.Every5Minutes,
            (await provider.GetStateAsync<PulseCheckerState>("second", Ct))!.Interval);
    }

    /// <summary>The initializer runs on every start, so running it twice has to be harmless.</summary>
    [Fact]
    public async Task InitializeAsync_RunAgainstAnExistingTable_KeepsWhatIsAlreadyThere()
    {
        var provider = Provider();
        await provider.SetStateAsync("survives-restart", new PulseCheckerState(PulseInterval.Every20Seconds), Ct);

        await new RelationalStateProviderInitializer(Connect, RelationalDialect.Sqlite, Table)
            .InitializeAsync(Ct);

        var stored = await provider.GetStateAsync<PulseCheckerState>("survives-restart", Ct);
        Assert.Equal(PulseInterval.Every20Seconds, stored!.Interval);
    }
}

/// <summary>
/// The table name is the one thing that cannot be a parameter -- no database allows an identifier
/// to be parameterised -- so it is the one thing that has to be checked before it reaches the SQL.
/// </summary>
public class RelationalDialectTests
{
    [Theory]
    [InlineData("healthie_pulse_state")]
    [InlineData("dbo.healthie_pulse_state")]
    [InlineData("_leading_underscore")]
    [InlineData("Mixed_Case9")]
    public void APlainIdentifier_IsAccepted(string tableName)
    {
        RelationalDialect.ValidateTableName(tableName);
    }

    [Theory]
    [InlineData("state; DROP TABLE users--")]
    [InlineData("state'")]
    [InlineData("9starts_with_a_digit")]
    [InlineData("has space")]
    [InlineData("too.many.parts")]
    [InlineData("")]
    [InlineData("   ")]
    public void AnythingElse_IsRefused(string tableName)
    {
        Assert.Throws<ArgumentException>(() => RelationalDialect.ValidateTableName(tableName));
    }

    /// <summary>
    /// Each engine spells the upsert differently, and getting one wrong shows up as state that
    /// never updates. These do not prove the SQL runs -- only SQLite is executed here -- but they
    /// do catch a dialect that forgot a column or a parameter.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllDialects))]
    public void EveryDialect_NamesTheTableAndEveryParameter(RelationalDialect dialect)
    {
        var createTable = dialect.CreateTable("some_table");
        var upsert = dialect.Upsert("some_table");

        Assert.Contains("some_table", createTable, StringComparison.Ordinal);
        Assert.Contains("some_table", upsert, StringComparison.Ordinal);

        foreach (var column in new[] { "name", "state_type", "value" })
        {
            Assert.Contains(column, createTable, StringComparison.Ordinal);
            Assert.Contains($"@{column}", upsert, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The initializer runs on every start, so each dialect's create has to tolerate the table
    /// already existing rather than throwing on the second boot.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllDialects))]
    public void EveryDialect_CreatesTheTableOnlyIfItIsMissing(RelationalDialect dialect)
    {
        var createTable = dialect.CreateTable("some_table");

        Assert.True(
            createTable.Contains("IF NOT EXISTS", StringComparison.OrdinalIgnoreCase)
                || createTable.Contains("IS NULL", StringComparison.OrdinalIgnoreCase),
            $"{dialect.Name} would fail on a second start: {createTable}");
    }

    public static TheoryData<RelationalDialect> AllDialects() =>
    [
        RelationalDialect.PostgreSql,
        RelationalDialect.SqlServer,
        RelationalDialect.Sqlite,
    ];
}
