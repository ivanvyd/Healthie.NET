using Npgsql;
using Healthie.StateProviding.Relational;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.StateProviding.Postgres;

/// <summary>
/// Extension methods for registering the PostgreSQL state provider with dependency injection.
/// </summary>
public static class StartupExtensions
{
    /// <summary>
    /// Registers a state provider backed by PostgreSQL.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="connectionString">The connection string to the database.</param>
    /// <param name="tableName">
    /// The table to store state in, created on startup if absent. Must be a plain identifier,
    /// optionally schema-qualified.
    /// </param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <remarks>
    /// Works against anything speaking the PostgreSQL wire protocol, which includes Databricks Lakebase -- it is managed PostgreSQL, so no Databricks-specific code is involved.
    /// </remarks>
    public static IServiceCollection AddHealthiePostgres(
        this IServiceCollection services,
        string connectionString,
        string tableName = Relational.StartupExtensions.DefaultTableName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return services.AddHealthieRelational(
            () => new NpgsqlConnection(connectionString),
            RelationalDialect.PostgreSql,
            tableName);
    }
}
