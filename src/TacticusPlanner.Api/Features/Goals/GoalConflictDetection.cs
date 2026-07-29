using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Detects a violation of the "at most one Active/Paused goal per (profile, entity, goal type)" unique
/// index (see <c>GoalConfiguration.ix_goals_one_active_or_paused_per_entity_and_type</c>) so a concurrent
/// race that slips past the handler's read-before-write pre-check surfaces as the documented 400 instead
/// of an unhandled 500.
/// </summary>
internal static class GoalConflictDetection
{
    private const string ActiveOrPausedIndexName = "ix_goals_one_active_or_paused_per_entity_and_type";

    public static bool IsActiveOrPausedConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
            && postgres.ConstraintName == ActiveOrPausedIndexName;
}
