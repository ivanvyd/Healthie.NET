using System.Globalization;
using System.Text.RegularExpressions;

namespace Healthie.StateProviding.Relational;

/// <summary>
/// The parts of the state provider's SQL that differ between database engines.
/// </summary>
/// <remarks>
/// <para>
/// Reading is identical everywhere, so only creating the table and writing a row need saying.
/// Between them they are the whole dialect surface, which is why one provider serves PostgreSQL,
/// SQL Server and SQLite rather than three near-identical ones.
/// </para>
/// <para>
/// The three below are supplied ready-made. Anything else with an ADO.NET driver -- MySQL, Oracle,
/// Firebird -- works by constructing one of these, so a database this library has never heard of
/// does not need a release to support.
/// </para>
/// </remarks>
/// <param name="Name">The engine's name, used in error messages.</param>
/// <param name="CreateTableFormat">
/// Statement creating the table if absent, with <c>{0}</c> for the table name. Must be safe to run
/// against a database where the table already exists.
/// </param>
/// <param name="UpsertFormat">
/// Statement inserting or replacing one row, with <c>{0}</c> for the table name and the parameters
/// <c>@name</c>, <c>@state_type</c> and <c>@value</c>.
/// </param>
public sealed record RelationalDialect(string Name, string CreateTableFormat, string UpsertFormat)
{
    /// <summary>Reads one row. Identical on every engine, so it is not part of the dialect.</summary>
    internal const string SelectFormat =
        "SELECT state_type, value FROM {0} WHERE name = @name";

    /// <summary>
    /// A table name, optionally schema-qualified. Anything else is refused rather than interpolated.
    /// </summary>
    private static readonly Regex SafeTableName = new(
        @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)?$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>PostgreSQL, and anything speaking its dialect -- including Databricks Lakebase.</summary>
    public static RelationalDialect PostgreSql { get; } = new(
        "PostgreSQL",
        "CREATE TABLE IF NOT EXISTS {0} (" +
            "name TEXT NOT NULL PRIMARY KEY, " +
            "state_type TEXT NULL, " +
            "value TEXT NOT NULL)",
        "INSERT INTO {0} (name, state_type, value) VALUES (@name, @state_type, @value) " +
            "ON CONFLICT (name) DO UPDATE SET state_type = EXCLUDED.state_type, value = EXCLUDED.value");

    /// <remarks>
    /// <c>name</c> is capped at 450 characters because that is the longest a SQL Server primary key
    /// column may be. A pulse checker's name defaults to its type's full name, which does not come
    /// close, but a name past the cap fails on insert rather than silently truncating.
    /// <para>
    /// The write updates first and inserts only if nothing was updated, under <c>UPDLOCK,
    /// SERIALIZABLE</c>. Those hints are what make it safe: without them two writers can both find
    /// no row and both insert, and one of them gets a primary key violation.
    /// </para>
    /// </remarks>
    public static RelationalDialect SqlServer { get; } = new(
        "SQL Server",
        "IF OBJECT_ID(N'{0}', N'U') IS NULL CREATE TABLE {0} (" +
            "name NVARCHAR(450) NOT NULL PRIMARY KEY, " +
            "state_type NVARCHAR(4000) NULL, " +
            "value NVARCHAR(MAX) NOT NULL)",
        "UPDATE {0} WITH (UPDLOCK, SERIALIZABLE) SET state_type = @state_type, value = @value " +
            "WHERE name = @name; " +
        "IF @@ROWCOUNT = 0 " +
            "INSERT INTO {0} (name, state_type, value) VALUES (@name, @state_type, @value);");

    /// <summary>SQLite, which needs no server and so suits a single node or a sample.</summary>
    public static RelationalDialect Sqlite { get; } = new(
        "SQLite",
        "CREATE TABLE IF NOT EXISTS {0} (" +
            "name TEXT NOT NULL PRIMARY KEY, " +
            "state_type TEXT NULL, " +
            "value TEXT NOT NULL)",
        "INSERT INTO {0} (name, state_type, value) VALUES (@name, @state_type, @value) " +
            "ON CONFLICT(name) DO UPDATE SET state_type = excluded.state_type, value = excluded.value");

    /// <summary>
    /// Checks a table name before it is put into a statement.
    /// </summary>
    /// <remarks>
    /// The table name is the one part of this SQL that is not a parameter, because no database lets
    /// you parameterise an identifier. It comes from configuration rather than from a user, but
    /// configuration is not a trust boundary worth betting on, so anything that is not a plain
    /// identifier -- optionally schema-qualified -- is refused.
    /// </remarks>
    /// <param name="tableName">The configured table name.</param>
    /// <exception cref="ArgumentException">The name is not a plain, optionally schema-qualified identifier.</exception>
    internal static void ValidateTableName(string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        if (!SafeTableName.IsMatch(tableName))
        {
            throw new ArgumentException(
                $"Table name '{tableName}' is not a plain identifier. Use letters, digits and " +
                "underscores, optionally as schema.table -- it is placed into SQL directly, because " +
                "no database allows an identifier to be parameterised.",
                nameof(tableName));
        }
    }

    internal string CreateTable(string tableName) => Format(CreateTableFormat, tableName);

    internal string Upsert(string tableName) => Format(UpsertFormat, tableName);

    internal static string Select(string tableName) => Format(SelectFormat, tableName);

    private static string Format(string format, string tableName) =>
        string.Format(CultureInfo.InvariantCulture, format, tableName);
}
