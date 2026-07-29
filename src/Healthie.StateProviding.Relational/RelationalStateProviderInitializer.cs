using Healthie.Abstractions.StateProviding;
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

        await using var command = connection.CreateCommand();
        command.CommandText = _createTableSql;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
