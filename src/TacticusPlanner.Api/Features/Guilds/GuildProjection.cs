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
        var members = guild.Members
            .OrderBy(RoleRank)
            .ThenBy(DisplayLabel, StringComparer.OrdinalIgnoreCase)
            .Select(BuildMemberSummary)
            .ToList();

        return new RegisteredGuildResponse(
            guild.Id.Value,
            Guid.Parse(guild.TacticusGuildId.Value),
            guild.Tag,
            guild.Name,
            guild.Level,
            guild.LastSyncSucceededAt,
            callerMember.Role.ToString(),
            callerMember.Role is GuildRole.Leader or GuildRole.CoLeader,
            members
        );
    }

    private static GuildMemberSummary BuildMemberSummary(GuildMember member)
    {
        var maskedUserId = SecretMasker.Mask(member.TacticusUserId.Value) ?? string.Empty;

        return new GuildMemberSummary(
            member.Id.Value,
            maskedUserId,
            member.LinkedPlayerName,
            member.ProfileId is not null,
            member.Role.ToString(),
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
    string CallerRole,
    bool CanSynchronize,
    IReadOnlyList<GuildMemberSummary> Members
);

public sealed record GuildMemberSummary(
    Guid GuildMemberId,
    string MaskedTacticusUserId,
    string? LinkedPlayerName,
    bool IsLinked,
    string Role,
    int Level,
    long? LastActiveInGameOn,
    DateTimeOffset? LastActiveInPlannerOn,
    string DisplayLabel
);
