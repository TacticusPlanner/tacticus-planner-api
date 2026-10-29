using Microsoft.EntityFrameworkCore;
using Npgsql;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Encryption;

namespace TacticusPlanner.Api.Features.TacticusIntegration;

/// <summary>
/// The one way a profile's Tacticus user id is set, shared by <c>PUT account/tacticus-integration</c> and the
/// V1 import so both normalize, hash and unlink identically.
/// </summary>
internal static class TacticusUserIdAssignment
{
    /// <summary>409 issue code (and V1 import part code) for an id another profile already claims.</summary>
    public const string ConflictIssueCode = "tacticusUserIdInUse";

    private const string ProfileUniqueIndex = "ix_profiles_tacticus_user_id_hash";

    /// <summary>The id in the canonical form the guild roster is hashed in (<see cref="Guid.ToString()"/>),
    /// or null when it is not a GUID. Canonicalizing keeps one Tacticus user from being claimed twice under
    /// differently formatted spellings, and keeps the hash matching the roster's.</summary>
    public static string? Normalize(string value) =>
        Guid.TryParse(value.Trim(), out var id) ? id.ToString() : null;

    /// <summary>
    /// Sets (or, with null, clears) the tracked profile's id and hash. When that changes the id, the profile's
    /// guild-member link — and the copies derived from it — are severed in the same unit of work, so a link made
    /// under the previous id can no longer authorize anything; the next guild sync relinks it from the new id.
    /// </summary>
    public static async Task AssignAsync(
        PlannerDbContext db,
        IColumnHashService hashService,
        Profile profile,
        string? canonicalId,
        CancellationToken ct)
    {
        var hash = canonicalId is null ? null : TacticusUserIdHash.FromNullable(hashService.ComputeHash(canonicalId));
        if (hash != profile.TacticusUserIdHash)
        {
            foreach (var member in await db.GuildMembers.Where(entity => entity.ProfileId == profile.Id).ToListAsync(ct))
            {
                member.ProfileId = null;
                member.LinkedPlayerName = null;
                member.LastActiveInPlannerOn = null;
            }
        }

        profile.TacticusUserId = canonicalId is null ? null : TacticusUserId.From(canonicalId);
        profile.TacticusUserIdHash = hash;
    }

    public static bool IsConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ProfileUniqueIndex,
        };
}
