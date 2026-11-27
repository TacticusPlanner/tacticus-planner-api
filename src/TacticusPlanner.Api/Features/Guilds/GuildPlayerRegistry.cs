using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Guilds;

/// <summary>
/// Adds the players a roster or raid save has seen to <c>guild_players</c>. Idempotent and safe against concurrent
/// saves: a known player is left as is, except that a missing id suffix is filled.
/// </summary>
public sealed class GuildPlayerRegistry(PlannerDbContext db, TimeProvider timeProvider)
{
    public async Task AddAsync(GuildId guildId, IEnumerable<(TacticusUserIdHash Hash, string? Suffix)> players, CancellationToken ct)
    {
        var distinct = players
            .GroupBy(player => Convert.ToHexString(player.Hash.Value), StringComparer.Ordinal)
            .Select(group => (group.First().Hash, Suffix: group.Select(player => player.Suffix).FirstOrDefault(suffix => suffix is not null)))
            .ToList();
        if (distinct.Count == 0)
        {
            return;
        }

        var ids = distinct.Select(_ => Guid.CreateVersion7()).ToArray();
        var hashes = distinct.Select(player => player.Hash.Value).ToArray();
        var suffixes = distinct.Select(player => player.Suffix).ToArray();
        var now = timeProvider.GetUtcNow();
        var guild = guildId.Value;
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO guild_players (id, revision, guild_id, tacticus_user_id_hash, tacticus_user_id_suffix, created_at, updated_at)
            SELECT seen.id, 0, {guild}, seen.hash, seen.suffix, {now}, {now}
            FROM unnest({ids}, {hashes}, {suffixes}) AS seen(id, hash, suffix)
            ON CONFLICT (guild_id, tacticus_user_id_hash) DO UPDATE
                SET tacticus_user_id_suffix = excluded.tacticus_user_id_suffix
                WHERE guild_players.tacticus_user_id_suffix IS NULL AND excluded.tacticus_user_id_suffix IS NOT NULL
            """,
            ct);
    }

    /// <summary>Every current member of the guild, with the suffix of their Tacticus user id.</summary>
    public Task AddMembersAsync(Guild guild, CancellationToken ct) =>
        AddAsync(
            guild.Id,
            guild.Members
                .Where(member => member.TacticusUserIdHash is not null)
                .Select(member => (member.TacticusUserIdHash!.Value, (string?)GuildRaidEntryNormalizer.UserIdSuffixOf(member.TacticusUserId.Value))),
            ct);
}
