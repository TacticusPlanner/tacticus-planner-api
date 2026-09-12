using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Guilds;

/// <summary>
/// Resolves the caller's registered, synchronized, token-ready guild for the Guild Raid status endpoints, shared
/// between the read and forced-refresh endpoints so both apply the same prerequisite checks.
/// </summary>
internal static class ReadyGuildResolver
{
    public static async Task<ReadyGuildResult> ResolveAsync(PlannerDbContext db, ProfileId profileId, CancellationToken ct)
    {
        var profileExists = await db.Profiles.AsNoTracking().AnyAsync(entity => entity.Id == profileId, ct);
        if (!profileExists)
        {
            return new ReadyGuildResult.ProfileNotFound();
        }

        var guild = await db.GuildMembers.AsNoTracking()
            .Where(member => member.ProfileId == profileId)
            .Select(member => member.Guild)
            .FirstOrDefaultAsync(ct);
        if (guild is null)
        {
            return new ReadyGuildResult.NotReady("No linked registered guild is available.");
        }
        if (guild.LastSyncSucceededAt is null)
        {
            return new ReadyGuildResult.NotReady("The linked guild has never completed synchronization.");
        }
        if (string.IsNullOrWhiteSpace(guild.GuildApiToken))
        {
            return new ReadyGuildResult.NotReady("No usable Guild API token is stored for the linked guild.");
        }

        return new ReadyGuildResult.Ready(guild);
    }
}

internal abstract record ReadyGuildResult
{
    public sealed record Ready(Guild Guild) : ReadyGuildResult;
    public sealed record NotReady(string Message) : ReadyGuildResult;
    public sealed record ProfileNotFound : ReadyGuildResult;
}
