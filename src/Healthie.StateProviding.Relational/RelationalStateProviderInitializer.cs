using Healthie.Abstractions.StateProviding;
using System.Data;
using System.Data.Common;

namespace Healthie.StateProviding.Relational;

/// <summary>
/// Creates the table backing <see cref="RelationalStateProvider"/> on startup if it is not there.
/// </summary>
/// <remarks>
/// The database itself must already exist; only the table is created. Every dialect's statement is
/// written to be safe against a table that already exists, so this runs on every start rather than
/// needing to know whether it has run before.
/// </remarks>
/// <param name="connectionFactory">Creates a new, unopened connection to the database.</param>
/// <param name="dialect">The engine's SQL. See <see cref="RelationalDialect"/>.</param>
/// <param name="tableName">The table to create.</param>
public sealed class RelationalStateProviderInitializer(
    Func<DbConnection> connectionFactory,
    RelationalDialect dialect,
    string tableName) : IStateProviderInitializer
{
    private readonly Func<DbConnection> _connectionFactory = connectionFactory
        ?? throw new ArgumentNullException(nameof(connectionFactory));

    private readonly string _createTableSql = (dialect ?? throw new ArgumentNullException(nameof(dialect)))
        .CreateTable(Validated(tableName));

    private readonly string _addVersionColumnSql = dialect.AddVersionColumn(Validated(tableName));
    private readonly string _tableName = Validated(tableName);

    private static string Validated(string tableName)
    {
        RelationalDialect.ValidateTableName(tableName);
        return tableName;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = _createTableSql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await AddVersionColumnIfMissingAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Brings a table created before versioning existed up to date.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The column is checked for rather than added blindly, because no engine here spells "add it
    /// if it is missing" in a way the others also understand. So the check and the change are two
    /// steps, and two instances starting together can both pass the check. The loser's <c>ALTER</c>
    /// then fails, which would be a startup crash on a database that is in fact correct.
    /// </para>
    /// <para>
    /// So a failure is not taken at face value: the columns are read again, and a failure that left
    /// the column present was the other instance winning the race and is not an error. Anything else
    /// is rethrown untouched. That avoids matching on error text, which differs per engine and would
    /// swallow real failures along with this one.
    /// </para>
    /// <para>
    /// Asking a query for no rows is how the columns are read, because every ADO.NET provider
    /// answers it the same way and none of them needs the rows to describe the shape.
    /// </para>
    /// </remarks>
    private async Task AddVersionColumnIfMissingAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (await HasVersionColumnAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = _addVersionColumnSql;

            await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!await HasVersionColumnAsync(connection, cancellationToken).ConfigureAwait(false))
            {
                throw;
            }
        }
    }

    /// <summary>Whether the table already carries the version column.</summary>
    private async Task<bool> HasVersionColumnAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var probe = connection.CreateCommand();
        probe.CommandText = $"SELECT * FROM {_tableName} WHERE 1 = 0";

        await using var reader = await probe
            .ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken)
            .ConfigureAwait(false);

        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), "version", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
