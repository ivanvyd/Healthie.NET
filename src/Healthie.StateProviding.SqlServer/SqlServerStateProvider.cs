using Healthie.Abstractions.StateProviding;
using Healthie.StateProviding.SqlServer.Helpers;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace Healthie.StateProviding.SqlServer;

public class SqlServerStateProvider(string connectionString) :
    IStateProvider, IStateProviderInitializer,
    IAsyncStateProvider, IAsyncStateProviderInitializer
{
    private readonly string _connectionString = connectionString;

    private const string TableName = "HealthieState";

    public TState? GetState<TState>(string name)
    {
        using SqlConnection connection = OpenConnection();

        using SqlCommand command = connection.GenerateGetStateQuery(TableName, name);

        object? result = command.ExecuteScalar();
        if (result == null || result == DBNull.Value)
        {
            return default;
        }

        return JsonSerializer.Deserialize<TState>((string)result);
    }

    public async Task<TState?> GetStateAsync<TState>(string name)
    {
        await using SqlConnection connection = await OpenConnectionAsync();

        await using SqlCommand command = connection.GenerateGetStateQuery(TableName, name);

        object? result = await command.ExecuteScalarAsync();
        if (result == null || result == DBNull.Value)
        {
            return default;
        }

        return JsonSerializer.Deserialize<TState>((string)result);
    }

    public void SetState<TState>(string name, TState state)
    {
        using SqlConnection connection = OpenConnection();

        string jsonState = JsonSerializer.Serialize(state);

        using SqlCommand command = connection.GenerateSetStateCommand<TState>(TableName,
            name,
            jsonState);

        command.ExecuteNonQuery();
    }

    public async Task SetStateAsync<TState>(string name, TState state)
    {
        await using SqlConnection connection = await OpenConnectionAsync();

        string jsonState = JsonSerializer.Serialize(state);

        await using SqlCommand command = connection.GenerateSetStateCommand<TState>(TableName,
            name,
            jsonState);

        await command.ExecuteNonQueryAsync();
    }

    public async Task InitializeAsync()
    {
        await using SqlConnection connection = await OpenConnectionAsync();

        // TODO: validate the schema.
        await using SqlCommand command = connection.GenerateInitializeCommand(TableName);

        await command.ExecuteNonQueryAsync();
    }

    public void Initialize()
    {
        using SqlConnection connection = OpenConnection();

        // TODO: validate the schema.
        using SqlCommand command = connection.GenerateInitializeCommand(TableName);

        command.ExecuteNonQuery();
    }

    private SqlConnection OpenConnection()
    {
        SqlConnection connection = new(_connectionString);

        connection.Open();

        return connection;
    }

    private async Task<SqlConnection> OpenConnectionAsync()
    {
        SqlConnection connection = new(_connectionString);

        await connection.OpenAsync();

        return connection;
    }
}
