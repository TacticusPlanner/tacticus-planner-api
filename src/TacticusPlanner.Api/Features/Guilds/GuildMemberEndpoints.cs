using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.TacticusIntegration;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Encryption;

namespace TacticusPlanner.Api.Features.Guilds;

/// <summary>
/// Shared checks of the claim and player endpoints (<c>guild-member-verification</c>): only a verified Leader or
/// Co-Leader of the caller's guild gets past them, with the guild and its whole roster tracked.
/// </summary>
internal static class GuildManagerAccess
{
    public const string ClaimChangedIssueCode = "guildClaimChanged";
    public const string NotLinkedIssueCode = "guildNotLinked";
    public const string ConcurrencyIssueCode = "concurrency";
    public const string ForbiddenIssueCode = "forbidden";

    public static async Task<(Guild Guild, GuildMember Caller)?> ResolveAsync(
        IEndpoint endpoint, PlannerDbContext db, ProfileId? profileId, CancellationToken ct)
    {
        if (profileId is not { } callerId || !await db.Profiles.AnyAsync(profile => profile.Id == callerId, ct))
        {
            await endpoint.SendNotFoundProblemAsync(ct);
            return null;
        }

        var guildId = await db.GuildMembers.AsNoTracking()
            .Where(member => member.ProfileId == callerId)
            .Select(member => (GuildId?)member.GuildId)
            .SingleOrDefaultAsync(ct);
        if (guildId is null)
        {
            await endpoint.SendProblemAsync(
                StatusCodes.Status409Conflict, ct, "No registered guild is linked to this profile.", NotLinkedIssueCode);
            return null;
        }

        var guild = await db.Guilds.Include(entity => entity.Members).SingleAsync(entity => entity.Id == guildId, ct);
        var caller = guild.Members.First(member => member.ProfileId == callerId);
        if (!GuildClaimEvaluator.IsLeader(caller.Role) || GuildClaimEvaluator.StateOf(caller) != GuildClaimState.Verified)
        {
            await endpoint.SendProblemAsync(
                StatusCodes.Status403Forbidden,
                ct,
                "Only a confirmed Leader or Co-Leader can manage the guild's players and claims.",
                GuildSyncResult.VerificationRequired.IssueCode);
            return null;
        }

        return (guild, caller);
    }

    /// <summary>
    /// What a leader echoes to decide on a claim, so the decision cannot land on another account. A keyed hash of
    /// member and profile, meaningless outside its guild and not a profile id.
    /// </summary>
    public static string ClaimId(IColumnHashService hashService, GuildMember member) =>
        Convert.ToHexString(hashService.ComputeHash($"guild-claim:{member.Id.Value}:{member.ProfileId!.Value.Value}")!)[..32]
            .ToLowerInvariant();

    public static bool CanReject(GuildMember member, GuildMember caller)
    {
        var state = GuildClaimEvaluator.StateOf(member);
        return state is GuildClaimState.Pending or GuildClaimState.Verified
            && member.Id != caller.Id
            && !(state == GuildClaimState.Verified && GuildClaimEvaluator.IsLeader(member.Role) && caller.Role != GuildRole.Leader);
    }

    public static Guid? RouteGuid(IEndpoint endpoint, string name) =>
        Guid.TryParse(endpoint.HttpContext.GetRouteValue(name)?.ToString(), out var value) ? value : null;

    /// <summary>
    /// Verifies the claim, and gives the player the claimant's Tacticus name when it has none and the name is known.
    /// </summary>
    public static async Task ConfirmAsync(
        PlannerDbContext db, GuildClaimNames names, GuildMember member, DateTimeOffset now, CancellationToken ct)
    {
        GuildClaimEvaluator.Verify(member, GuildClaimVerificationMethod.Leader, now);
        if (names.AssignedNameOf(member) is null
            && names.TacticusNameOf(member.ProfileId!.Value) is { } tacticusName
            && member.TacticusUserIdHash is { } hash
            && await db.GuildPlayers.SingleOrDefaultAsync(
                player => player.GuildId == member.GuildId && player.TacticusUserIdHash == hash, ct) is { } player)
        {
            player.AssignedName = tacticusName.Trim()[..Math.Min(tacticusName.Trim().Length, GuildPlayerNames.MaxLength)];
        }
    }

    /// <summary>Saves, or answers 409 <c>concurrency</c> when another decision changed the guild meanwhile.</summary>
    public static async Task<bool> TrySaveAsync(IEndpoint endpoint, PlannerDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            await endpoint.SendProblemAsync(
                StatusCodes.Status409Conflict, ct, "The guild changed while this decision was saved. Reload and retry.",
                ConcurrencyIssueCode);
            return false;
        }
    }
}

internal static class GuildPlayerNames
{
    public const int MaxLength = 50;

    /// <returns>The trimmed name, <see langword="null"/> to clear it, or <c>false</c> in <paramref name="valid"/>.</returns>
    public static string? Normalize(string? name, out bool valid)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            valid = true;
            return null;
        }

        valid = trimmed.Length <= MaxLength && !trimmed.Any(char.IsControl);
        return trimmed;
    }
}

public sealed class GetGuildPlayersEndpoint : EndpointWithoutRequest<GuildPlayersResponse>
{
    public override void Configure()
    {
        Get("guild/players");
        Summary(summary =>
        {
            summary.Summary = "Lists the guild's players, current members first, with their claims.";
            summary.Description = "For a confirmed Leader or Co-Leader. Former members are the players seen in the "
                + "guild's stored raid hits who are no longer on the roster, newest hit first. Never calls the Tacticus "
                + "API and returns no profile id, display name, hash or full Tacticus user id.";
            summary.Response<GuildPlayersResponse>(StatusCodes.Status200OK, "The guild's players.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status403Forbidden,
                "guildVerificationRequired: the caller is not a confirmed Leader or Co-Leader.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated account/profile has not been provisioned.");
            summary.Response(StatusCodes.Status409Conflict, "guildNotLinked: no registered guild is linked to the caller.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<PlannerDbContext>();
        if (await GuildManagerAccess.ResolveAsync(this, db, ProcessorState<CurrentUserState>().ProfileId, ct) is not { } access)
        {
            return;
        }

        var (guild, caller) = access;

        // Seeding is idempotent; doing it here as well guarantees every current member has a player to name.
        await Resolve<GuildPlayerRegistry>().AddMembersAsync(guild, ct);
        var players = await db.GuildPlayers.AsNoTracking().Where(player => player.GuildId == guild.Id).ToListAsync(ct);
        var lastHits = await db.GuildRaidAttacks.AsNoTracking()
            .Where(attack => attack.Season!.GuildId == guild.Id)
            .GroupBy(attack => attack.TacticusUserIdHash)
            .Select(group => new { Hash = group.Key, LastHitAt = group.Max(attack => attack.CompletedAt) })
            .ToListAsync(ct);
        var lastHitByHash = lastHits.ToDictionary(hit => GuildClaimEvaluator.Key(hit.Hash), hit => hit.LastHitAt);
        var names = await Resolve<GuildClaimEvaluator>().LoadNamesAsync(guild.Id, guild.Members.ToList(), ct);
        var hashService = Resolve<IColumnHashService>();

        var playersByHash = players.ToDictionary(player => GuildClaimEvaluator.Key(player.TacticusUserIdHash));
        var current = GuildProjection.InRosterOrder(guild.Members)
            .Where(member => member.TacticusUserIdHash is { } hash && playersByHash.ContainsKey(GuildClaimEvaluator.Key(hash)))
            .Select(member =>
            {
                var key = GuildClaimEvaluator.Key(member.TacticusUserIdHash!.Value);
                return Summary(playersByHash[key], lastHitByHash, Membership(member, caller, names, hashService));
            })
            .ToList();
        var memberKeys = guild.Members
            .Where(member => member.TacticusUserIdHash is not null)
            .Select(member => GuildClaimEvaluator.Key(member.TacticusUserIdHash!.Value))
            .ToHashSet(StringComparer.Ordinal);
        var former = players
            .Where(player => !memberKeys.Contains(GuildClaimEvaluator.Key(player.TacticusUserIdHash)))
            .Select(player => Summary(player, lastHitByHash, null))
            .OrderByDescending(player => player.LastHitAt)
            .ThenBy(player => player.PlayerId);

        await Send.OkAsync(new GuildPlayersResponse([.. current, .. former]), ct);
    }

    private static GuildPlayerSummary Summary(
        GuildPlayer player, Dictionary<string, DateTimeOffset> lastHitByHash, GuildPlayerMembership? membership) =>
        new(
            player.Id.Value,
            player.TacticusUserIdSuffix is { } suffix ? SecretMasker.Mask("00000000" + suffix) : null,
            player.AssignedName,
            lastHitByHash.TryGetValue(GuildClaimEvaluator.Key(player.TacticusUserIdHash), out var lastHitAt) ? lastHitAt : null,
            membership);

    private static GuildPlayerMembership Membership(
        GuildMember member, GuildMember caller, GuildClaimNames names, IColumnHashService hashService)
    {
        var state = GuildProjection.ToStatus(GuildClaimEvaluator.StateOf(member));
        var claim = state is not { } status
            ? null
            : new GuildPlayerClaim(
                GuildManagerAccess.ClaimId(hashService, member),
                status,
                status == GuildClaimStatus.Verified ? ToMethod(member.VerificationMethod) : null,
                names.TacticusNameOf(member.ProfileId!.Value),
                names.MatchOf(member),
                member.Id == caller.Id,
                GuildManagerAccess.CanReject(member, caller));
        return new GuildPlayerMembership(member.Id.Value, member.Role, member.Level, claim);
    }

    private static GuildClaimMethod? ToMethod(GuildClaimVerificationMethod? method) => method switch
    {
        GuildClaimVerificationMethod.Key => GuildClaimMethod.Key,
        GuildClaimVerificationMethod.NameMatch => GuildClaimMethod.NameMatch,
        GuildClaimVerificationMethod.Leader => GuildClaimMethod.Leader,
        _ => null,
    };
}

public sealed class SetGuildPlayerNameEndpoint : Endpoint<SetGuildPlayerNameRequest>
{
    public override void Configure()
    {
        Put("guild/players/{playerId}/name");
        Summary(summary =>
        {
            summary.Summary = "Sets or clears the name a confirmed leader gives a guild player.";
            summary.Description = "The name is trimmed; blank or null clears it. Duplicates are allowed. A saved "
                + "name can verify a pending Officer or Member claim whose synced Tacticus name equals it.";
            summary.Response(StatusCodes.Status204NoContent, "The name was saved.");
            summary.Response(StatusCodes.Status400BadRequest, "A name over 50 characters or with control characters.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status403Forbidden,
                "guildVerificationRequired: the caller is not a confirmed Leader or Co-Leader.");
            summary.Response(StatusCodes.Status404NotFound, "The profile is not provisioned, or no such player is in the caller's guild.");
            summary.Response(StatusCodes.Status409Conflict, "guildNotLinked, or concurrency.");
        });
    }

    public override async Task HandleAsync(SetGuildPlayerNameRequest req, CancellationToken ct)
    {
        var db = Resolve<PlannerDbContext>();
        if (await GuildManagerAccess.ResolveAsync(this, db, ProcessorState<CurrentUserState>().ProfileId, ct) is not { } access)
        {
            return;
        }

        var (guild, _) = access;

        var name = GuildPlayerNames.Normalize(req.Name, out var valid);
        if (!valid)
        {
            AddError(request => request.Name!, "A name has 1 to 50 characters and no control characters.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var playerId = GuildManagerAccess.RouteGuid(this, "playerId");
        var player = playerId is null
            ? null
            : await db.GuildPlayers.SingleOrDefaultAsync(
                entity => entity.Id == GuildPlayerId.From(playerId.Value) && entity.GuildId == guild.Id, ct);
        if (player is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        player.AssignedName = name;
        await db.SaveChangesAsync(ct);
        await Resolve<GuildClaimEvaluator>().ApplyAsync(guild, ct);
        if (await GuildManagerAccess.TrySaveAsync(this, db, ct))
        {
            await Send.NoContentAsync(ct);
        }
    }
}

public sealed class ConfirmGuildClaimEndpoint : Endpoint<GuildClaimDecisionRequest>
{
    public override void Configure()
    {
        Post("guild/members/{guildMemberId}/confirm");
        Summary(summary =>
        {
            summary.Summary = "Confirms the claim on a guild member, whatever its state.";
            summary.Description = "Carries the profileClaimId the caller was shown. Gives the player the claimant's "
                + "synced Tacticus name when it has no name and that name is known.";
            summary.Response(StatusCodes.Status204NoContent, "The claim is verified.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status403Forbidden,
                "guildVerificationRequired, or forbidden for the caller's own claim.");
            summary.Response(StatusCodes.Status404NotFound, "The profile is not provisioned, or no such member is in the caller's guild.");
            summary.Response(StatusCodes.Status409Conflict,
                "guildClaimChanged: another account holds the claim now, or none does; guildNotLinked; or concurrency.");
        });
    }

    public override async Task HandleAsync(GuildClaimDecisionRequest req, CancellationToken ct)
    {
        var db = Resolve<PlannerDbContext>();
        if (await GuildClaimDecision.ResolveAsync(
                this, db, ProcessorState<CurrentUserState>().ProfileId, Resolve<IColumnHashService>(), req, ct)
            is not { } decision)
        {
            return;
        }

        var (guild, _, member) = decision;

        var names = await Resolve<GuildClaimEvaluator>().LoadNamesAsync(guild.Id, guild.Members.ToList(), ct);
        await GuildManagerAccess.ConfirmAsync(db, names, member, Resolve<TimeProvider>().GetUtcNow(), ct);
        if (await GuildManagerAccess.TrySaveAsync(this, db, ct))
        {
            await Send.NoContentAsync(ct);
        }
    }
}

public sealed class RejectGuildClaimEndpoint : Endpoint<GuildClaimDecisionRequest>
{
    public override void Configure()
    {
        Post("guild/members/{guildMemberId}/reject");
        Summary(summary =>
        {
            summary.Summary = "Rejects a pending or verified claim on a guild member.";
            summary.Description = "Ends any verification, removes the link, and clears the claimant's Tacticus user id "
                + "when it is still the member's, so another account can enter it. The same account claiming again "
                + "stays rejected until a leader confirms it.";
            summary.Response(StatusCodes.Status204NoContent, "The claim is rejected.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status403Forbidden,
                "guildVerificationRequired; or forbidden for the caller's own claim, or a Co-Leader rejecting a "
                    + "verified Leader or Co-Leader.");
            summary.Response(StatusCodes.Status404NotFound, "The profile is not provisioned, or no such member is in the caller's guild.");
            summary.Response(StatusCodes.Status409Conflict, "guildClaimChanged, guildNotLinked, or concurrency.");
        });
    }

    public override async Task HandleAsync(GuildClaimDecisionRequest req, CancellationToken ct)
    {
        var db = Resolve<PlannerDbContext>();
        if (await GuildClaimDecision.ResolveAsync(
                this, db, ProcessorState<CurrentUserState>().ProfileId, Resolve<IColumnHashService>(), req, ct)
            is not { } decision)
        {
            return;
        }

        var (_, caller, member) = decision;

        var state = GuildClaimEvaluator.StateOf(member);
        if (state == GuildClaimState.Rejected)
        {
            await Send.NoContentAsync(ct);
            return;
        }

        if (!GuildManagerAccess.CanReject(member, caller))
        {
            await this.SendProblemAsync(
                StatusCodes.Status403Forbidden, ct, "Only a confirmed Leader can reject a confirmed leader's claim.",
                GuildManagerAccess.ForbiddenIssueCode);
            return;
        }

        var claimantId = member.ProfileId!.Value;
        member.RejectedProfileId = claimantId;
        GuildClaimEvaluator.ClearVerification(member);

        // IgnoreQueryFilters: the claimant is another profile than the caller.
        var claimant = await db.Profiles.IgnoreQueryFilters().SingleOrDefaultAsync(profile => profile.Id == claimantId, ct);
        if (claimant is not null && claimant.TacticusUserIdHash == member.TacticusUserIdHash)
        {
            await TacticusUserIdAssignment.AssignAsync(db, Resolve<IColumnHashService>(), claimant, null, ct);
        }

        if (await GuildManagerAccess.TrySaveAsync(this, db, ct))
        {
            await Send.NoContentAsync(ct);
        }
    }
}

/// <summary>Checks shared by confirm and reject of one claim.</summary>
internal static class GuildClaimDecision
{
    public static async Task<(Guild Guild, GuildMember Caller, GuildMember Member)?> ResolveAsync(
        IEndpoint endpoint,
        PlannerDbContext db,
        ProfileId? profileId,
        IColumnHashService hashService,
        GuildClaimDecisionRequest req,
        CancellationToken ct)
    {
        if (await GuildManagerAccess.ResolveAsync(endpoint, db, profileId, ct) is not { } access)
        {
            return null;
        }

        var (guild, caller) = access;

        var memberId = GuildManagerAccess.RouteGuid(endpoint, "guildMemberId");
        var member = memberId is null ? null : guild.Members.FirstOrDefault(entity => entity.Id.Value == memberId);
        if (member is null)
        {
            await endpoint.SendNotFoundProblemAsync(ct);
            return null;
        }

        if (member.ProfileId is null
            || !string.Equals(GuildManagerAccess.ClaimId(hashService, member), req.ProfileClaimId, StringComparison.Ordinal))
        {
            await endpoint.SendProblemAsync(
                StatusCodes.Status409Conflict, ct, "The claim changed; nothing was decided. Reload and retry.",
                GuildManagerAccess.ClaimChangedIssueCode);
            return null;
        }

        if (member.Id == caller.Id)
        {
            await endpoint.SendProblemAsync(
                StatusCodes.Status403Forbidden, ct, "Nobody can decide on their own claim.", GuildManagerAccess.ForbiddenIssueCode);
            return null;
        }

        return (guild, caller, member);
    }
}

public sealed class ConfirmGuildClaimsEndpoint : Endpoint<ConfirmGuildClaimsRequest, ConfirmGuildClaimsResponse>
{
    public const int MaxClaims = 100;

    public override void Configure()
    {
        Post("guild/members/confirm");
        Summary(summary =>
        {
            summary.Summary = "Confirms several pending claims on players without an assigned name.";
            summary.Description = "Each claim is confirmed only while it is pending, still held by the account the "
                + "caller was shown, on a player without a name, and not the caller's own; every other one is skipped "
                + "and left unchanged. A confirmed player gets the claimant's synced Tacticus name when known.";
            summary.Response<ConfirmGuildClaimsResponse>(StatusCodes.Status200OK, "Which members were confirmed and skipped.");
            summary.Response(StatusCodes.Status400BadRequest, "No claims, or more than 100.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status403Forbidden,
                "guildVerificationRequired: the caller is not a confirmed Leader or Co-Leader.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated account/profile has not been provisioned.");
            summary.Response(StatusCodes.Status409Conflict, "guildNotLinked, or concurrency.");
        });
    }

    public override async Task HandleAsync(ConfirmGuildClaimsRequest req, CancellationToken ct)
    {
        if (req.Claims is not { Count: > 0 and <= MaxClaims })
        {
            AddError(request => request.Claims, "Send 1 to 100 claims.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        if (await GuildManagerAccess.ResolveAsync(this, db, ProcessorState<CurrentUserState>().ProfileId, ct) is not { } access)
        {
            return;
        }

        var (guild, caller) = access;

        var hashService = Resolve<IColumnHashService>();
        var names = await Resolve<GuildClaimEvaluator>().LoadNamesAsync(guild.Id, guild.Members.ToList(), ct);
        var now = Resolve<TimeProvider>().GetUtcNow();
        var confirmed = new List<Guid>();
        var skipped = new List<Guid>();
        foreach (var claim in req.Claims.DistinctBy(claim => claim.GuildMemberId))
        {
            var member = guild.Members.FirstOrDefault(entity => entity.Id.Value == claim.GuildMemberId);
            if (member is not null
                && member.Id != caller.Id
                && GuildClaimEvaluator.StateOf(member) == GuildClaimState.Pending
                && names.AssignedNameOf(member) is null
                && string.Equals(GuildManagerAccess.ClaimId(hashService, member), claim.ProfileClaimId, StringComparison.Ordinal))
            {
                await GuildManagerAccess.ConfirmAsync(db, names, member, now, ct);
                confirmed.Add(claim.GuildMemberId);
            }
            else
            {
                skipped.Add(claim.GuildMemberId);
            }
        }

        if (await GuildManagerAccess.TrySaveAsync(this, db, ct))
        {
            await Send.OkAsync(new ConfirmGuildClaimsResponse(confirmed, skipped), ct);
        }
    }
}

public sealed record GuildPlayersResponse(IReadOnlyList<GuildPlayerSummary> Players);

public sealed record GuildPlayerSummary(
    Guid PlayerId,
    string? MaskedTacticusUserId,
    string? AssignedName,
    DateTimeOffset? LastHitAt,
    GuildPlayerMembership? Member);

public sealed record GuildPlayerMembership(Guid GuildMemberId, GuildRole Role, int Level, GuildPlayerClaim? Claim);

public sealed record GuildPlayerClaim(
    string ProfileClaimId,
    GuildClaimStatus State,
    GuildClaimMethod? Method,
    string? TacticusName,
    GuildNameMatch NameMatch,
    bool IsCaller,
    bool CanReject);

/// <summary>How a verified claim was verified.</summary>
public enum GuildClaimMethod
{
    [JsonStringEnumMemberName("key")] Key,
    [JsonStringEnumMemberName("nameMatch")] NameMatch,
    [JsonStringEnumMemberName("leader")] Leader,
}

public sealed record SetGuildPlayerNameRequest(string? Name);

public sealed record GuildClaimDecisionRequest(string ProfileClaimId);

public sealed record ConfirmGuildClaimsRequest(IReadOnlyList<GuildClaimReference> Claims);

public sealed record GuildClaimReference(Guid GuildMemberId, string ProfileClaimId);

public sealed record ConfirmGuildClaimsResponse(IReadOnlyList<Guid> Confirmed, IReadOnlyList<Guid> Skipped);
