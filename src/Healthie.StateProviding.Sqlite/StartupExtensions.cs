using Microsoft.Data.Sqlite;
using Healthie.StateProviding.Relational;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.StateProviding.Sqlite;

/// <summary>
/// Extension methods for registering the SQLite state provider with dependency injection.
/// </summary>
public static class StartupExtensions
{
    /// <summary>
    /// Registers a state provider backed by SQLite.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="connectionString">The connection string to the database.</param>
    /// <param name="tableName">
    /// The table to store state in, created on startup if absent. Must be a plain identifier,
    /// optionally schema-qualified.
    /// </param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <remarks>
    /// Needs no server, which makes it the durable option for a single node or a sample. SQLite serialises writers, so a deployment running several replicas against one file will contend; use PostgreSQL or SQL Server there.
    /// </remarks>
    public static IServiceCollection AddHealthieSqlite(
        this IServiceCollection services,
        string connectionString,
        string tableName = Relational.StartupExtensions.DefaultTableName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return services.AddHealthieRelational(
            () => new SqliteConnection(connectionString),
            RelationalDialect.Sqlite,
            tableName);
    }
}
