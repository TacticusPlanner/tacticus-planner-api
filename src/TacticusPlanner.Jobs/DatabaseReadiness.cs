using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Jobs;

/// <summary>Whether the database is at the schema this image was built for. Only the API applies migrations.</summary>
public interface IDatabaseReadiness
{
    Task<IReadOnlyList<string>> PendingMigrationsAsync(CancellationToken cancellationToken);
}

internal sealed class EfDatabaseReadiness(PlannerDbContext db) : IDatabaseReadiness
{
    public async Task<IReadOnlyList<string>> PendingMigrationsAsync(CancellationToken cancellationToken) =>
        [.. await db.Database.GetPendingMigrationsAsync(cancellationToken)];
}
