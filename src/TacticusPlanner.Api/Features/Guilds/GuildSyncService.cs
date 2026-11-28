using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Refit;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.TacticusApi;

namespace TacticusPlanner.Api.Features.Guilds;

/// <summary>
/// Shared synchronization flow for both guild registration and explicit re-synchronization (Guild Phase 1).
/// Fetches the upstream guild, validates it, authorizes the caller from the FRESH response (never from
/// persisted roles), and upserts the <see cref="Guild"/> + <see cref="GuildMember"/> rows in one
/// transaction. Registration additionally persists the encrypted Guild API token; explicit sync never
/// replaces it (<paramref name="persistToken"/> in <see cref="SynchronizeAsync"/> distinguishes the two).
/// </summary>
public sealed partial class GuildSyncService(
    PlannerDbContext db,
    ITacticusApi tacticusApi,
    IColumnHashService hashService,
    GuildClaimEvaluator claimEvaluator,
    GuildPlayerRegistry playerRegistry,
    TimeProvider timeProvider,
    ILogger<GuildSyncService> logger
)
{
    /// <summary>Minimum spacing between upstream roster fetches through <c>POST guild/sync</c> for one guild,
    /// measured from <see cref="Guild.LastSyncAttemptedAt"/> — the shared guild token's quota is spent once per
    /// window however many of its Leader/Co-Leaders ask. Mirrors <see cref="GuildRaidStatusService.CooldownWindow"/>.</summary>
    public static readonly TimeSpan CooldownWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// A key needs the Guild Raid scope as well as the Guild one, but <c>GET guild</c> only proves the latter. Refuse
    /// to store a key that cannot read raids rather than letting every later refresh fail. A 404 is not about scope
    /// (the guild was just returned for this key), so it does not block registration.
    /// </summary>
    private async Task<GuildSyncResult?> CheckGuildRaidAccessAsync(string guildApiToken, CancellationToken ct)
    {
        try
        {
            await tacticusApi.GetGuildRaidsAsync(guildApiToken, ct);
            return null;
        }
        catch (ApiException exception) when ((int)exception.StatusCode is 404)
        {
            return null;
        }
        catch (ApiException exception) when ((int)exception.StatusCode is 400 or 401 or 403)
        {
            return new GuildSyncResult.UpstreamRejected(
                "This Guild API key cannot read Guild Raid data. Create a key with both the Guild and Guild Raid scopes."
            );
        }
        catch (Exception exception) when (exception is ApiExceptionBase or HttpRequestException or TaskCanceledException)
        {
            return new GuildSyncResult.UpstreamUnavailable(
                "The Tacticus API is currently unavailable. Try again shortly."
            );
        }
    }

    public async Task<GuildSyncResult> SynchronizeAsync(
        ProfileId callerProfileId,
        TacticusUserId callerTacticusUserId,
        string guildApiToken,
        bool persistToken,
        bool persistTokenOnlyIfNew = false,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(callerTacticusUserId.Value, out var callerTacticusGuid))
        {
            return new GuildSyncResult.InvalidRequest("The configured Tacticus User ID is not valid.");
        }

        var (upstream, fetchFailure) = await FetchGuildAsync(guildApiToken, ct);
        if (upstream is null)
        {
            return fetchFailure!;
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

        // The guild id is encrypted at rest (non-deterministically, per ADR 0005), so it can't be matched
        // by re-encrypting and comparing ciphertext — look up by the keyed hash instead, same as
        // Profile/GuildMember's Tacticus user id linking.
        var tacticusGuildIdHash = TacticusGuildIdHash.FromNullable(hashService.ComputeHash(upstream.GuildId.ToString()));

        var guild = await db.Guilds
            .Include(entity => entity.Members)
            .FirstOrDefaultAsync(entity => entity.TacticusGuildIdHash == tacticusGuildIdHash, ct);

        var isNewGuild = guild is null;
        guild ??= new Guild
        {
            Id = GuildId.From(Guid.CreateVersion7()),
            TacticusGuildId = TacticusGuildId.From(upstream.GuildId.ToString()),
            TacticusGuildIdHash = tacticusGuildIdHash,
            Tag = upstream.GuildTag,
            Name = upstream.Name,
        };

        var persistsToken = persistToken && (!persistTokenOnlyIfNew || isNewGuild);

        // A guild with a verified leader keeps its key until a verified leader replaces it. Judged on the members as
        // stored, with the roles of the fresh roster, before anything changes.
        if (persistsToken && !isNewGuild && IsKeyReplacementRefused(guild, upstream.Members, callerProfileId))
        {
            return new GuildSyncResult.VerificationRequired(
                "A confirmed Leader or Co-Leader must confirm you in Planner before you can replace this guild's key."
            );
        }

        if (persistsToken && await CheckGuildRaidAccessAsync(guildApiToken, ct) is { } accessFailure)
        {
            return accessFailure;
        }

        if (persistsToken)
        {
            guild.GuildApiToken = guildApiToken;
            guild.ConfiguredByProfileId = callerProfileId;
        }

        if (isNewGuild)
        {
            db.Guilds.Add(guild);
        }

        if (await SaveRosterAsync(guild, upstream, ct) is { } conflict)
        {
            return conflict;
        }

        var callerId = TacticusUserId.From(callerTacticusGuid.ToString());
        var callerMember = guild.Members.First(member => member.TacticusUserId == callerId);

        return new GuildSyncResult.Success(guild, callerMember, isNewGuild);
    }

    /// <summary>
    /// Refreshes a registered guild's roster with its stored key, without a caller: members, roles, links and claims as
    /// a sync saves them. Saves nothing when the key now answers for another guild. Never replaces the key.
    /// </summary>
    /// <returns>The failure, or <see langword="null"/> once saved.</returns>
    public async Task<GuildSyncResult?> RefreshRosterAsync(GuildId guildId, CancellationToken ct)
    {
        var guild = await db.Guilds.Include(entity => entity.Members).SingleOrDefaultAsync(entity => entity.Id == guildId, ct);
        if (guild?.GuildApiToken is not { } token)
        {
            return new GuildSyncResult.InvalidRequest("The guild has no stored Guild API token.");
        }

        var (upstream, fetchFailure) = await FetchGuildAsync(token, ct);
        if (upstream is null)
        {
            return fetchFailure;
        }

        if (TacticusGuildIdHash.FromNullable(hashService.ComputeHash(upstream.GuildId.ToString())) != guild.TacticusGuildIdHash)
        {
            return new GuildSyncResult.CallerNotAuthorized("The stored Guild API token now answers for another guild.");
        }

        return await SaveRosterAsync(guild, upstream, ct);
    }

    private async Task<(TacticusApi.Models.Guild.Guild? Guild, GuildSyncResult? Failure)> FetchGuildAsync(
        string guildApiToken, CancellationToken ct)
    {
        TacticusApi.Models.Guild.GuildResponse response;
        try
        {
            response = await tacticusApi.GetGuildAsync(guildApiToken, ct);
        }
        catch (ApiException exception) when ((int)exception.StatusCode is 400 or 401 or 403 or 404)
        {
            return (null, new GuildSyncResult.UpstreamRejected(
                "The Tacticus API could not fetch guild data for the supplied Guild API token."
            ));
        }
        catch (Exception exception) when (exception is ApiExceptionBase or HttpRequestException or TaskCanceledException)
        {
            return (null, new GuildSyncResult.UpstreamUnavailable(
                "The Tacticus API is currently unavailable. Try again shortly."
            ));
        }

        var upstream = response.Guild;
        if (upstream is null
            || upstream.GuildId == Guid.Empty
            || string.IsNullOrWhiteSpace(upstream.GuildTag)
            || string.IsNullOrWhiteSpace(upstream.Name)
            || upstream.Members.Count == 0)
        {
            return (null, new GuildSyncResult.InvalidUpstreamData("The Tacticus API returned an incomplete guild response."));
        }

        if (upstream.Members.Select(member => member.UserId).Distinct().Count() != upstream.Members.Count)
        {
            return (null, new GuildSyncResult.InvalidUpstreamData("The Tacticus API returned duplicate guild members."));
        }

        return (upstream, null);
    }

    private static bool IsKeyReplacementRefused(
        Guild guild, List<TacticusApi.Models.Guild.GuildMember> upstreamMembers, ProfileId callerProfileId)
    {
        var upstreamLeaders = upstreamMembers
            .Where(member => member.Role is TacticusApi.Models.Guild.GuildRole.LEADER or TacticusApi.Models.Guild.GuildRole.CO_LEADER)
            .Select(member => TacticusUserId.From(member.UserId.ToString()))
            .ToHashSet();
        var hasVerifiedLeader = guild.Members.Any(member => upstreamLeaders.Contains(member.TacticusUserId)
            && GuildClaimEvaluator.StateOf(member) == GuildClaimState.Verified);
        var callerVerified = guild.Members.Any(member => member.ProfileId == callerProfileId
            && GuildClaimEvaluator.StateOf(member) == GuildClaimState.Verified);
        return hasVerifiedLeader && !callerVerified;
    }

    /// <summary>Upserts the roster, re-evaluates claims, saves, and records the players it saw.</summary>
    /// <returns>A conflict, or <see langword="null"/> once saved.</returns>
    private async Task<GuildSyncResult?> SaveRosterAsync(
        Guild guild, TacticusApi.Models.Guild.Guild upstream, CancellationToken ct)
    {
        guild.Tag = upstream.GuildTag;
        guild.Name = upstream.Name;
        guild.Level = upstream.Level;

        await LinkAndUpsertMembersAsync(guild, upstream.Members, ct);
        await claimEvaluator.ApplyAsync(guild, ct);

        var now = timeProvider.GetUtcNow();
        guild.LastSyncAttemptedAt = now;
        guild.LastSyncSucceededAt = now;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Callers such as the V1 import keep saving through this context: drop the rejected changes so a
            // later SaveChanges does not replay them and fail the whole request.
            db.ChangeTracker.Clear();
            return new GuildSyncResult.Conflict("The guild changed while this update was being applied. Please retry.");
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return new GuildSyncResult.Conflict(
                "This guild or one of its members conflicts with an existing registration."
            );
        }

        // After the save, as a new guild must exist first. Idempotent, so a failure here only waits for the next save.
        try
        {
            await playerRegistry.AddMembersAsync(guild, ct);
        }
        catch (DbException exception)
        {
            LogPlayersNotRecorded(logger, exception, guild.Id);
        }

        return null;
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "The players of guild {GuildId}'s roster could not be recorded; the next roster save retries."
    )]
    private static partial void LogPlayersNotRecorded(ILogger logger, Exception exception, GuildId guildId);

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
            member => TacticusUserIdHash.FromNullable(hashService.ComputeHash(member.UserId.ToString()))
        );

        // Filtered by hash in SQL, so only profiles that match an upstream member are read.
        // IgnoreQueryFilters: linking deliberately spans every profile, not just the caller's — guild
        // membership is its own authorization boundary (see PlannerDbContext.ApplyProfileQueryFilters'
        // note on Guild/GuildMember), so the global profile query filter must not narrow this to the
        // caller alone.
        var memberHashes = hashesByUserId.Values.Where(hash => hash is not null).ToList();
        var linkableProfiles = await db.Profiles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(profile => memberHashes.Contains(profile.TacticusUserIdHash))
            .Select(profile => new
            {
                profile.Id,
                profile.TacticusUserIdHash,
                profile.DisplayName,
                profile.LastSeenAt,
            })
            .ToListAsync(ct);

        var profilesByHash = linkableProfiles
            .GroupBy(profile => profile.TacticusUserIdHash!.Value)
            .ToDictionary(group => group.Key, group => group.First());

        var linkedProfileIds = upstreamMembers
            .Select(member => hashesByUserId[member.UserId])
            .Where(hash => hash is not null)
            .Select(hash => hash!.Value)
            .Where(profilesByHash.ContainsKey)
            .Select(hash => profilesByHash[hash].Id)
            .Distinct()
            .ToList();

        // The linked player-data snapshot name takes precedence over the profile's display name — see
        // Guild Phase 1 spec's name-resolution rule. Only non-empty synced names are considered.
        // IgnoreQueryFilters: same reasoning as linkableProfiles above — these snapshots belong to other
        // linked members, not the caller.
        var snapshotNamesByProfileId = linkedProfileIds.Count == 0
            ? new Dictionary<ProfileId, string>()
            : await db.PlayerDataSnapshots
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(snapshot => linkedProfileIds.Contains(snapshot.Id))
                .Select(snapshot => new { snapshot.Id, snapshot.PlayerDetails.Name })
                .Where(snapshot => snapshot.Name != string.Empty)
                .ToDictionaryAsync(snapshot => snapshot.Id, snapshot => snapshot.Name, ct);

        // A Tacticus user is in one guild at a time, and this fresh roster proves where they are now. A link left
        // on another guild's roster (they moved, and that guild has not synced since) would break the
        // one-guild-per-profile index, so it is severed here; that guild's next sync drops the member row.
        if (linkedProfileIds.Count > 0)
        {
            var linkedIds = linkedProfileIds.Select(id => (ProfileId?)id).ToList();
            var staleLinks = await db.GuildMembers
                .Where(member => member.GuildId != guild.Id && linkedIds.Contains(member.ProfileId))
                .ToListAsync(ct);
            foreach (var staleLink in staleLinks)
            {
                staleLink.ProfileId = null;
                staleLink.LinkedPlayerName = null;
                staleLink.LastActiveInPlannerOn = null;
                GuildClaimEvaluator.ClearVerification(staleLink);
            }
        }

        var existingByUserId = guild.Members.ToDictionary(member => member.TacticusUserId);
        var upstreamUserIds = new HashSet<TacticusUserId>(
            upstreamMembers.Select(member => TacticusUserId.From(member.UserId.ToString()))
        );
        var now = timeProvider.GetUtcNow();

        foreach (var upstreamMember in upstreamMembers)
        {
            var upstreamUserId = TacticusUserId.From(upstreamMember.UserId.ToString());
            var hash = hashesByUserId[upstreamMember.UserId];
            var linkedProfile = hash is not null && profilesByHash.TryGetValue(hash.Value, out var profile)
                ? profile
                : null;

            // Unlinked members have no name to show — the upstream guild response never contains one.
            var linkedName = linkedProfile is null
                ? null
                : snapshotNamesByProfileId.GetValueOrDefault(linkedProfile.Id, linkedProfile.DisplayName) is { Length: > 0 } name
                    ? name
                    : null; // unconfirmed profiles have no public name — never fall back to a provider value

            if (!existingByUserId.TryGetValue(upstreamUserId, out var member))
            {
                member = new GuildMember
                {
                    Id = GuildMemberId.From(Guid.CreateVersion7()),
                    GuildId = guild.Id,
                    TacticusUserId = upstreamUserId,
                };
                guild.Members.Add(member);
            }

            member.TacticusUserIdHash = hash;
            member.ProfileId = linkedProfile?.Id;
            member.Role = MapRole(upstreamMember.Role);
            member.Level = upstreamMember.Level;
            member.LastActiveInGameOn = upstreamMember.LastActivityOn;
            member.LastActiveInPlannerOn = linkedProfile?.LastSeenAt;
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
    public sealed record Success(Guild Guild, GuildMember CallerMember, bool WasCreated) : GuildSyncResult;

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

    /// <summary>Only a verified Leader or Co-Leader may replace the key of a guild that has one — 403
    /// <c>guildVerificationRequired</c>.</summary>
    public sealed record VerificationRequired(string Message) : GuildSyncResult
    {
        public const string IssueCode = "guildVerificationRequired";
    }
}
