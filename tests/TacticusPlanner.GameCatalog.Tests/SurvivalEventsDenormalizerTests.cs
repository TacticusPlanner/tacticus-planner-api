using System.Text.Json;
using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class SurvivalEventsDenormalizerTests
{
    private static readonly IReadOnlyList<GameCatalogSurvivalEvent> Shipped = GameCatalogLoader.Load().SurvivalEventViews;

    private static GameCatalogSurvivalEvent Event(string id) => Shipped.Single(survivalEvent => survivalEvent.Id == id);

    [Fact]
    public void EverySeasonalEventWithSurvivalIsServedById() =>
        Assert.Equal(
            ["crescendo-a", "crescendo-b", "december-2026", "may-2026", "september-2026"],
            Shipped.Select(survivalEvent => survivalEvent.Id));

    [Theory]
    [InlineData("season_december_2026_event", "december-2026")]
    [InlineData("season_crescendo_a_event", "crescendo-a")]
    public void TheIdComesFromTheEventName(string eventName, string id) =>
        Assert.Equal(id, GameCatalogDenormalizer.SurvivalEventId(eventName));

    [Fact]
    public void TheCalendarDatesAnEventAndLeavesTheRestUndated()
    {
        var december = Event("december-2026");
        Assert.Equal("survival", december.DefinitionId);
        Assert.Equal(new DateTimeOffset(2026, 12, 3, 0, 0, 0, TimeSpan.Zero), december.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 12, 10, 0, 0, 0, TimeSpan.Zero), december.EndUtc);

        var crescendo = Event("crescendo-a");
        Assert.Null(crescendo.DefinitionId);
        Assert.Null(crescendo.StartUtc);
    }

    [Fact]
    public void RulesAllowEveryFactionTheBattleDoesNotBar()
    {
        var rules = Event("december-2026").Rules;

        Assert.Equal(
            ["Ultramarines", "Sisterhood", "AstraMilitarum", "AdeptusMechanicus", "Custodes", "LeaguesOfVotann", "AdeptusAstartes"],
            rules.AllowedFactionIds);
        Assert.False(rules.MowsAllowed);
        Assert.Equal(10, rules.MaxCharacters);
        Assert.Equal(new GameCatalogSurvivalStamina(2, 5, 43201, 1), rules.Stamina);
        Assert.Equal(new GameCatalogSurvivalLastStand("RepairField", 25), rules.LastStand);
        Assert.Null(rules.Summon);
        Assert.Equal(new GameCatalogSurvivalRarityWave("Uncommon", 3), rules.PersistentRarityUps[0]);
    }

    [Fact]
    public void CrescendoHasAGlobalSummonInsteadOfLastStand()
    {
        var rules = Event("crescendo-a").Rules;

        Assert.Null(rules.LastStand);
        Assert.Equal("astraSmnGuardsman", rules.Summon!.UnitId);
        Assert.Equal(new GameCatalogSurvivalSummonLevel("Mythic", 65), rules.Summon.Levels[^1]);
    }

    [Fact]
    public void WavesGroupTheirEnemiesAndNameWhatClearingThemGrants()
    {
        var waves = Event("december-2026").Waves;

        Assert.Equal(20, waves.Count);
        Assert.Equal(
            [new GameCatalogSurvivalUnitCount("orksNpc1Grot", 6, 5), new GameCatalogSurvivalUnitCount("orksNpc2OrkBoy", 6, 3)],
            waves[1].Enemies);
        Assert.Equal(3, waves[2].Wave);
        Assert.Equal(5, waves[2].Round);
        Assert.Equal("Uncommon", waves[2].EnemyRarityOnClear);
        Assert.Equal(
            [
                new GameCatalogSurvivalPowerUp("powupHealing", null, null, 1),
                new GameCatalogSurvivalPowerUp("powupSummonSpawn_admecSmnTechpriest", "powupSummonSpawn_admecSmnTechpriest", 2, 1),
            ],
            waves[2].PowerUpsOnClear);
        Assert.Equal(60_022_256_310, waves[^1].Power);
    }

    [Fact]
    public void ACloneWaveHasNoEnemiesAndItsHealth()
    {
        var clone = Event("december-2026").Waves.Single(wave => wave.CloneHealthPct is not null);

        Assert.Equal(16, clone.Wave);
        Assert.Equal(80, clone.CloneHealthPct);
        Assert.Empty(clone.Enemies);
    }

    [Fact]
    public void ScoringKeepsMedalsByWaveKillScoresAndTheCloneScore()
    {
        var scoring = Event("december-2026").Scoring;

        Assert.Equal(["iron", "bronze", "silver", "gold", "platinum", "diamond"], scoring.Medals.Select(medal => medal.Medal));
        Assert.Equal(new GameCatalogRewardRangeView("xpMythic", 1, 1), scoring.Medals[^1].Rewards.Single());
        Assert.Equal(13, scoring.Medals[^1].Wave);
        Assert.Equal(1000, scoring.CloneWaveScore);
        Assert.Equal(400, scoring.SkippedTurnScore);
        Assert.Contains(new GameCatalogSurvivalKillScore("orksNpc1Grot", 6, 47), scoring.KillScores);
        Assert.Equal(16, Event("crescendo-a").Scoring.Medals.Single(medal => medal.Medal == "adamantine").Wave);
    }

    [Fact]
    public void LaddersChestsMissionsAndOffersComeFromTheEvent()
    {
        var december = Event("december-2026");

        Assert.Equal(24, december.Milestones.Count);
        Assert.Equal(100, december.Milestones[0].RequiredProgress);
        Assert.Equal(new GameCatalogRewardRangeView("seasonalEventCurrencyDecember2026", 30, 30), december.Milestones[0].Rewards.Single());
        Assert.Equal(Enumerable.Range(1, 35), december.Chests.Select(chest => chest.Level));
        Assert.Equal(new GameCatalogSurvivalCost("seasonalEventCurrencyDecember2026", 40), december.Chests[0].Cost);
        Assert.Equal(7, december.Missions.Daily.Count);
        Assert.Equal(20, december.Missions.Free.Single().Missions.Count);
        Assert.Equal(10, december.Missions.Premium.Single().Missions.Count);
        Assert.Equal(["pass", "playmore"], december.Offers.Select(offer => offer.Kind));
        Assert.Equal(999, december.Offers[0].PriceCents);
        Assert.Contains(new GameCatalogRewardRangeView("seasonalPremium", 1, 1), december.Offers[0].Rewards);
        Assert.Equal(new GameCatalogSurvivalPoints(5450, 2020, 1000, 2025, 405), december.Points);
    }

    [Fact]
    public void CrescendoEventsTakeOnlyTheirOwnChests()
    {
        Assert.Equal(35, Event("crescendo-a").Chests.Count);
        Assert.Equal(35, Event("crescendo-b").Chests.Count);
        Assert.Equal("crescendo_b_daily_01", Event("crescendo-b").Missions.Daily[0].Id);
    }

    [Fact]
    public void ACalendarOccurrenceMustNameAServedEventOnce()
    {
        var definition = new GameCatalogEventDefinition(
            "survival", "Survival", new GameCatalogEventRecurrence("None", null, null, null), ["survivalEventId"], null);
        GameCatalogEventOccurrence Occurrence(string id, string survivalEventId) =>
            new(id, "survival", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(7),
                new Dictionary<string, JsonElement> { ["survivalEventId"] = JsonSerializer.SerializeToElement(survivalEventId) });
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateSurvivalEvents(
            [Event("december-2026")],
            ["admecManipulus"],
            [.. Event("december-2026").Waves.SelectMany(wave => wave.Enemies.Select(enemy => enemy.NpcId)
                .Concat(wave.PowerUpsOnClear.Select(powerUp => powerUp.NpcId).OfType<string>()))
                .Concat(Event("december-2026").Scoring.KillScores.Select(score => score.NpcId))],
            [definition],
            [Occurrence("occ-1", "december-2026"), Occurrence("occ-2", "december-2026"), Occurrence("occ-3", "june-2026")],
            errors);

        Assert.Equal(["DuplicateSurvivalOccurrence", "UnknownSurvivalEvent"], errors.Select(error => error.Code));
    }

    [Fact]
    public void AnUnservedFeaturedHeroFailsValidation()
    {
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateSurvivalEvents([Event("may-2026")], [], [], [], [], errors);

        Assert.Contains(errors, error => error.Code == "InvalidSurvivalEvent" && error.Message.Contains("astarCyrus", StringComparison.Ordinal));
    }
}
