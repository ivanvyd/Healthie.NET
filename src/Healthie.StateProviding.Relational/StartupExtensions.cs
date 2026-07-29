using Healthie.Abstractions.StateProviding;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;

namespace Healthie.StateProviding.Relational;

/// <summary>
/// Extension methods for registering a relational state provider with dependency injection.
/// </summary>
public static class StartupExtensions
{
    /// <summary>
    /// The table state is stored in unless another name is given.
    /// </summary>
    /// <remarks>
    /// Lower case with underscores so it needs no quoting on any of the supported engines:
    /// PostgreSQL folds an unquoted identifier to lower case, and quoting it here to preserve
    /// casing would make the table awkward to query by hand afterwards.
    /// </remarks>
    public const string DefaultTableName = "healthie_pulse_state";

    /// <summary>
    /// Registers a state provider backed by any relational database with an ADO.NET driver.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="connectionFactory">
    /// Creates a new, unopened connection. Called once per operation, so it should hand back a
    /// connection from the driver's pool rather than a shared instance.
    /// </param>
    /// <param name="dialect">
    /// The engine's SQL. <see cref="RelationalDialect.PostgreSql"/>,
    /// <see cref="RelationalDialect.SqlServer"/> and <see cref="RelationalDialect.Sqlite"/> are
    /// supplied; construct one for any other engine.
    /// </param>
    /// <param name="tableName">
    /// The table to store state in, created on startup if absent. Must be a plain identifier,
    /// optionally schema-qualified.
    /// </param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <remarks>
    /// Registered with <c>AddSingleton</c> rather than <c>TryAddSingleton</c> on purpose: the
    /// built-in in-memory provider registers with <c>TryAdd</c>, so whichever call comes first,
    /// this one wins and registration stays order-independent.
    /// </remarks>
    public static IServiceCollection AddHealthieRelational(
        this IServiceCollection services,
        Func<DbConnection> connectionFactory,
        RelationalDialect dialect,
        string tableName = DefaultTableName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(dialect);
        RelationalDialect.ValidateTableName(tableName);

        services.AddSingleton<IStateProvider>(
            new RelationalStateProvider(connectionFactory, dialect, tableName));

        services.AddSingleton<IStateProviderInitializer>(
            new RelationalStateProviderInitializer(connectionFactory, dialect, tableName));

        return services;
    }
}
