using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Encryption;

namespace TacticusPlanner.Api;

/// <summary>
/// Applies every pending EF Core migration, then the idempotent <see cref="ColumnEncryptionBackfill"/>, before
/// the web server begins accepting requests.
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
        var rewritten = await ColumnEncryptionBackfill.RunAsync(
            db,
            scope.ServiceProvider.GetRequiredService<IColumnHashService>(),
            cancellationToken);
        if (rewritten > 0)
        {
            ColumnEncryptionBackfilled(logger, rewritten);
        }

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

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Information,
        Message = "Re-encrypted and re-hashed {RowCount} rows still on the legacy column-encryption envelope."
    )]
    private static partial void ColumnEncryptionBackfilled(ILogger logger, int rowCount);
}
