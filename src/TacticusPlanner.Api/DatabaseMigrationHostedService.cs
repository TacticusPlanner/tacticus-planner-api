using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api;

/// <summary>
/// Applies every pending EF Core migration before the web server begins accepting requests.
/// </summary>
public sealed partial class DatabaseMigrationHostedService(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    ILogger<DatabaseMigrationHostedService> logger
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ApplyingDatabaseMigrations(logger, environment.EnvironmentName);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        DatabaseMigrationsApplied(logger, environment.EnvironmentName);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Applying database migrations for {EnvironmentName}."
    )]
    private static partial void ApplyingDatabaseMigrations(ILogger logger, string environmentName);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Database migrations applied for {EnvironmentName}."
    )]
    private static partial void DatabaseMigrationsApplied(ILogger logger, string environmentName);
}
