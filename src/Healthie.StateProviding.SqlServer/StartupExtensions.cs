using Microsoft.Data.SqlClient;
using Healthie.StateProviding.Relational;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.StateProviding.SqlServer;

/// <summary>
/// Extension methods for registering the SQL Server state provider with dependency injection.
/// </summary>
public static class StartupExtensions
{
    /// <summary>
    /// Registers a state provider backed by SQL Server.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="connectionString">The connection string to the database.</param>
    /// <param name="tableName">
    /// The table to store state in, created on startup if absent. Must be a plain identifier,
    /// optionally schema-qualified.
    /// </param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <remarks>
    /// The checker name is the primary key and so is capped at 450 characters, which is the longest a SQL Server key column may be. A checker name defaults to its type's full name and does not come close.
    /// </remarks>
    public static IServiceCollection AddHealthieSqlServer(
        this IServiceCollection services,
        string connectionString,
        string tableName = Relational.StartupExtensions.DefaultTableName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return services.AddHealthieRelational(
            () => new SqlConnection(connectionString),
            RelationalDialect.SqlServer,
            tableName);
    }
}
