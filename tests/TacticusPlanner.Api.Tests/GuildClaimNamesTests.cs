using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Api.Tests;

public sealed class GuildClaimNamesTests
{
    [Theory]
    [InlineData(" dark ", "Dark")]
    [InlineData("DARK", "dark")]
    // "é" precomposed against "e" with a combining acute accent.
    [InlineData("René", "René")]
    public void NamesMatchIgnoringCaseSpacesAndComposition(string assigned, string synced)
    {
        var member = Member(GuildRole.Member);

        Assert.Equal(GuildNameMatch.Matches, Names((member, assigned, synced)).MatchOf(member));
    }

    [Fact]
    public void DifferentNamesDiffer()
    {
        var member = Member(GuildRole.Member);

        Assert.Equal(GuildNameMatch.Differs, Names((member, "Dark", "Bob")).MatchOf(member));
    }

    [Fact]
    public void NoAssignedNameComesBeforeAMissingTacticusName()
    {
        var unnamed = Member(GuildRole.Member);
        var unsynced = Member(GuildRole.Member);
        var names = Names((unnamed, null, "Bob"), (unsynced, "Dark", null));

        Assert.Equal(GuildNameMatch.NoAssignedName, names.MatchOf(unnamed));
        Assert.Equal(GuildNameMatch.NoTacticusName, names.MatchOf(unsynced));
    }

    [Fact]
    public void AnAssignedNameUsedTwiceIsADuplicate()
    {
        var first = Member(GuildRole.Member);
        var second = Member(GuildRole.Officer);
        var names = Names((first, "Twin", "Twin"), (second, " twin", "Other"));

        Assert.Equal(GuildNameMatch.Duplicate, names.MatchOf(first));
        Assert.Equal(GuildNameMatch.Duplicate, names.MatchOf(second));
        Assert.True(names.IsDuplicateAssigned("TWIN"));
    }

    [Fact]
    public void ATacticusNameTwoClaimantsShareIsADuplicate()
    {
        var first = Member(GuildRole.Member);
        var second = Member(GuildRole.Member);
        var names = Names((first, "Dark", "Dark"), (second, "Other", "dark"));

        Assert.Equal(GuildNameMatch.Duplicate, names.MatchOf(first));
    }

    [Fact]
    public void ClaimStateFollowsTheLinkedProfile()
    {
        var member = Member(GuildRole.Member);
        Assert.Equal(GuildClaimState.Pending, GuildClaimEvaluator.StateOf(member));

        GuildClaimEvaluator.Verify(member, GuildClaimVerificationMethod.Leader, DateTimeOffset.UtcNow);
        Assert.Equal(GuildClaimState.Verified, GuildClaimEvaluator.StateOf(member));

        // Another profile on the same member is not verified by the old verification.
        member.ProfileId = ProfileId.From(Guid.NewGuid());
        Assert.Equal(GuildClaimState.Pending, GuildClaimEvaluator.StateOf(member));

        member.RejectedProfileId = member.ProfileId;
        Assert.Equal(GuildClaimState.Rejected, GuildClaimEvaluator.StateOf(member));

        member.ProfileId = null;
        Assert.Equal(GuildClaimState.None, GuildClaimEvaluator.StateOf(member));
    }

    private static GuildMember Member(GuildRole role)
    {
        var id = Guid.NewGuid();
        return new GuildMember
        {
            Id = GuildMemberId.From(Guid.NewGuid()),
            TacticusUserId = TacticusUserId.From(id.ToString()),
            TacticusUserIdHash = TacticusUserIdHash.From(id.ToByteArray().Concat(id.ToByteArray()).ToArray()),
            ProfileId = ProfileId.From(Guid.NewGuid()),
            Role = role,
        };
    }

    private static GuildClaimNames Names(params (GuildMember Member, string? Assigned, string? Synced)[] entries) => new(
        entries.Where(entry => entry.Assigned is not null)
            .ToDictionary(entry => GuildClaimEvaluator.Key(entry.Member.TacticusUserIdHash!.Value), entry => entry.Assigned!),
        entries.Where(entry => entry.Synced is not null)
            .ToDictionary(entry => entry.Member.ProfileId!.Value, entry => entry.Synced!),
        [.. entries.Select(entry => entry.Member)]);
}
