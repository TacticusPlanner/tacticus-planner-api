using Microsoft.EntityFrameworkCore;
using Refit;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.Persistence.Users;
using TacticusPlanner.Persistence.Users.Guilds;
using TacticusPlanner.TacticusApi;

namespace TacticusPlanner.Api.Features.Guilds;

/// <summary>
/// Shared synchronization flow for both guild registration and explicit re-synchronization (Guild Phase 1).
/// Fetches the upstream guild, validates it, authorizes the caller from the FRESH response (never from
/// persisted roles), and upserts the <see cref="Guild"/> + <see cref="GuildMember"/> rows in one
/// transaction. Registration additionally persists the encrypted Guild API token; explicit sync never
/// replaces it (<paramref name="persistToken"/> in <see cref="SynchronizeAsync"/> distinguishes the two).
/// </summary>
public sealed class GuildSyncService(
    PlannerDbContext db,
    ITacticusApi tacticusApi,
    IColumnHashService hashService,
    TimeProvider timeProvider
)
{
    public async Task<GuildSyncResult> SynchronizeAsync(
        ProfileId callerProfileId,
        string callerTacticusUserId,
        string guildApiToken,
        bool persistToken,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(callerTacticusUserId, out var callerTacticusGuid))
        {
            return new GuildSyncResult.InvalidRequest("The configured Tacticus User ID is not valid.");
        }

        TacticusApi.Models.Guild.GuildResponse response;
        try
        {
            response = await tacticusApi.GetGuildAsync(guildApiToken, ct);
        }
        catch (ApiException exception) when ((int)exception.StatusCode is 400 or 401 or 403 or 404)
        {
            return new GuildSyncResult.UpstreamRejected(
                "The Tacticus API could not fetch guild data for the supplied Guild API token."
            );
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException or TaskCanceledException)
        {
            return new GuildSyncResult.UpstreamUnavailable(
                "The Tacticus API is currently unavailable. Try again shortly."
            );
        }

        var upstream = response.Guild;
        if (upstream is null
            || upstream.GuildId == Guid.Empty
            || string.IsNullOrWhiteSpace(upstream.GuildTag)
            || string.IsNullOrWhiteSpace(upstream.Name)
            || upstream.Members.Count == 0)
        {
            return new GuildSyncResult.InvalidUpstreamData("The Tacticus API returned an incomplete guild response.");
        }

        if (upstream.Members.Select(member => member.UserId).Distinct().Count() != upstream.Members.Count)
        {
            return new GuildSyncResult.InvalidUpstreamData("The Tacticus API returned duplicate guild members.");
        }

        var callerUpstreamMember = upstream.Members.FirstOrDefault(member => member.UserId == callerTacticusGuid);
        if (callerUpstreamMember is null
            || callerUpstreamMember.Role
                is not (TacticusApi.Models.Guild.GuildRole.LEADER or TacticusApi.Models.Guild.GuildRole.CO_LEADER))
        {
            return new GuildSyncResult.CallerNotAuthorized(
                "Only the guild's current Leader or Co-Leader can register or synchronize it."
            );
        }

        var guild = await db.Guilds
            .Include(entity => entity.Members)
            .FirstOrDefaultAsync(entity => entity.TacticusGuildId == upstream.GuildId, ct);

        var isNewGuild = guild is null;
        guild ??= new Guild
        {
            Id = GuildId.From(Guid.CreateVersion7()),
            TacticusGuildId = upstream.GuildId,
            Tag = upstream.GuildTag,
            Name = upstream.Name,
        };

        guild.Tag = upstream.GuildTag;
        guild.Name = upstream.Name;
        guild.Level = upstream.Level;

        if (persistToken)
        {
            guild.GuildApiToken = guildApiToken;
            guild.ConfiguredByProfileId = callerProfileId;
        }

        if (isNewGuild)
        {
            db.Guilds.Add(guild);
        }

        await LinkAndUpsertMembersAsync(guild, upstream.Members, ct);

        var now = timeProvider.GetUtcNow();
        guild.LastSyncAttemptedAt = now;
        guild.LastSyncSucceededAt = now;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new GuildSyncResult.Conflict("The guild changed while this update was being applied. Please retry.");
        }
        catch (DbUpdateException)
        {
            return new GuildSyncResult.Conflict(
                "This guild or one of its members conflicts with an existing registration."
            );
        }

        var callerMember = guild.Members.First(member => member.TacticusUserId == callerTacticusGuid);

        return new GuildSyncResult.Success(guild, callerMember);
    }

    private async Task LinkAndUpsertMembersAsync(
        Guild guild,
        List<TacticusApi.Models.Guild.GuildMember> upstreamMembers,
        CancellationToken ct
    )
    {
        // Every upstream member's Tacticus user id is hashed with the same keyed HMAC as
        // Profile.TacticusUserIdHash, so linking never requires decrypting either side.
        var hashesByUserId = upstreamMembers.ToDictionary(
            member => member.UserId,
            member => hashService.ComputeHash(member.UserId.ToString())
        );

        // Loaded once per sync and matched in memory: byte[] equality does not translate cleanly to SQL,
        // and Planner's total linkable-profile count is modest, so this avoids one query per member.
        var linkableProfiles = await db.Profiles
            .AsNoTracking()
            .Where(profile => profile.TacticusUserIdHash != null)
            .Select(profile => new { profile.Id, profile.TacticusUserIdHash, profile.DisplayName })
            .ToListAsync(ct);

        var profilesByHash = linkableProfiles
            .GroupBy(profile => Convert.ToHexString(profile.TacticusUserIdHash!))
            .ToDictionary(group => group.Key, group => group.First());

        var linkedProfileIds = upstreamMembers
            .Select(member => hashesByUserId[member.UserId])
            .Where(hash => hash is not null)
            .Select(hash => Convert.ToHexString(hash!))
            .Where(profilesByHash.ContainsKey)
            .Select(key => profilesByHash[key].Id)
            .Distinct()
            .ToList();

        // The linked player-data snapshot name takes precedence over the profile's display name — see
        // Guild Phase 1 spec's name-resolution rule. Only non-empty synced names are considered.
        var snapshotNamesByProfileId = linkedProfileIds.Count == 0
            ? new Dictionary<ProfileId, string>()
            : await db.PlayerDataSnapshots
                .AsNoTracking()
                .Where(snapshot => linkedProfileIds.Contains(snapshot.Id))
                .Select(snapshot => new { snapshot.Id, snapshot.PlayerDetails.Name })
                .Where(snapshot => snapshot.Name != string.Empty)
                .ToDictionaryAsync(snapshot => snapshot.Id, snapshot => snapshot.Name, ct);

        var existingByUserId = guild.Members.ToDictionary(member => member.TacticusUserId);
        var upstreamUserIds = new HashSet<Guid>(upstreamMembers.Select(member => member.UserId));
        var now = timeProvider.GetUtcNow();

        foreach (var upstreamMember in upstreamMembers)
        {
            var hash = hashesByUserId[upstreamMember.UserId];
            var hashKey = hash is null ? null : Convert.ToHexString(hash);
            var linkedProfile = hashKey is not null && profilesByHash.TryGetValue(hashKey, out var profile)
                ? profile
                : null;

            // Unlinked members have no name to show — the upstream guild response never contains one.
            var linkedName = linkedProfile is null
                ? null
                : snapshotNamesByProfileId.GetValueOrDefault(linkedProfile.Id, linkedProfile.DisplayName);

            if (!existingByUserId.TryGetValue(upstreamMember.UserId, out var member))
            {
                member = new GuildMember
                {
                    Id = GuildMemberId.From(Guid.CreateVersion7()),
                    GuildId = guild.Id,
                    TacticusUserId = upstreamMember.UserId,
                };
                guild.Members.Add(member);
            }

            member.TacticusUserIdHash = hash;
            member.ProfileId = linkedProfile?.Id;
            member.Role = MapRole(upstreamMember.Role);
            member.Level = upstreamMember.Level;
            member.LastActivityOn = upstreamMember.LastActivityOn;
            member.LinkedPlayerName = linkedName;
            member.LastSyncedAt = now;
        }

        // Members absent from the latest successful upstream response are deleted, not retained — the
        // upstream roster is treated as the authoritative current-membership snapshot.
        foreach (var departedMember in guild.Members.Where(member => !upstreamUserIds.Contains(member.TacticusUserId)).ToList())
        {
            guild.Members.Remove(departedMember);
            db.Remove(departedMember);
        }
    }

    private static GuildRole MapRole(TacticusApi.Models.Guild.GuildRole role)
    {
        return role switch
        {
            TacticusApi.Models.Guild.GuildRole.LEADER => GuildRole.Leader,
            TacticusApi.Models.Guild.GuildRole.CO_LEADER => GuildRole.CoLeader,
            TacticusApi.Models.Guild.GuildRole.OFFICER => GuildRole.Officer,
            TacticusApi.Models.Guild.GuildRole.MEMBER => GuildRole.Member,
            _ => GuildRole.Member,
        };
    }
}

/// <summary>Outcome of <see cref="GuildSyncService.SynchronizeAsync"/>; endpoints map each case to the
/// HTTP status the Guild Phase 1 spec calls for instead of relying on exceptions for control flow.</summary>
public abstract record GuildSyncResult
{
    public sealed record Success(Guild Guild, GuildMember CallerMember) : GuildSyncResult;

    /// <summary>The caller's Tacticus User ID is missing/malformed — 400.</summary>
    public sealed record InvalidRequest(string Message) : GuildSyncResult;

    /// <summary>The Tacticus API rejected the token outright (bad/expired/wrong scope) — 400.</summary>
    public sealed record UpstreamRejected(string Message) : GuildSyncResult;

    /// <summary>The Tacticus API could not be reached — 502/503.</summary>
    public sealed record UpstreamUnavailable(string Message) : GuildSyncResult;

    /// <summary>The upstream response was malformed (missing identity, duplicate members) — 400.</summary>
    public sealed record InvalidUpstreamData(string Message) : GuildSyncResult;

    /// <summary>The caller is absent from the fresh roster, or below Co-Leader — 403.</summary>
    public sealed record CallerNotAuthorized(string Message) : GuildSyncResult;

    /// <summary>A database uniqueness/concurrency conflict — 409.</summary>
    public sealed record Conflict(string Message) : GuildSyncResult;
}
