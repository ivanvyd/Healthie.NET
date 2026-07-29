using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;
using Healthie.DependencyInjection;
using Healthie.StateProviding.Relational;
using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace Healthie.Tests.Unit;

/// <summary>
/// A checker that is renamed or removed leaves its state behind for ever, and the Hangfire and
/// Temporal packages both log that the leftovers can be cleaned up -- advice that until now had no
/// way to be taken.
/// </summary>
public class StateDeleteTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A provider written against the original two-method interface.</summary>
    private sealed class OlderProvider : IStateProvider
    {
        public Task<TState?> GetStateAsync<TState>(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<TState?>(default);

        public Task SetStateAsync<TState>(string name, TState state, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>
    /// A default that quietly did nothing would report a cleanup that never happened, so it refuses
    /// and names itself instead.
    /// </summary>
    [Fact]
    public async Task AProviderThatCannotDelete_RefusesAndNamesItself()
    {
        IStateProvider provider = new OlderProvider();

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => provider.DeleteStateAsync("gone", Ct));

        Assert.Contains(nameof(OlderProvider), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheInMemoryProvider_RemovesState()
    {
        IStateProvider provider = new InMemoryStateProvider();
        await provider.SetStateAsync("gone", new PulseCheckerState(), Ct);

        Assert.True(await provider.DeleteStateAsync("gone", Ct));
        Assert.Null(await provider.GetStateAsync<PulseCheckerState>("gone", Ct));
    }

    /// <summary>
    /// The distinction a caller can act on: "there was state and it is gone" against "there was
    /// none". A cleanup job wants to know which.
    /// </summary>
    [Fact]
    public async Task DeletingSomethingThatWasNeverThere_ReportsFalseRatherThanThrowing()
    {
        IStateProvider provider = new InMemoryStateProvider();

        Assert.False(await provider.DeleteStateAsync("never-existed", Ct));
    }

    [Fact]
    public async Task DeletingOneChecker_LeavesTheOthersAlone()
    {
        IStateProvider provider = new InMemoryStateProvider();
        await provider.SetStateAsync("gone", new PulseCheckerState(), Ct);
        await provider.SetStateAsync("kept", new PulseCheckerState(PulseInterval.Every5Minutes), Ct);

        await provider.DeleteStateAsync("gone", Ct);

        Assert.NotNull(await provider.GetStateAsync<PulseCheckerState>("kept", Ct));
    }
}

/// <summary>
/// Driven against a real SQLite file, so the DELETE is executed rather than inspected. This is the
/// provider all three relational packages share.
/// </summary>
public sealed class RelationalDeleteTests : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Table = "healthie_pulse_state";

    private string _databasePath = string.Empty;
    private string _connectionString = string.Empty;

    private DbConnection Connect() => new SqliteConnection(_connectionString);

    private RelationalStateProvider Provider() => new(Connect, RelationalDialect.Sqlite, Table);

    public async ValueTask InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"healthie-delete-{Guid.NewGuid():N}.db");
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
    public async Task RemovesTheRow()
    {
        var provider = Provider();
        await provider.SetStateAsync("gone", new PulseCheckerState(), Ct);

        Assert.True(await provider.DeleteStateAsync("gone", Ct));
        Assert.Null(await provider.GetStateAsync<PulseCheckerState>("gone", Ct));
    }

    [Fact]
    public async Task DeletingARowThatIsNotThere_ReportsFalse()
    {
        Assert.False(await Provider().DeleteStateAsync("never-existed", Ct));
    }

    [Fact]
    public async Task DeletesOnlyTheNamedRow()
    {
        var provider = Provider();
        await provider.SetStateAsync("gone", new PulseCheckerState(), Ct);
        await provider.SetStateAsync("kept", new PulseCheckerState(PulseInterval.Every20Seconds), Ct);

        await provider.DeleteStateAsync("gone", Ct);

        var kept = await provider.GetStateAsync<PulseCheckerState>("kept", Ct);
        Assert.Equal(PulseInterval.Every20Seconds, kept!.Interval);
    }

    /// <summary>
    /// The name is a parameter, not interpolated, so a checker name is data however it is spelled.
    /// </summary>
    [Fact]
    public async Task ACheckerNameContainingSqlIsJustAName()
    {
        const string Hostile = "'; DELETE FROM healthie_pulse_state; --";

        var provider = Provider();
        await provider.SetStateAsync(Hostile, new PulseCheckerState(), Ct);
        await provider.SetStateAsync("bystander", new PulseCheckerState(), Ct);

        Assert.True(await provider.DeleteStateAsync(Hostile, Ct));

        // The bystander survives, which it would not if the name had been interpolated.
        Assert.NotNull(await provider.GetStateAsync<PulseCheckerState>("bystander", Ct));
    }

    [Fact]
    public async Task DeletedStateStaysDeleted()
    {
        var provider = Provider();
        await provider.SetStateAsync("gone", new PulseCheckerState(), Ct);
        await provider.DeleteStateAsync("gone", Ct);

        Assert.False(await provider.DeleteStateAsync("gone", Ct));
    }
}
