using System.Text.Json.Serialization;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Guilds;

namespace TacticusPlanner.Api.Features.Guilds;

/// <summary>
/// Builds the "registered guild" API projection shared by GetMyGuildEndpoint, RegisterGuildEndpoint, and
/// SyncMyGuildEndpoint. Masking (<see cref="SecretMasker"/>) is applied here, before any response object is
/// constructed, so a full Tacticus user id never reaches a response type.
/// </summary>
public static class GuildProjection
{
    public static RegisteredGuildResponse Build(Guild guild, GuildMember callerMember)
    {
        var members = InRosterOrder(guild.Members)
            .Select(BuildMemberSummary)
            .ToList();

        var callerState = GuildClaimEvaluator.StateOf(callerMember);
        var canManageMembers = GuildClaimEvaluator.IsLeader(callerMember.Role) && callerState == GuildClaimState.Verified;

        return new RegisteredGuildResponse(
            guild.Id.Value,
            Guid.Parse(guild.TacticusGuildId.Value),
            guild.Tag,
            guild.Name,
            guild.Level,
            guild.LastSyncSucceededAt,
            callerMember.Role,
            callerMember.Role is GuildRole.Leader or GuildRole.CoLeader,
            members,
            ToStatus(callerState) ?? GuildClaimStatus.Pending,
            canManageMembers,
            canManageMembers
                ? guild.Members.Count(member => GuildClaimEvaluator.StateOf(member) == GuildClaimState.Pending)
                : 0
        );
    }

    /// <summary>The wire value of a claim state; <see langword="null"/> without a claim.</summary>
    public static GuildClaimStatus? ToStatus(GuildClaimState state) => state switch
    {
        GuildClaimState.Pending => GuildClaimStatus.Pending,
        GuildClaimState.Verified => GuildClaimStatus.Verified,
        GuildClaimState.Rejected => GuildClaimStatus.Rejected,
        _ => null,
    };

    /// <summary>The order the Guild page lists members in.</summary>
    public static IOrderedEnumerable<GuildMember> InRosterOrder(IEnumerable<GuildMember> members) =>
        members.OrderBy(RoleRank).ThenBy(DisplayLabel, StringComparer.OrdinalIgnoreCase);

    private static GuildMemberSummary BuildMemberSummary(GuildMember member)
    {
        var maskedUserId = SecretMasker.Mask(member.TacticusUserId.Value) ?? string.Empty;

        return new GuildMemberSummary(
            member.Id.Value,
            maskedUserId,
            member.LinkedPlayerName,
            member.ProfileId is not null,
            member.Role,
            member.Level,
            member.LastActiveInGameOn,
            member.LastActiveInPlannerOn,
            member.LinkedPlayerName ?? maskedUserId
        );
    }

    private static string DisplayLabel(GuildMember member)
    {
        return member.LinkedPlayerName ?? SecretMasker.Mask(member.TacticusUserId.Value) ?? string.Empty;
    }

    // Leader, Co-Leader, Officer, Member — per the Guild Phase 1 spec's member-ordering rule.
    private static int RoleRank(GuildMember member)
    {
        return member.Role switch
        {
            GuildRole.Leader => 0,
            GuildRole.CoLeader => 1,
            GuildRole.Officer => 2,
            GuildRole.Member => 3,
            _ => 4,
        };
    }
}

public sealed record RegisteredGuildResponse(
    Guid GuildId,
    Guid TacticusGuildId,
    string Tag,
    string Name,
    int Level,
    DateTimeOffset? LastSyncSucceededAt,
    GuildRole CallerRole,
    bool CanSynchronize,
    IReadOnlyList<GuildMemberSummary> Members,
    GuildClaimStatus CallerClaimState,
    bool CanManageMembers,
    int PendingClaimCount
);

/// <summary>Where a guild member's claim stands (<c>guild-member-verification</c>).</summary>
public enum GuildClaimStatus
{
    [JsonStringEnumMemberName("pending")] Pending,
    [JsonStringEnumMemberName("verified")] Verified,
    [JsonStringEnumMemberName("rejected")] Rejected,
}

public sealed record GuildMemberSummary(
    Guid GuildMemberId,
    string MaskedTacticusUserId,
    string? LinkedPlayerName,
    bool IsLinked,
    GuildRole Role,
    int Level,
    long? LastActiveInGameOn,
    DateTimeOffset? LastActiveInPlannerOn,
    string DisplayLabel
);
