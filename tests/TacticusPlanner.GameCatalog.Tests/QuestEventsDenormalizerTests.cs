using System.Text.Json;
using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class QuestEventsDenormalizerTests
{
    private static readonly GameCatalogSnapshot Snapshot = GameCatalogLoader.Load();

    private static GameCatalogQuestEventView Quest(string id) =>
        Snapshot.QuestEventViews.Single(quest => quest.FeaturedCharacterId == id);

    [Fact]
    public void ShippedDatasetServesTheGamesFiveQuestsWithSeventeenBattlesEach()
    {
        Assert.Equal(["astarCyrus", "astarEradicator", "darkaSternguard", "thousSekhetar", "votanChampion"], Snapshot.QuestEventViews.Select(quest => quest.FeaturedCharacterId));
        Assert.All(Snapshot.QuestEventViews, quest => Assert.Equal(Enumerable.Range(1, 17), quest.Battles.Select(battle => battle.Number)));
    }

    [Fact]
    public void CyrusBattleOneIsServedWithGroupedEnemiesAndTypedObjectives()
    {
        var cyrus = Quest("astarCyrus");

        Assert.Equal(["AdeptusAstartes", "Ultramarines", "SpaceWolves", "BlackTemplars"], cyrus.AllowedFactionIds);
        Assert.Equal(["Necrons"], cyrus.EnemyFactionIds);
        var battle = cyrus.Battles[0];
        Assert.Equal(("Common", 1, 3, "C3_70", 6, 3), (battle.Tier, battle.TierPosition, battle.DeploySlots, battle.BoardId, battle.SpawnPointsSet, battle.LightningVictoryTurns));
        Assert.Equal(
            [new GameCatalogQuestEnemyView("necroNpc2FlayedOne", 1, 3), new GameCatalogQuestEnemyView("necroNpc1Warrior", 1, 4)],
            battle.Enemies);
        Assert.Equal(
            [new GameCatalogQuestObjectiveView("NoDamageTakenByAnyHero", null, 100), new GameCatalogQuestObjectiveView("NotDamageType", "Power", 100)],
            battle.Objectives);
        Assert.Equal(new GameCatalogQuestLootView("upgDmgC001", 3, 4), battle.Loot);
    }

    [Fact]
    public void CyrusBattleThreeKeepsTheSameNpcAtDifferentIndexesApart()
    {
        Assert.Equal(
            [
                new GameCatalogQuestEnemyView("necroBossPlasmancerLHE", 3, 1),
                new GameCatalogQuestEnemyView("necroNpc2FlayedOne", 3, 3),
                new GameCatalogQuestEnemyView("necroNpc1Warrior", 2, 4),
            ],
            Quest("astarCyrus").Battles[2].Enemies);
    }

    [Fact]
    public void CyrusBattleSeventeenIsTheMythicBattle()
    {
        var battle = Quest("astarCyrus").Battles[16];

        Assert.Equal(("Mythic", 1, 5, "LE3_24", 4), (battle.Tier, battle.TierPosition, battle.DeploySlots, battle.BoardId, battle.LightningVictoryTurns));
        Assert.Equal(new GameCatalogQuestLootView("upgHpM001", 1, 6), battle.Loot);
    }

    [Fact]
    public void DeploySlotsFollowTheTierPositionRule()
    {
        var battles = Quest("astarCyrus").Battles;

        Assert.Equal([3, 4, 5, 3, 4, 5, 3, 3, 3, 4, 5, 3, 4, 5, 5, 5, 5], battles.Select(battle => battle.DeploySlots));
        Assert.Equal([1, 2, 3, 4, 5], battles.Where(battle => battle.Tier == "Rare").Select(battle => battle.TierPosition));
    }

    [Fact]
    public void APowerUpWithoutAnIndexIsServedAtIndexOne()
    {
        Assert.Contains(new GameCatalogQuestEnemyView("powupArmor", 1, 1), Quest("astarEradicator").Battles[8].Enemies);
    }

    [Fact]
    public void ANumericHitTargetIsServedAsAString()
    {
        var target = JsonDocument.Parse("3").RootElement;

        Assert.Equal("3", GameCatalogDenormalizer.QuestObjectiveTarget(target));
        Assert.Contains(new GameCatalogQuestObjectiveView("MaxHits", "3", 100), Quest("astarEradicator").Battles[6].Objectives);
    }

    [Fact]
    public void CratesAreSeventeenWithTheLastAtEightThousandEightHundred()
    {
        Assert.Equal(Enumerable.Range(1, 17), Snapshot.QuestCrateViews.Select(crate => crate.Number));
        Assert.Equal(8800, Snapshot.QuestCrateViews[^1].Points);
        Assert.Contains(new GameCatalogQuestCrateReward("AbilityBadge", 1, 1, "Mythic", "Mythic"), Snapshot.QuestCrateViews[^1].Rewards);
    }
}
