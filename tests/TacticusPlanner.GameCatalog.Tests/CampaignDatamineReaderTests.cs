using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class CampaignDatamineReaderTests
{
    private const string Key = "campaign-battles-death-guard-vs-admech";

    private static DatamineNpcStat Stat(int rank, int stars) => new(10, 5, 5, rank, stars, 1, 0);

    private static readonly Dictionary<string, DatamineNpc> Npcs = new(StringComparer.Ordinal)
    {
        ["admecNpc1Vanguard"] = new("Skitarii Vanguard", "AdeptusMechanicus", "Imperial", 2, null, null, null, null,
            [Stat(0, 0), Stat(1, 0), Stat(2, 1)]),
        ["admecBossDestroyer"] = new("Exitor-Rho", "AdeptusMechanicus", "Imperial", 2, null, null, null, null,
            [Stat(2, 3), Stat(10, 6)]),
        ["blackNpc3TraitorGuardsman"] = new("Traitor Guardsman", "BlackLegion", "Chaos", 2, null, null, null, null, [Stat(0, 0)]),
    };

    private static readonly Dictionary<string, DatamineUpgrade> Upgrades = new(StringComparer.Ordinal)
    {
        ["upgHpC015"] = new("Bionic Arm", "Common", "Health", null),
    };

    private static GameCatalogCampaignCuration Curation(string campaign = "eventStandard1") =>
        new([new GameCatalogCampaignSourceCuration(campaign, "AMS")]);

    private static readonly IReadOnlyDictionary<string, GameCatalogFactionUnits> Units = GameCatalogLoader.Load().UnitsByFaction;

    private static DatamineCampaignUnlockConditions Unlock(string[] factions, string[]? units = null, string? follows = null) => new(
        new DatamineCampaignRequiredUnits(factions, units),
        follows is null ? null : [new DatamineCompletedCampaign("Event", follows)]);

    private static readonly DatamineCampaignUnlockConditions EventUnlock =
        Unlock(["DeathGuard", "WorldEaters"], ["deathBlightlord", "deathPutrifier", "deathBlightbringer"]);

    private static DatamineCampaignBattle Battle(string battleId, string? chanceOf = "upgHpC015%12/13") => new(
        battleId,
        6,
        4,
        900,
        [
            ["admecNpc1Vanguard:5", "admecNpc1Vanguard:1", "admecBossDestroyer:1", "powupHealth", "admecNpc1Vanguard:1"],
            ["blackNpc3TraitorGuardsman:0"],
        ],
        [2, 1],
        new DatamineCampaignLoot(["gold:40-66", "xpCommon"], chanceOf));

    private static GameCatalogCampaignGroup Read(GameCatalogCampaignCuration curation, params DatamineCampaignBattle[] battles) =>
        Read(curation, new Dictionary<string, List<DatamineCampaign>> { ["Event"] = [new DatamineCampaign("eventStandard1", battles, EventUnlock)] });

    private static readonly DatamineCampaignEventLoot Milestones = new("upgHpC015", "xpCommon:2", "gold:100-150");

    // Every event battle gets the same milestone loot unless a test passes its own campaignEvent.json.
    private static List<DatamineCampaignEvent> EventsFor(Dictionary<string, List<DatamineCampaign>> campaigns) =>
    [
        new(campaigns.GetValueOrDefault("Event", [])
            .Select(campaign => new DatamineCampaignEventCampaign(
                campaign.Id,
                campaign.Id + "-extremis",
                campaign.Battles.Select(battle => new DatamineCampaignEventBattle(battle.BattleId, Milestones)).ToArray(),
                []))
            .ToArray()),
    ];

    private static GameCatalogCampaignGroup Read(
        GameCatalogCampaignCuration curation,
        Dictionary<string, List<DatamineCampaign>> campaigns,
        List<DatamineCampaignEvent>? events = null) =>
        Assert.Single(GameCatalogDenormalizer.ReadDatamineCampaigns(
            new Dictionary<string, GameCatalogCampaignCuration> { [Key] = curation },
            campaigns,
            Units,
            Npcs,
            Upgrades,
            events ?? EventsFor(campaigns))).Value;

    [Fact]
    public void ABattleReadsItsOneTimeMedalRewards()
    {
        var battle = Battle("01") with
        {
            Loot = new DatamineCampaignLoot(["gold:40-66"], null, "xpCommon", null, "shards_deathBlightlord:5"),
        };

        var read = Assert.Single(Read(Curation(), battle).Battles);

        Assert.Equal(
            [new GameCatalogCampaignMedalReward(1, "xpCommon", 1, 1), new GameCatalogCampaignMedalReward(3, "shards_deathBlightlord", 5, 5)],
            read.Rewards.Medals);
    }

    [Fact]
    public void AnEventBattleReadsItsMilestoneRewardsFromTheCampaignEvent()
    {
        var read = Assert.Single(Read(Curation(), Battle("01")).Battles);

        Assert.Equal(
            [
                new GameCatalogCampaignMilestoneReward("Bronze", "upgHpC015", 1, 1),
                new GameCatalogCampaignMilestoneReward("Silver", "xpCommon", 2, 2),
                new GameCatalogCampaignMilestoneReward("Gold", "gold", 100, 150),
            ],
            read.Rewards.Milestones);
    }

    [Fact]
    public void AnEventBattleMissingFromTheCampaignEventIsRejected()
    {
        var campaigns = new Dictionary<string, List<DatamineCampaign>> { ["Event"] = [new DatamineCampaign("eventStandard1", [Battle("01")], EventUnlock)] };

        var error = Assert.Throws<InvalidOperationException>(() => Read(Curation(), campaigns, []));

        Assert.Contains("has no milestone rewards", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABattleReadsItsIdRewardsAndEnemiesFromTheGame()
    {
        var group = Read(Curation(), Battle("05"), Battle("13B"));

        Assert.Equal(["Standard"], group.Types);
        Assert.Equal([("AMS05", 5, false), ("AMSC13B", 13, true)], group.Battles.Select(battle => (battle.Id, battle.NodeNumber, battle.Challenge)));
        var battle = group.Battles[0];
        Assert.Equal((6, 4, 900), (battle.EnergyCost, battle.Slots, battle.EnemyPower));
        Assert.Equal([new GameCatalogCampaignGuaranteedReward("gold", 40, 66), new GameCatalogCampaignGuaranteedReward("xpCommon", 1, 1)], battle.Rewards.Guaranteed);
        Assert.Equal(new GameCatalogCampaignPotentialReward("upgHpC015", "upgradeCommon_eventStandard"), Assert.Single(battle.Rewards.Potential));
        Assert.Equal("upgradeCommon_eventStandardChallenge", Assert.Single(group.Battles[1].Rewards.Potential).ChanceId);

        // The allied team and the power-up are not enemies; a boss index is 1-based, an npc's 0-based and capped.
        Assert.Equal(4, battle.EnemiesTotal);
        Assert.Equal(["AdeptusMechanicus"], battle.EnemiesFactions);
        Assert.Equal(["Exitor-Rho", "Skitarii Vanguard"], battle.EnemiesTypes);
        Assert.Equal(
            [
                new GameCatalogCampaignDetailedEnemy("admecBossDestroyer", "Exitor-Rho", 1, 3, "Stone3"),
                new GameCatalogCampaignDetailedEnemy("admecNpc1Vanguard", "Skitarii Vanguard", 2, 0, "Stone2"),
                new GameCatalogCampaignDetailedEnemy("admecNpc1Vanguard", "Skitarii Vanguard", 1, 1, "Stone3"),
            ],
            battle.DetailedEnemyTypes);
    }

    [Fact]
    public void GroupMetadataFollowsTheGamesUnlockConditions()
    {
        var group = Read(
            new([new("eventStandard1", "AMS"), new("eventExtremis1", "AME")]),
            new Dictionary<string, List<DatamineCampaign>>
            {
                ["Event"] =
                [
                    new DatamineCampaign("eventStandard1", [Battle("01")], EventUnlock),
                    // An extremis campaign names its units again, or leaves them to the campaign it follows.
                    new DatamineCampaign("eventExtremis1", [Battle("01")], Unlock(["DeathGuard", "WorldEaters"], follows: "eventStandard1")),
                ],
            });

        Assert.Equal(("eventCampaign1", "event", "DeathGuard", "Chaos"), (group.GroupId, group.ReleaseType, group.Faction, group.AlliesAlliance));
        Assert.Equal(["Standard", "Extremis"], group.Types);
        Assert.Equal(["deathBlightlord", "deathPutrifier", "deathBlightbringer"], group.CoreCharacters);
        // Allies keep the catalog's faction order.
        Assert.Equal(["DeathGuard", "WorldEaters"], group.AlliesFactions);
        Assert.Equal(["AMS01", "AME01"], group.Battles.Select(battle => battle.Id));
    }

    [Fact]
    public void AlliesAreEveryFactionTheCampaignAllows()
    {
        Dictionary<string, List<DatamineCampaign>> FallOfCadia(params string[] allowed) => new()
        {
            ["Standard"] = [new DatamineCampaign("campaign2", [Battle("01")], Unlock(allowed, ["blackHaarken", "blackTerminator", "blackPossession"]))],
        };

        var group = Read(new([new("campaign2", "FoC")]), FallOfCadia("WorldEaters", "BlackLegion", "DeathGuard", "TheLostAndTheDamned"));

        Assert.Equal(("campaign2", "standard", "BlackLegion"), (group.GroupId, group.ReleaseType, group.Faction));
        Assert.Equal(["BlackLegion", "DeathGuard", "TheLostAndTheDamned", "WorldEaters"], group.AlliesFactions);
        var error = Assert.Throws<InvalidOperationException>(() => Read(new([new("campaign2", "FoC")]), FallOfCadia("BlackLegion", "Traitors")));
        Assert.Contains("'Traitors'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreCharactersAreCuratedOnlyWhereTheGameNamesNone()
    {
        Dictionary<string, List<DatamineCampaign>> Indomitus() => new()
        {
            ["Standard"] = [new DatamineCampaign("campaign1", [Battle("01")], Unlock(["Ultramarines", "Sisterhood"]))],
            ["Elite"] = [new DatamineCampaign("elite1", [Battle("01")], new(
                new DatamineCampaignRequiredUnits(["Ultramarines", "Sisterhood"], null), [new DatamineCompletedCampaign("Standard", "campaign1")]))],
        };
        string[] core = ["ultraTigurius", "ultraEliminatorSgt", "ultraInceptorSgt"];

        var elite = Read(new([new("elite1", "IE")], core), Indomitus());
        Assert.Equal(("elite1", "Ultramarines", "Imperial"), (elite.GroupId, elite.Faction, elite.AlliesAlliance));
        Assert.Equal(core, elite.CoreCharacters);
        Assert.Equal(["Sisterhood", "Ultramarines"], elite.AlliesFactions);

        var missing = Assert.Throws<InvalidOperationException>(() => Read(new([new("campaign1", "I")]), Indomitus()));
        Assert.Contains("must be curated", missing.Message, StringComparison.Ordinal);
        var redundant = Assert.Throws<InvalidOperationException>(() => Read(Curation() with { CoreCharacters = core }, Battle("01")));
        Assert.Contains("already names", redundant.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownCampaignFails()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Read(Curation("eventStandard9"), Battle("01")));

        Assert.Contains("campaign-battles", error.Message, StringComparison.Ordinal);
        Assert.Contains("'eventStandard9'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADropThatIsNotAShardOrUpgradeFails()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Read(Curation(), Battle("01", "gold%1/2")));

        Assert.Contains("'AMS01'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'gold'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippedEventsServeThirtyThreeBattlesPerTypeWithThreeChallenges()
    {
        var group = GameCatalogLoader.Load().CampaignGroups["campaign-battles-death-guard-vs-admech"];

        Assert.All(group.Types, type =>
        {
            var battles = group.Battles.Where(battle => battle.Type == type).ToArray();
            Assert.Equal((33, 3), (battles.Length, battles.Count(battle => battle.Challenge)));
        });
    }

    [Fact]
    public void GroupsCarryTheirCampaignNumberAndBattlesTheirLightningVictory()
    {
        var group = Read(Curation(), Battle("01") with { LightningVictory = 4 }, Battle("02"));

        Assert.Equal(1, group.Number);
        Assert.Equal([4, null], group.Battles.Select(battle => battle.LightningVictoryTurns));
    }

    [Fact]
    public void AStorylineBattleOfAnotherCampaignFails()
    {
        var campaigns = new Dictionary<string, List<DatamineCampaign>>
        {
            ["Standard"] = [new DatamineCampaign("campaign2", [Battle("01") with { CampaignId = 3 }], Unlock(["BlackLegion"], ["blackHaarken", "blackTerminator", "blackPossession"]))],
        };

        var error = Assert.Throws<InvalidOperationException>(() => Read(new([new("campaign2", "FoC")]), campaigns));

        Assert.Contains("not campaign 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippedGroupsNumberTheirCampaignsAndEarlyIndomitusHasNoLightningVictory()
    {
        var snapshot = GameCatalogLoader.Load();
        var definitions = GameCatalogDenormalizer.BuildCampaignDefinitions(snapshot.CampaignGroups).ToDictionary(definition => definition.GroupId);

        Assert.Equal((1, 2, 4, 4, 6), (definitions["campaign1"].Number, definitions["mirror2"].Number, definitions["elite4"].Number, definitions["eliteMirror4"].Number, definitions["eventCampaign6"].Number));
        var indomitus = GameCatalogDenormalizer.BuildCampaignBattles(snapshot.CampaignGroups, [])
            .Where(battle => battle.CampaignGroupId == "campaign1")
            .ToDictionary(battle => battle.NodeNumber, battle => battle.LightningVictoryTurns);
        Assert.Null(indomitus[18]);
        Assert.NotNull(indomitus[19]);
    }
}
