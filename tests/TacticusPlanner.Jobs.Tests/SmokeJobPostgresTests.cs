using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TacticusPlanner.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace TacticusPlanner.Jobs.Tests;

/// <summary>The jobs container's real registrations against PostgreSQL. Requires Docker.</summary>
public sealed class SmokeJobPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public async ValueTask InitializeAsync() => await container.StartAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await container.DisposeAsync();

    [Fact]
    public async Task RefusesToRunBeforeTheApiHasMigratedTheDatabase()
    {
        await using var services = CreateServices();

        var exitCode = await JobRunner.RunAsync(
            services, ["smoke"], NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Equal(JobRunner.MigrationsPending, exitCode);
    }

    [Fact]
    public async Task SucceedsAgainstAMigratedDatabase()
    {
        await using var services = CreateServices();
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
            await db.Database.GetService<IMigrator>().MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);
        }

        var exitCode = await JobRunner.RunAsync(
            services, ["smoke"], NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Equal(JobRunner.Succeeded, exitCode);
    }

    private ServiceProvider CreateServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:planner-db"] = container.GetConnectionString(),
            })
            .Build();
        return new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddPlannerDatabase(configuration)
            .AddBatchJobs()
            .BuildServiceProvider();
    }
}
