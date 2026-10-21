using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(TacticusPlanner.Api.IntegrationTests.PostgresAssemblyFixture))]

namespace TacticusPlanner.Api.IntegrationTests;

public sealed class PostgresAssemblyFixture : IAsyncLifetime
{
    private readonly Func<PostgreSqlContainer> buildContainer;
    private PostgreSqlContainer postgres = null!;

    public PostgresAssemblyFixture() : this(() => new PostgreSqlBuilder("postgres:18-alpine").Build()) { }

    internal PostgresAssemblyFixture(Func<PostgreSqlContainer> buildContainer)
    {
        this.buildContainer = buildContainer;
    }

    public static PostgresAssemblyFixture Shared { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        try
        {
            postgres = buildContainer();
            await postgres.StartAsync(TestContext.Current.CancellationToken);
            Shared = this;
        }
        catch (Exception exception)
        {
            if (postgres is not null)
                await postgres.DisposeAsync();
            throw new InvalidOperationException(
                "API integration tests require Docker. Start Docker Desktop (Linux containers), verify `docker info` succeeds, then rerun dotnet test tests/TacticusPlanner.Api.IntegrationTests.",
                exception);
        }
    }

    public DatabaseLease CreateDatabase()
    {
        var name = $"api_tests_{Guid.NewGuid():N}";
        using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE {name}";
        command.ExecuteNonQuery();
        return new DatabaseLease(postgres.GetConnectionString(), name);
    }

    public ValueTask DisposeAsync() => postgres.DisposeAsync();

    public sealed class DatabaseLease(string adminConnectionString, string name) : IDisposable
    {
        public string ConnectionString { get; } = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = name,
        }.ConnectionString;

        public void Dispose()
        {
            // Clear only this factory's pool; parallel factories retain their own connections.
            using var ownedConnection = new NpgsqlConnection(ConnectionString);
            NpgsqlConnection.ClearPool(ownedConnection);
            using var admin = new NpgsqlConnection(adminConnectionString);
            admin.Open();
            using var command = admin.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS {name} WITH (FORCE)";
            command.ExecuteNonQuery();
        }
    }
}
