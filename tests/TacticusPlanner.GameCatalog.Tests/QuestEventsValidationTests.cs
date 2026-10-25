using System.Globalization;
using System.Text.Json;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class QuestEventsValidationTests
{
    private static readonly GameCatalogSnapshot Snapshot = GameCatalogLoader.Load();
    private static readonly GameCatalogRawQuestEvent Nubari = Snapshot.QuestEvents.Single(quest => quest.UnitId == "astarEradicator");

    private static List<GameCatalogValidationError> Validate(
        GameCatalogRawQuestEvent quest, IReadOnlyList<GameCatalogQuestCrate>? crates = null)
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateQuestEvents(
            [quest],
            crates ?? Snapshot.QuestCrates,
            new HashSet<string>(StringComparer.Ordinal) { "astarEradicator" },
            new HashSet<string>(StringComparer.Ordinal) { "AdeptusAstartes", "Ultramarines", "Sisterhood", "BloodAngels", "BlackLegion" },
            new HashSet<string>(Snapshot.Npcs.Select(npc => npc.Id), StringComparer.Ordinal),
            new HashSet<string>(Snapshot.Upgrades.Select(upgrade => upgrade.Id), StringComparer.Ordinal),
            errors);
        return errors;
    }

    private static GameCatalogRawQuestEvent WithBattle(int number, Func<GameCatalogRawQuestBattle, GameCatalogRawQuestBattle> change) =>
        Nubari with
        {
            Tiers = Nubari.Tiers
                .Select(tier => tier with { Battles = tier.Battles.Select(battle => battle.BattleNr == number.ToString(CultureInfo.InvariantCulture) ? change(battle) : battle).ToArray() })
                .ToArray(),
        };

    private static void AssertSingleError(List<GameCatalogValidationError> errors, string code)
    {
        var error = Assert.Single(errors);
        Assert.Equal(code, error.Code);
    }

    [Fact]
    public void ShippedDataLoads() => Assert.Empty(Validate(Nubari));

    [Fact]
    public void AWrongTierSplitFails()
    {
        var rare = Nubari.Tiers[2];
        var broken = Nubari with { Tiers = [.. Nubari.Tiers.Take(2), rare with { Battles = rare.Battles.Take(4).ToArray() }, .. Nubari.Tiers.Skip(3)] };

        var errors = Validate(broken);

        Assert.Contains(errors, error => error.Code == "InvalidQuestBattles" && error.Dataset == GameCatalogDatasets.QuestEvents);
    }

    [Fact]
    public void AScoreTotalThatMissesTheLastCrateFails()
    {
        var broken = WithBattle(1, battle => battle with { Objectives = battle.Objectives.Take(1).ToArray() });

        var error = Assert.Single(Validate(broken));
        Assert.Equal("InvalidQuestScore", error.Code);
        Assert.Contains("8700", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownEnemyFails() =>
        AssertSingleError(Validate(WithBattle(1, battle => battle with { Enemies = [.. battle.Enemies, new GameCatalogRawQuestEnemy("nobody", 1)] })), "MissingReference");

    [Fact]
    public void AnUnknownUpgradeFails() =>
        AssertSingleError(Validate(WithBattle(1, battle => battle with { Loot = new(new("upgNothing", 3, 4)) })), "MissingReference");

    [Fact]
    public void AnInvalidLootChanceFails() =>
        AssertSingleError(Validate(WithBattle(1, battle => battle with { Loot = new(new(battle.Loot.Reward.UpgradeId, 5, 4)) })), "InvalidLootChance");

    [Fact]
    public void APowerBelowOneFails() =>
        AssertSingleError(Validate(WithBattle(1, battle => battle with { Power = 0 })), "InvalidPower");

    [Fact]
    public void AnUnknownObjectiveFails() =>
        AssertSingleError(
            Validate(WithBattle(2, battle => battle with { Objectives = [new GameCatalogRawQuestObjective("WinFast", null, 100)] })),
            "UnknownObjective");

    [Fact]
    public void AMissingTargetFails() =>
        AssertSingleError(
            Validate(WithBattle(2, battle => battle with { Objectives = [new GameCatalogRawQuestObjective("NotTrait", null, 100)] })),
            "MissingObjectiveTarget");

    [Fact]
    public void AHitTargetBelowOneFails() =>
        AssertSingleError(
            Validate(WithBattle(2, battle => battle with
            {
                Objectives = [new GameCatalogRawQuestObjective("MaxHits", JsonDocument.Parse("0").RootElement, 100)],
            })),
            "InvalidObjectiveTarget");

    [Fact]
    public void AnUnknownFactionFails() =>
        AssertSingleError(Validate(Nubari with { EnemyFactions = ["Nobody"] }), "MissingReference");

    [Fact]
    public void CratesOutOfOrderFail()
    {
        var crates = Snapshot.QuestCrates.Select(crate => crate.Number == 5 ? crate with { Points = 100 } : crate).ToArray();

        Assert.Contains(Validate(Nubari, crates), error => error.Code == "InvalidCrates" && error.Dataset == GameCatalogDatasets.QuestCrates);
    }

    [Fact]
    public void AnUnknownCrateRewardFails()
    {
        var crates = Snapshot.QuestCrates
            .Select(crate => crate.Number == 1 ? crate with { Rewards = [new GameCatalogQuestCrateReward("Shards", 1, 1, null, null)] } : crate)
            .ToArray();

        AssertSingleError(Validate(Nubari, crates), "InvalidCrateReward");
    }
}
