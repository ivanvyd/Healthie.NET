using DotNet.Testcontainers.Containers;
using Testcontainers.CosmosDb;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Healthie.Tests.Unit;

/// <summary>
/// Starts one container for a whole test class rather than one per test.
/// </summary>
/// <remarks>
/// <para>
/// xUnit builds a fresh instance of a test class for every test method, so a container started from
/// <see cref="IAsyncLifetime"/> on the class is started once per test. That is what it was doing:
/// the CosmosDB emulator, which takes minutes to become ready, was started nine times for nine
/// tests and two of them timed out waiting. A class fixture is built once and shared, which is what
/// this is for.
/// </para>
/// <para>
/// A failure to start is recorded rather than thrown, so the tests can skip with the reason instead
/// of the whole class erroring on a machine with no container runtime.
/// </para>
/// </remarks>
/// <typeparam name="TContainer">The container this fixture owns.</typeparam>
public abstract class ContainerFixture<TContainer> : IAsyncLifetime
    where TContainer : IContainer
{
    /// <summary>The container, once it has started.</summary>
    protected TContainer? Container { get; private set; }

    /// <summary>Why the container is not usable, or <c>null</c> when it is.</summary>
    public string? Unavailable { get; private set; }

    /// <summary>Builds the container. Called once.</summary>
    protected abstract TContainer Build();

    /// <summary>Anything that has to happen after the container is up, such as creating a schema.</summary>
    protected virtual Task OnStartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async ValueTask InitializeAsync()
    {
        try
        {
            Container = Build();
            await Container.StartAsync();
            await OnStartedAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Unavailable = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Container is not null)
        {
            await Container.DisposeAsync();
        }
    }
}

/// <summary>One Redis for the Redis tests.</summary>
public sealed class RedisFixture : ContainerFixture<RedisContainer>
{
    protected override RedisContainer Build() => new RedisBuilder("redis:7-alpine").Build();

    /// <summary>The connection string, once the container is up.</summary>
    public string ConnectionString => Container!.GetConnectionString();
}

/// <summary>One PostgreSQL for the PostgreSQL tests.</summary>
public sealed class PostgresFixture : ContainerFixture<PostgreSqlContainer>
{
    protected override PostgreSqlContainer Build() => new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>The connection string, once the container is up.</summary>
    public string ConnectionString => Container!.GetConnectionString();
}

/// <summary>One SQL Server for the SQL Server tests.</summary>
public sealed class SqlServerFixture : ContainerFixture<MsSqlContainer>
{
    protected override MsSqlContainer Build() =>
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    /// <summary>The connection string, once the container is up.</summary>
    public string ConnectionString => Container!.GetConnectionString();
}

/// <summary>
/// One CosmosDB emulator for the CosmosDB tests, which is the one that most needs sharing: it takes
/// minutes to become ready.
/// </summary>
public sealed class CosmosDbFixture : ContainerFixture<CosmosDbContainer>
{
    protected override CosmosDbContainer Build() =>
        new CosmosDbBuilder("mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview").Build();

    /// <summary>The connection string, once the container is up.</summary>
    public string ConnectionString => Container!.GetConnectionString();

    /// <summary>The handler that accepts the emulator's self-signed certificate.</summary>
    public HttpMessageHandler HttpMessageHandler => Container!.HttpMessageHandler;
}
