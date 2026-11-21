using TacticusPlanner.Api.Features.Teams;
using TacticusPlanner.Domain.Teams;
using TacticusPlanner.GameCatalog;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// <see cref="TeamRules.Evaluate"/> over the shipped catalog (<c>personal-teams</c>, "Every save satisfies the team
/// rules"): every rule code, plus each spec scenario.
/// </summary>
public sealed class TeamRulesTests
{
    private static readonly TeamRuleFacts Facts = TeamCatalogFacts.For(GameCatalogLoader.Load());

    private static readonly string[] Ultras = ["ultraCalgar", "ultraTitus", "ultraApothecary", "ultraTigurius", "ultraEliminatorSgt", "ultraInceptorSgt"];

    private static TeamDraft Draft(
        TeamMode[] modes,
        string[]? characters = null,
        int? flex = null,
        string[]? mows = null,
        string? campaign = null,
        string[]? incursions = null,
        string name = "Test team",
        int? mowFlex = null,
        TeamRarityCap? cap = null) =>
        new(name, modes, characters ?? ["ultraCalgar", "ultraTitus", "ultraApothecary"], flex, mows ?? [], campaign, incursions ?? [], null, mowFlex, cap);

    private static IReadOnlyList<TeamRuleCode> Evaluate(TeamDraft draft) => TeamRules.Evaluate(draft, Facts);

    [Fact]
    public void AValidTeamBreaksNoRule() =>
        Assert.Empty(Evaluate(Draft([TeamMode.WarOffense], Ultras[..5], mows: ["ultraDreadnought"])));

    [Theory]
    [InlineData("ab")]
    [InlineData("   ab   ")]
    [InlineData("12345678901234567890123456789012345678901")]
    public void NameMustBeThreeToFortyCharacters(string name) =>
        Assert.Equal([TeamRuleCode.NameLength], Evaluate(Draft([TeamMode.Horde], name: name)));

    [Fact]
    public void AModeIsRequired() =>
        Assert.Equal([TeamRuleCode.NoMode], Evaluate(Draft([])));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ThreeCharactersAreRequired(int count) =>
        Assert.Equal([TeamRuleCode.TooFewCharacters], Evaluate(Draft([TeamMode.Horde], characters: Ultras[..count])));

    [Fact]
    public void MowsDoNotCountTowardThree() =>
        Assert.Equal(
            [TeamRuleCode.TooFewCharacters],
            Evaluate(Draft([TeamMode.Horde], characters: Ultras[..2], mows: ["ultraDreadnought", "tauBroadside"])));

    [Theory]
    [InlineData("notAUnit", null)]
    [InlineData("ultraCalgar", "notAMow")]
    public void UnitsMustExist(string character, string? mow) =>
        Assert.Equal(
            [TeamRuleCode.UnknownUnit],
            Evaluate(Draft([TeamMode.Horde], characters: [character, "ultraTitus", "ultraApothecary"], mows: mow is null ? [] : [mow])));

    [Fact]
    public void SixCoreCharactersInGuildWar() =>
        Assert.Equal([TeamRuleCode.TooManyCoreCharacters], Evaluate(Draft([TeamMode.WarOffense], Ultras)));

    [Fact]
    public void FlexCharactersDoNotCountTowardFive() =>
        Assert.Empty(Evaluate(Draft(
            [TeamMode.WarDefense], [.. Ultras, "bloodDante"], flex: 5)));

    [Fact]
    public void TwoCoreMowsInTournamentArena() =>
        Assert.Equal(
            [TeamRuleCode.TooManyCoreMows],
            Evaluate(Draft([TeamMode.TournamentArena], mows: ["ultraDreadnought", "tauBroadside"])));

    [Fact]
    public void FlexMowsDoNotCountTowardOne() =>
        Assert.Empty(Evaluate(Draft(
            [TeamMode.WarOffense], mows: ["ultraDreadnought", "tauBroadside", "orksRukkatrukk"], mowFlex: 1)));

    [Fact]
    public void EveryMowFlex() =>
        Assert.Empty(Evaluate(Draft([TeamMode.WarDefense], mows: ["ultraDreadnought", "tauBroadside"], mowFlex: 0)));

    [Theory]
    [InlineData(TeamRarityCap.Common)]
    [InlineData(TeamRarityCap.Mythic)]
    public void TheRarityCapChangesNoRule(TeamRarityCap cap) =>
        Assert.Equal(
            Evaluate(Draft([TeamMode.WarOffense], Ultras)),
            Evaluate(Draft([TeamMode.WarOffense], Ultras, cap: cap)));

    [Fact]
    public void GuildRaidHasNoSizeLimit() =>
        Assert.Empty(Evaluate(Draft(
            [TeamMode.GuildRaid], [.. Ultras, "bloodDante"], mows: ["ultraDreadnought", "tauBroadside"])));

    [Fact]
    public void CampaignCombinedWithAnotherMode() =>
        Assert.Contains(
            TeamRuleCode.ModeNotCombinable,
            Evaluate(Draft([TeamMode.Campaign, TeamMode.Horde], ["ultraTigurius", "ultraEliminatorSgt", "ultraInceptorSgt"], campaign: "campaign1")));

    [Fact]
    public void IncursionCombinedWithAnotherMode() =>
        Assert.Contains(
            TeamRuleCode.ModeNotCombinable,
            Evaluate(Draft([TeamMode.Incursion, TeamMode.WarOffense], incursions: ["orksRukkatrukk"])));

    [Fact]
    public void ACampaignTeamOnIndomitusIsValid() =>
        Assert.Empty(Evaluate(Draft(
            [TeamMode.Campaign], ["ultraTigurius", "ultraEliminatorSgt", "ultraInceptorSgt", "adeptCanoness"], campaign: "campaign1")));

    [Fact]
    public void CampaignAndIncursionTeamsHaveNoMows()
    {
        Assert.Contains(
            TeamRuleCode.MowsNotAllowed,
            Evaluate(Draft([TeamMode.Campaign], ["ultraTigurius", "ultraEliminatorSgt", "ultraInceptorSgt"], mows: ["ultraDreadnought"], campaign: "campaign1")));
        Assert.Contains(
            TeamRuleCode.MowsNotAllowed,
            Evaluate(Draft([TeamMode.Incursion], mows: ["ultraDreadnought"], incursions: ["orksRukkatrukk"])));
    }

    [Fact]
    public void ACampaignTeamNeedsACampaign() =>
        Assert.Contains(TeamRuleCode.CampaignRequired, Evaluate(Draft([TeamMode.Campaign])));

    [Fact]
    public void OnlyACampaignTeamHasACampaign() =>
        Assert.Equal([TeamRuleCode.CampaignRequired], Evaluate(Draft([TeamMode.Horde], campaign: "campaign1")));

    [Theory]
    [InlineData("elite1")]
    [InlineData("eliteMirror2")]
    [InlineData("notACampaign")]
    public void EliteIsNotASeparateCampaign(string groupId) =>
        Assert.Contains(
            TeamRuleCode.UnknownCampaign,
            Evaluate(Draft([TeamMode.Campaign], ["ultraTigurius", "ultraEliminatorSgt", "ultraInceptorSgt"], campaign: groupId)));

    [Fact]
    public void CampaignCoreCharacters() =>
        Assert.Equal(
            [TeamRuleCode.CoreCharacterMissing],
            Evaluate(Draft([TeamMode.Campaign], ["ultraTigurius", "ultraEliminatorSgt", "adeptCanoness"], campaign: "campaign1")));

    [Fact]
    public void CoreCharacterListedAsFlex() =>
        Assert.Equal(
            [TeamRuleCode.CoreCharacterMissing],
            Evaluate(Draft([TeamMode.Campaign], ["ultraEliminatorSgt", "ultraInceptorSgt", "ultraTigurius"], flex: 2, campaign: "campaign1")));

    [Fact]
    public void ACampaignAllowsOnlyItsFactions() =>
        Assert.Equal(
            [TeamRuleCode.FactionNotAllowed],
            Evaluate(Draft([TeamMode.Campaign], ["ultraTigurius", "ultraEliminatorSgt", "ultraInceptorSgt", "orksNob"], campaign: "campaign1")));

    [Fact]
    public void EveryNonEliteCampaignIsOffered()
    {
        string[] expected =
        [
            "campaign1", "campaign2", "campaign3", "campaign4", "mirror1", "mirror2", "mirror3", "mirror4",
            "eventCampaign1", "eventCampaign2", "eventCampaign3", "eventCampaign4", "eventCampaign5", "eventCampaign6",
            "eventCampaign7",
        ];
        Assert.Equal(expected.Order(), Facts.Campaigns.Keys.Order());
    }

    [Fact]
    public void IncursionFactionIsOutsideWhatTheMowAllows() =>
        Assert.Equal(
            [TeamRuleCode.FactionNotAllowed],
            Evaluate(Draft([TeamMode.Incursion], ["ultraCalgar", "ultraTitus", "adeptCanoness"], incursions: ["orksRukkatrukk"])));

    [Fact]
    public void ATeamForTwoIncursions() =>
        Assert.Equal(
            [TeamRuleCode.FactionNotAllowed],
            Evaluate(Draft([TeamMode.Incursion], ["ultraCalgar", "ultraTitus", "adeptCanoness"], incursions: ["tauBroadside", "orksRukkatrukk"])));

    [Fact]
    public void ATeamForTwoCompatibleIncursions() =>
        Assert.Empty(Evaluate(Draft([TeamMode.Incursion], Ultras[..3], incursions: ["tauBroadside", "orksRukkatrukk"])));

    [Fact]
    public void IncursionsWithNoFactionInCommon() =>
        Assert.Contains(
            TeamRuleCode.NoSharedIncursionFaction,
            Evaluate(Draft([TeamMode.Incursion], Ultras[..3], incursions: ["ultraDreadnought", "tauBroadside"])));

    [Fact]
    public void AnIncursionTeamNeedsAnIncursion() =>
        Assert.Equal([TeamRuleCode.IncursionRequired], Evaluate(Draft([TeamMode.Incursion])));

    [Fact]
    public void OnlyAnIncursionTeamHasIncursions() =>
        Assert.Equal([TeamRuleCode.IncursionRequired], Evaluate(Draft([TeamMode.Horde], incursions: ["orksRukkatrukk"])));

    [Fact]
    public void IncursionsMustBeServed() =>
        Assert.Equal(
            [TeamRuleCode.UnknownIncursion],
            Evaluate(Draft([TeamMode.Incursion], Ultras[..3], incursions: ["orksRukkatrukk", "notAnIncursion"])));

    [Fact]
    public void CodesAreReportedInRulesTableOrder() =>
        Assert.Equal(
            [TeamRuleCode.NameLength, TeamRuleCode.NoMode, TeamRuleCode.TooFewCharacters],
            Evaluate(Draft([], characters: [], name: "x")));

    [Fact]
    public void ShippedCatalogFacts()
    {
        Assert.DoesNotContain("elite1", Facts.Campaigns.Keys);
        Assert.Equal(["ultraTigurius", "ultraEliminatorSgt", "ultraInceptorSgt"], Facts.Campaigns["campaign1"].CoreCharacterIds);
        Assert.Equal(6, Facts.IncursionFactionsByMowId["orksRukkatrukk"].Count);
    }
}
