using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.PlayerData;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Guilds;

/// <summary>
/// Re-evaluates the claims of the syncing profile's guild after a player sync, as the sync may have brought the
/// Tacticus name that matches an assigned one. Runs behind the sync's savepoint, so a failure never fails the sync.
/// </summary>
public sealed class GuildClaimSyncStep(PlannerDbContext db, GuildClaimEvaluator evaluator) : IPlayerDataSyncDefaultStep
{
    public async Task ApplyAsync(PlayerDataSyncWrite write, CancellationToken ct)
    {
        var guildId = await db.GuildMembers.AsNoTracking()
            .Where(member => member.ProfileId == write.ProfileId)
            .Select(member => (GuildId?)member.GuildId)
            .SingleOrDefaultAsync(ct);
        if (guildId is null)
        {
            return;
        }

        var guild = await db.Guilds.Include(entity => entity.Members).SingleAsync(entity => entity.Id == guildId, ct);
        await evaluator.ApplyAsync(guild, ct);
    }

    public void Discard()
    {
        foreach (var entry in db.ChangeTracker.Entries()
                     .Where(entry => entry.Entity is GuildMember or Guild)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
