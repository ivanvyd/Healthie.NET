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
/// <param name="AddVersionColumnFormat">
/// Statement adding the version column to a table that predates it, with <c>{0}</c> for the table
/// name. Run only when the column is missing. Optional: a dialect that does not supply one is left
/// alone by the migration, which is right for a table this library created and wrong only for one
/// that predates versioning, where the statement is the whole point.
/// </param>
/// <param name="InsertIfAbsentFormat">
/// Statement inserting one row only if the name is not taken, reporting the outcome through rows
/// affected: one means it was written, zero that somebody else got there first. Optional, and
/// <see cref="PortableInsertIfAbsentFormat"/> is used when it is not given.
/// </param>
public sealed record RelationalDialect(
    string Name,
    string CreateTableFormat,
    string UpsertFormat,
    string? AddVersionColumnFormat = null,
    string? InsertIfAbsentFormat = null)
{
    /// <summary>
    /// The fallback for a dialect that does not supply its own, which is every hand-built one.
    /// </summary>
    /// <remarks>
    /// It works everywhere and is <b>not</b> atomic: under READ COMMITTED two writers can both find
    /// no row, both insert, and the loser take a primary key violation instead of being told it
    /// lost. That is a thrown exception rather than a lost update, so it is safe in the sense that
    /// matters and wrong in the sense that is visible. The three dialects below each override it
    /// with a form their engine performs in one step.
    /// </remarks>
    internal const string PortableInsertIfAbsentFormat =
        "INSERT INTO {0} (name, state_type, value, version) " +
            "SELECT @name, @state_type, @value, @version " +
            "WHERE NOT EXISTS (SELECT 1 FROM {0} WHERE name = @name)";

    /// <summary>Reads one row. Identical on every engine, so it is not part of the dialect.</summary>
    internal const string SelectFormat =
        "SELECT state_type, value FROM {0} WHERE name = @name";

    /// <summary>Reads one row with the version to write back against.</summary>
    internal const string SelectWithVersionFormat =
        "SELECT state_type, value, version FROM {0} WHERE name = @name";

    /// <summary>
    /// Writes one row only if its version is still what the caller read.
    /// </summary>
    /// <remarks>
    /// The version in the WHERE clause is what makes this conditional, and rows-affected is how the
    /// engine reports the outcome: zero means somebody else wrote first. It is the same shape EF
    /// Core generates for a concurrency token, and it works on every engine without a stored
    /// procedure or a lock.
    /// </remarks>
    internal const string ConditionalUpdateFormat =
        "UPDATE {0} SET state_type = @state_type, value = @value, version = @version " +
            "WHERE name = @name AND version = @expected_version";

    /// <summary>Reads many rows at once. The parameter list is built per call, from its length.</summary>
    /// <remarks>
    /// The names go in as parameters rather than as an interpolated list, so a checker name can
    /// contain anything at all without it becoming SQL.
    /// </remarks>
    internal const string SelectManyFormat =
        "SELECT name, state_type, value FROM {0} WHERE name IN ({1})";

    /// <summary>
    /// A table name, optionally schema-qualified. Anything else is refused rather than interpolated.
    /// </summary>
    /// <remarks>
    /// Ends at <c>\z</c> rather than <c>$</c>, which in .NET also matches immediately before a
    /// single trailing newline -- so <c>"state\n"</c> passed a guard whose own error message says
    /// it does not. A lone trailing newline is only whitespace to every engine here, so nothing
    /// could be smuggled through it, but a guard that admits what it documents as impossible is
    /// worth closing before something later depends on it.
    /// </remarks>
    private static readonly Regex SafeTableName = new(
        @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)?\z",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>PostgreSQL, and anything speaking its dialect -- including Databricks Lakebase.</summary>
    public static RelationalDialect PostgreSql { get; } = new(
        "PostgreSQL",
        "CREATE TABLE IF NOT EXISTS {0} (" +
            "name TEXT NOT NULL PRIMARY KEY, " +
            "state_type TEXT NULL, " +
            "value TEXT NOT NULL, " +
            "version TEXT NULL)",
        "INSERT INTO {0} (name, state_type, value, version) VALUES (@name, @state_type, @value, @version) " +
            "ON CONFLICT (name) DO UPDATE SET state_type = EXCLUDED.state_type, value = EXCLUDED.value, version = EXCLUDED.version",
        "ALTER TABLE {0} ADD COLUMN version TEXT NULL",
        // The engine resolves the conflict itself, so there is no window between deciding to insert
        // and inserting. A row that was already there reports zero rows affected, which is a refusal.
        "INSERT INTO {0} (name, state_type, value, version) VALUES (@name, @state_type, @value, @version) " +
            "ON CONFLICT (name) DO NOTHING");

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
            "value NVARCHAR(MAX) NOT NULL, " +
            "version NVARCHAR(64) NULL)",
        "UPDATE {0} WITH (UPDLOCK, SERIALIZABLE) SET state_type = @state_type, value = @value, version = @version " +
            "WHERE name = @name; " +
        "IF @@ROWCOUNT = 0 " +
            "INSERT INTO {0} (name, state_type, value, version) VALUES (@name, @state_type, @value, @version);",
        "ALTER TABLE {0} ADD version NVARCHAR(64) NULL",
        // SQL Server has no ON CONFLICT, so the existence check takes the same UPDLOCK, HOLDLOCK the
        // upsert above takes -- which is what stops a second writer reaching the same conclusion.
        "INSERT INTO {0} (name, state_type, value, version) " +
            "SELECT @name, @state_type, @value, @version " +
            "WHERE NOT EXISTS (SELECT 1 FROM {0} WITH (UPDLOCK, HOLDLOCK) WHERE name = @name);");

    /// <summary>SQLite, which needs no server and so suits a single node or a sample.</summary>
    public static RelationalDialect Sqlite { get; } = new(
        "SQLite",
        "CREATE TABLE IF NOT EXISTS {0} (" +
            "name TEXT NOT NULL PRIMARY KEY, " +
            "state_type TEXT NULL, " +
            "value TEXT NOT NULL, " +
            "version TEXT NULL)",
        "INSERT INTO {0} (name, state_type, value, version) VALUES (@name, @state_type, @value, @version) " +
            "ON CONFLICT(name) DO UPDATE SET state_type = excluded.state_type, value = excluded.value, version = excluded.version",
        "ALTER TABLE {0} ADD COLUMN version TEXT NULL",
        "INSERT INTO {0} (name, state_type, value, version) VALUES (@name, @state_type, @value, @version) " +
            "ON CONFLICT(name) DO NOTHING");

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

    /// <summary>Removes one row. Identical on every engine, so it is not part of the dialect.</summary>
    internal const string DeleteFormat = "DELETE FROM {0} WHERE name = @name";

    /// <summary>
    /// Adds the version column to a table created before it existed, or <c>null</c> when the
    /// dialect does not supply the statement.
    /// </summary>
    /// <remarks>
    /// A plain ALTER, run only when the column is genuinely missing -- the initializer checks first
    /// rather than relying on an IF NOT EXISTS that SQLite does not have for ADD COLUMN.
    /// </remarks>
    internal string? AddVersionColumn(string tableName) =>
        AddVersionColumnFormat is null ? null : Format(AddVersionColumnFormat, tableName);

    internal static string Select(string tableName) => Format(SelectFormat, tableName);

    internal static string SelectWithVersion(string tableName) => Format(SelectWithVersionFormat, tableName);

    internal static string ConditionalUpdate(string tableName) => Format(ConditionalUpdateFormat, tableName);

    internal string InsertIfAbsent(string tableName) =>
        Format(InsertIfAbsentFormat ?? PortableInsertIfAbsentFormat, tableName);

    internal static string Delete(string tableName) => Format(DeleteFormat, tableName);

    internal static string SelectMany(string tableName, int count) =>
        string.Format(
            CultureInfo.InvariantCulture,
            SelectManyFormat,
            tableName,
            string.Join(", ", Enumerable.Range(0, count).Select(i => $"@name{i}")));

    private static string Format(string format, string tableName) =>
        string.Format(CultureInfo.InvariantCulture, format, tableName);
}
