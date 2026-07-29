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
    /// The column is checked for rather than added blindly. PostgreSQL and SQL Server can express
    /// "add it if it is missing" in one statement, SQLite cannot -- and catching whatever error each
    /// engine raises for a duplicate column would mean guessing at messages and swallowing real
    /// failures with them.
    /// </para>
    /// <para>
    /// Asking a query for no rows is how the columns are read, because every ADO.NET provider
    /// answers it the same way and none of them needs the rows to describe the shape.
    /// </para>
    /// </remarks>
    private async Task AddVersionColumnIfMissingAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText = $"SELECT * FROM {_tableName} WHERE 1 = 0";

            await using var reader = await probe
                .ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken)
                .ConfigureAwait(false);

            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), "version", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = _addVersionColumnSql;

        await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
