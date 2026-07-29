using Healthie.Abstractions.StateProviding;
using System.Data;
using System.Data.Common;
using System.Text.Json;

namespace Healthie.StateProviding.Relational;

/// <summary>
/// Persists pulse checker state to any relational database with an ADO.NET driver.
/// </summary>
/// <remarks>
/// <para>
/// State is stored as JSON in a single table keyed by checker name, so the schema does not change
/// when the state model does. Reads deserialize into a fresh object, which is what
/// <c>StateChanged</c> needs: it compares the state that was stored against the state about to be
/// written, and handing back a shared instance would make every comparison find them equal.
/// </para>
/// <para>
/// A connection is opened per operation and disposed after it. That is what the pooling built into
/// every ADO.NET driver expects, and holding one open for the lifetime of a singleton would break
/// on the first network blip with no way back.
/// </para>
/// </remarks>
/// <param name="connectionFactory">Creates a new, unopened connection to the database.</param>
/// <param name="dialect">The engine's SQL. See <see cref="RelationalDialect"/>.</param>
/// <param name="tableName">The table holding the state.</param>
public sealed class RelationalStateProvider(
    Func<DbConnection> connectionFactory,
    RelationalDialect dialect,
    string tableName) : IStateProvider
{
    private readonly Func<DbConnection> _connectionFactory = connectionFactory
        ?? throw new ArgumentNullException(nameof(connectionFactory));

    private readonly RelationalDialect _dialect = dialect
        ?? throw new ArgumentNullException(nameof(dialect));

    private readonly string _tableName = Validated(tableName);
    private readonly string _selectSql = RelationalDialect.Select(Validated(tableName));
    private readonly string _upsertSql = dialect.Upsert(Validated(tableName));

    private static string Validated(string tableName)
    {
        RelationalDialect.ValidateTableName(tableName);
        return tableName;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The stored row records a state type other than <typeparamref name="TState"/>.
    /// </exception>
    public async Task<TState?> GetStateAsync<TState>(
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = _selectSql;
        AddParameter(command, "@name", name);

        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return default;
        }

        var storedStateType = await reader.IsDBNullAsync(0, cancellationToken).ConfigureAwait(false)
            ? null
            : reader.GetString(0);

        EnsureStoredTypeMatches<TState>(name, storedStateType);

        return JsonSerializer.Deserialize<TState>(reader.GetString(1));
    }

    /// <inheritdoc />
    public async Task SetStateAsync<TState>(
        string name,
        TState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = _upsertSql;
        AddParameter(command, "@name", name);
        AddParameter(command, "@state_type", typeof(TState).FullName);
        AddParameter(command, "@value", JsonSerializer.Serialize(state));
        AddParameter(command, "@version", NewVersion());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One query rather than one per name. Every dashboard load and every list request reads the
    /// whole set, and against a remote database the difference is a page that renders and one that
    /// waits. The names are parameters, not an interpolated list, so a checker name can contain
    /// anything without becoming SQL.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, TState>> GetStatesAsync<TState>(
        IEnumerable<string> names,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);

        var wanted = names.Distinct(StringComparer.Ordinal).ToList();
        var states = new Dictionary<string, TState>(StringComparer.Ordinal);

        // An IN () with nothing in it is a syntax error on most engines, and there is nothing to ask.
        if (wanted.Count == 0)
        {
            return states;
        }

        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = RelationalDialect.SelectMany(_tableName, wanted.Count);

        for (var i = 0; i < wanted.Count; i++)
        {
            AddParameter(command, $"@name{i}", wanted[i]);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = reader.GetString(0);

            var storedStateType = await reader.IsDBNullAsync(1, cancellationToken).ConfigureAwait(false)
                ? null
                : reader.GetString(1);

            EnsureStoredTypeMatches<TState>(name, storedStateType);

            if (JsonSerializer.Deserialize<TState>(reader.GetString(2)) is { } state)
            {
                states[name] = state;
            }
        }

        return states;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteStateAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = RelationalDialect.Delete(_tableName);
        AddParameter(command, "@name", name);

        // Rows affected is what distinguishes "there was state and it is gone" from "there was
        // none", which is the only thing a caller can act on.
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public bool SupportsOptimisticConcurrency => true;

    /// <summary>
    /// A fresh version for a write.
    /// </summary>
    /// <remarks>
    /// A new value each time, generated here rather than by the database, so the same statement
    /// works on every engine -- PostgreSQL has no auto-updating column and SQL Server's rowversion
    /// is not portable. It is opaque to callers, so only its uniqueness matters.
    /// </remarks>
    private static string NewVersion() => Guid.NewGuid().ToString("N");

    /// <inheritdoc />
    public async Task<StateEntry<TState>?> GetStateEntryAsync<TState>(
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = RelationalDialect.SelectWithVersion(_tableName);
        AddParameter(command, "@name", name);

        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var storedStateType = await reader.IsDBNullAsync(0, cancellationToken).ConfigureAwait(false)
            ? null
            : reader.GetString(0);

        EnsureStoredTypeMatches<TState>(name, storedStateType);

        var value = JsonSerializer.Deserialize<TState>(reader.GetString(1));

        if (value is null)
        {
            return null;
        }

        // Null for a row written before the column existed. Reported as unversioned rather than
        // invented: a caller then writes it unconditionally, exactly as it did before the upgrade,
        // and the row carries a version from that write on.
        var version = await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false)
            ? null
            : reader.GetString(2);

        return new StateEntry<TState>(value, version);
    }

    /// <inheritdoc />
    public async Task<bool> TrySetStateAsync<TState>(
        string name,
        TState state,
        string? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (expectedVersion is null)
        {
            await SetStateAsync(name, state, cancellationToken).ConfigureAwait(false);
            return true;
        }

        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = expectedVersion == IStateProvider.AbsentVersion
            ? RelationalDialect.InsertIfAbsent(_tableName)
            : RelationalDialect.ConditionalUpdate(_tableName);
        AddParameter(command, "@name", name);
        AddParameter(command, "@state_type", typeof(TState).FullName);
        AddParameter(command, "@value", JsonSerializer.Serialize(state));
        AddParameter(command, "@version", NewVersion());

        if (expectedVersion != IStateProvider.AbsentVersion)
        {
            AddParameter(command, "@expected_version", expectedVersion);
        }

        // Nothing updated means the version moved on, or the row is gone. Either way this write
        // lost, which is exactly what the caller asked to be told.
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    private static void AddParameter(DbCommand command, string name, string? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = (object?)value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// Verifies that the type recorded when the state was written is the type it is being read as.
    /// </summary>
    /// <remarks>
    /// Compares the full name rather than the assembly-qualified one. This library's assembly
    /// version tracks its release version, so an assembly-qualified comparison would reject every
    /// row written by a previous release -- and a pulse checker treats a failed read as its own
    /// failure, so an upgrade would take every checker unhealthy on data that is perfectly valid.
    /// </remarks>
    private static void EnsureStoredTypeMatches<TState>(string name, string? storedStateType)
    {
        // A row written before the type was recorded carries nothing to compare against.
        if (string.IsNullOrWhiteSpace(storedStateType))
        {
            return;
        }

        var expectedStateType = typeof(TState).FullName;

        if (expectedStateType is null
            || string.Equals(storedStateType, expectedStateType, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(
            $"State stored for pulse checker '{name}' was written as '{storedStateType}' but is being " +
            $"read as '{expectedStateType}'. Migrate or delete the stored row before reading it as a " +
            "different type.");
    }
}
