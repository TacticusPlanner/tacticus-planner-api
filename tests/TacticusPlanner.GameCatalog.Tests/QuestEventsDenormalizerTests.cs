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
        Assert.Equal(["astarEradicator", "lostaFearmonger", "orksWeirdboy", "tyranParasite", "votanChampion"], Snapshot.QuestEventViews.Select(quest => quest.FeaturedCharacterId));
        Assert.All(Snapshot.QuestEventViews, quest => Assert.Equal(Enumerable.Range(1, 17), quest.Battles.Select(battle => battle.Number)));
    }

    [Fact]
    public void EradicatorBattleOneIsServedWithGroupedEnemiesAndTypedObjectives()
    {
        var eradicator = Quest("astarEradicator");

        Assert.Equal(["AdeptusAstartes", "Ultramarines", "Sisterhood", "BloodAngels"], eradicator.AllowedFactionIds);
        Assert.Equal(["BlackLegion"], eradicator.EnemyFactionIds);
        var battle = eradicator.Battles[0];
        Assert.Equal(("Common", 1, 3, "EC2_04", 3, 2), (battle.Tier, battle.TierPosition, battle.DeploySlots, battle.BoardId, battle.SpawnPointsSet, battle.LightningVictoryTurns));
        Assert.Equal(
            [new GameCatalogQuestEnemyView("blackBossHaarkenLHE", 1, 1), new GameCatalogQuestEnemyView("blackNpc3TraitorGuardsman", 3, 6)],
            battle.Enemies);
        Assert.Equal(
            [new GameCatalogQuestObjectiveView("NoDamageTakenByAnyHero", null, 100), new GameCatalogQuestObjectiveView("UseNoSummons", null, 100)],
            battle.Objectives);
        Assert.Equal(new GameCatalogQuestLootView("upgArmC002", 3, 4), battle.Loot);
    }

    [Fact]
    public void EradicatorBattleThreeKeepsTheSameNpcAtDifferentIndexesApart()
    {
        Assert.Equal(
            [
                new GameCatalogQuestEnemyView("blackBossHaarkenLHE", 2, 1),
                new GameCatalogQuestEnemyView("blackNpc3TraitorGuardsman", 8, 1),
                new GameCatalogQuestEnemyView("blackNpc3TraitorGuardsman", 5, 9),
            ],
            Quest("astarEradicator").Battles[2].Enemies);
    }

    [Fact]
    public void EradicatorBattleSeventeenIsTheMythicBattle()
    {
        var battle = Quest("astarEradicator").Battles[16];

        Assert.Equal(("Mythic", 1, 5, "C2_47", 3), (battle.Tier, battle.TierPosition, battle.DeploySlots, battle.BoardId, battle.LightningVictoryTurns));
        Assert.Equal(new GameCatalogQuestLootView("upgHpM004", 1, 6), battle.Loot);
    }

    [Fact]
    public void DeploySlotsFollowTheTierPositionRule()
    {
        var battles = Quest("astarEradicator").Battles;

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
