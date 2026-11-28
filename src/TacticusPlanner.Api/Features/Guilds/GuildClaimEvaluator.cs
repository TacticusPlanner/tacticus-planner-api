using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Guilds;

/// <summary>Where a guild member's claim stands; <see cref="None"/> when no profile is linked.</summary>
public enum GuildClaimState
{
    None,
    Pending,
    Verified,
    Rejected,
}

/// <summary>How the assigned name of a claimed player compares with the claimant's synced Tacticus name.</summary>
public enum GuildNameMatch
{
    [JsonStringEnumMemberName("noAssignedName")] NoAssignedName,
    [JsonStringEnumMemberName("noTacticusName")] NoTacticusName,
    [JsonStringEnumMemberName("matches")] Matches,
    [JsonStringEnumMemberName("differs")] Differs,
    [JsonStringEnumMemberName("duplicate")] Duplicate,
}

/// <summary>
/// The one place claims are verified or lose verification (<c>guild-member-verification</c>). Works on tracked
/// members and leaves saving to its caller, so it joins whatever save changed them.
/// </summary>
public sealed class GuildClaimEvaluator(PlannerDbContext db, TimeProvider timeProvider)
{
    public static GuildClaimState StateOf(GuildMember member) =>
        member.ProfileId is not { } profileId ? GuildClaimState.None
        : member.VerifiedProfileId == profileId ? GuildClaimState.Verified
        : member.RejectedProfileId == profileId ? GuildClaimState.Rejected
        : GuildClaimState.Pending;

    public static bool IsLeader(GuildRole role) => role is GuildRole.Leader or GuildRole.CoLeader;

    /// <summary>Trimmed, NFC and upper-invariant, so " dark " and "Dark" are one name.</summary>
    public static string NormalizeName(string name) =>
        name.Trim().Normalize(NormalizationForm.FormC).ToUpper(CultureInfo.InvariantCulture);

    /// <summary>
    /// Ends verification of changed links, then verifies by the stored key and by matching names. The members must
    /// be the guild's whole current roster, tracked.
    /// </summary>
    public async Task ApplyAsync(Guild guild, CancellationToken ct)
    {
        var members = guild.Members.ToList();
        foreach (var member in members)
        {
            if (member.VerifiedProfileId is not null && member.VerifiedProfileId != member.ProfileId)
            {
                ClearVerification(member);
            }
        }

        var now = timeProvider.GetUtcNow();
        if (!members.Any(member => IsLeader(member.Role) && StateOf(member) == GuildClaimState.Verified)
            && guild.ConfiguredByProfileId is { } registrar
            && members.FirstOrDefault(member => member.ProfileId == registrar) is { } keyHolder
            && IsLeader(keyHolder.Role)
            && StateOf(keyHolder) == GuildClaimState.Pending)
        {
            Verify(keyHolder, GuildClaimVerificationMethod.Key, now);
        }

        var names = await LoadNamesAsync(guild.Id, members, ct);
        foreach (var member in members)
        {
            if (StateOf(member) == GuildClaimState.Pending
                && !IsLeader(member.Role)
                && names.MatchOf(member) == GuildNameMatch.Matches)
            {
                Verify(member, GuildClaimVerificationMethod.NameMatch, now);
            }
        }
    }

    /// <summary>Assigned names of the guild's players and synced names of the members' claimants.</summary>
    public async Task<GuildClaimNames> LoadNamesAsync(GuildId guildId, IReadOnlyCollection<GuildMember> members, CancellationToken ct)
    {
        var assigned = await db.GuildPlayers.AsNoTracking()
            .Where(player => player.GuildId == guildId && player.AssignedName != null)
            .Select(player => new { player.TacticusUserIdHash, player.AssignedName })
            .ToListAsync(ct);

        var claimants = members.Select(member => member.ProfileId).OfType<ProfileId>().Distinct().ToList();

        // Only a sync made with the key now on the profile counts: saving another key moves the success time past the
        // snapshot. IgnoreQueryFilters: the claimants are other profiles than the caller.
        var synced = claimants.Count == 0
            ? []
            : await (
                from snapshot in db.PlayerDataSnapshots.IgnoreQueryFilters().AsNoTracking()
                join integration in db.TacticusIntegrations.IgnoreQueryFilters().AsNoTracking()
                    on snapshot.Id equals integration.Id
                where claimants.Contains(snapshot.Id)
                    && integration.TacticusSyncLastSucceededAt != null
                    && snapshot.SyncedAt >= integration.TacticusSyncLastSucceededAt
                    && snapshot.PlayerDetails.Name != string.Empty
                select new { snapshot.Id, snapshot.PlayerDetails.Name }
            ).ToListAsync(ct);

        return new GuildClaimNames(
            assigned.ToDictionary(player => Key(player.TacticusUserIdHash), player => player.AssignedName!),
            synced.ToDictionary(entry => entry.Id, entry => entry.Name),
            members);
    }

    public static void Verify(GuildMember member, GuildClaimVerificationMethod method, DateTimeOffset now)
    {
        member.VerifiedProfileId = member.ProfileId;
        member.VerificationMethod = method;
        member.VerifiedAt = now;
        member.RejectedProfileId = null;
    }

    public static void ClearVerification(GuildMember member)
    {
        member.VerifiedProfileId = null;
        member.VerificationMethod = null;
        member.VerifiedAt = null;
    }

    internal static string Key(TacticusUserIdHash hash) => Convert.ToHexString(hash.Value);
}

/// <summary>Names a claim is judged by, for one guild's current members.</summary>
public sealed class GuildClaimNames
{
    private readonly Dictionary<string, string> assignedByHash;
    private readonly Dictionary<ProfileId, string> tacticusByProfile;
    private readonly HashSet<string> duplicateAssigned;
    private readonly HashSet<string> duplicateTacticus;

    internal GuildClaimNames(
        Dictionary<string, string> assignedByHash,
        Dictionary<ProfileId, string> tacticusByProfile,
        IReadOnlyCollection<GuildMember> members)
    {
        this.assignedByHash = assignedByHash;
        this.tacticusByProfile = tacticusByProfile;

        // Duplicates among current members only: a former player sharing a name does not block a match.
        duplicateAssigned = members
            .Select(AssignedNameOf).OfType<string>()
            .Select(GuildClaimEvaluator.NormalizeName)
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        duplicateTacticus = members
            .Select(member => member.ProfileId).OfType<ProfileId>().Distinct()
            .Select(TacticusNameOf).OfType<string>()
            .Select(GuildClaimEvaluator.NormalizeName)
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    public string? AssignedNameOf(GuildMember member) =>
        member.TacticusUserIdHash is { } hash ? assignedByHash.GetValueOrDefault(GuildClaimEvaluator.Key(hash)) : null;

    public string? TacticusNameOf(ProfileId profileId) => tacticusByProfile.GetValueOrDefault(profileId);

    /// <summary>Whether the normalized name is assigned to more than one current member.</summary>
    public bool IsDuplicateAssigned(string name) => duplicateAssigned.Contains(GuildClaimEvaluator.NormalizeName(name));

    public GuildNameMatch MatchOf(GuildMember member)
    {
        var assigned = AssignedNameOf(member);
        var tacticus = member.ProfileId is { } profileId ? TacticusNameOf(profileId) : null;
        if (assigned is null)
        {
            return GuildNameMatch.NoAssignedName;
        }

        if (tacticus is null)
        {
            return GuildNameMatch.NoTacticusName;
        }

        var normalizedAssigned = GuildClaimEvaluator.NormalizeName(assigned);
        var normalizedTacticus = GuildClaimEvaluator.NormalizeName(tacticus);
        if (duplicateAssigned.Contains(normalizedAssigned) || duplicateTacticus.Contains(normalizedTacticus))
        {
            return GuildNameMatch.Duplicate;
        }

        return string.Equals(normalizedAssigned, normalizedTacticus, StringComparison.Ordinal)
            ? GuildNameMatch.Matches
            : GuildNameMatch.Differs;
    }
}
