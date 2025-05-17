using Microsoft.Data.SqlClient;

namespace Healthie.StateProviding.SqlServer.Helpers;

internal static class SqlCommandsHelper
{
    public static SqlCommand GenerateSetStateCommand<TState>(this SqlConnection connection,
        string tableName,
        string name,
        string jsonState)
    {
        var stateType = typeof(TState).AssemblyQualifiedName;

        var sql = $@"
            MERGE {tableName} AS target
            USING (SELECT @Name AS Name, @Value AS Value, @StateType AS StateType) AS source
            ON (target.Name = source.Name)
            WHEN MATCHED THEN
                UPDATE SET Value = source.Value, StateType = source.StateType
            WHEN NOT MATCHED THEN
                INSERT (Name, Value, StateType) VALUES (source.Name, source.Value, source.StateType);";

        SqlCommand command = new(cmdText: sql, connection);
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Value", jsonState);
        command.Parameters.AddWithValue("@StateType", stateType);

        return command;
    }

    public static SqlCommand GenerateInitializeCommand(this SqlConnection connection, string tableName)
    {
        var checkTableSql = $@"
            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES 
                           WHERE TABLE_NAME = '{tableName}')
            BEGIN
                CREATE TABLE {tableName} (
                    Name NVARCHAR(450) PRIMARY KEY,
                    Value NVARCHAR(MAX) NOT NULL,
                    StateType NVARCHAR(1024) NOT NULL
                );
            END";

        SqlCommand command = new(checkTableSql, connection);

        return command;
    }

    public static SqlCommand GenerateGetStateQuery(this SqlConnection connection,
        string tableName,
        string name)
    {
        SqlCommand command = new($"SELECT Value FROM {tableName} WHERE Name = @Name", 
            connection);

        command.Parameters.AddWithValue("@Name", name);

        return command;
    }
}
