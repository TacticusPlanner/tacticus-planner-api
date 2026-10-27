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

    private static GameCatalogCampaignCuration Curation(string campaign = "eventStandard1") => new(
        "eventCampaign1", "DeathGuard", "event", "Chaos", ["DeathGuard", "WorldEaters"],
        ["deathBlightlord", "deathPutrifier", "deathBlightbringer"],
        [new GameCatalogCampaignSourceCuration("Standard", campaign, "AMS")]);

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
        Assert.Single(GameCatalogDenormalizer.ReadDatamineCampaigns(
            new Dictionary<string, GameCatalogCampaignCuration> { [Key] = curation },
            new Dictionary<string, List<DatamineCampaign>> { ["Event"] = [new DatamineCampaign("eventStandard1", battles)] },
            Npcs,
            Upgrades)).Value;

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
}
