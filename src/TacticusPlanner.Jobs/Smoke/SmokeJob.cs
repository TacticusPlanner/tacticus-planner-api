using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Jobs.Smoke;

/// <summary>
/// Proves the jobs container end to end (image, identity, Key Vault connection string, network path to PostgreSQL,
/// telemetry) without changing anything: it reads the applied migration count and the number of profiles.
/// </summary>
public sealed partial class SmokeJob(PlannerDbContext db, ILogger<SmokeJob> logger) : IBatchJob
{
    public const string Name = "smoke";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var migrations = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).Count();
        var profiles = await db.Profiles.IgnoreQueryFilters().CountAsync(cancellationToken);
        LogDatabaseReachable(logger, migrations, profiles);
    }

    [LoggerMessage(
        EventId = 100,
        Level = LogLevel.Information,
        Message = "Smoke: database reachable, {Migrations} migrations applied, {Profiles} profiles."
    )]
    private static partial void LogDatabaseReachable(ILogger logger, int migrations, int profiles);
}
