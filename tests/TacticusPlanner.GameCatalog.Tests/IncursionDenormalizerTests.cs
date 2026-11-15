using System.Text.Json;
using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class IncursionDenormalizerTests
{
    private static readonly Lazy<GameCatalogSnapshot> Catalog = new(GameCatalogLoader.Load);

    private static GameCatalogSnapshot Snapshot => Catalog.Value;

    private static GameCatalogIncursion Broadside => Snapshot.IncursionViews.Single(incursion => incursion.MowId == "tauBroadside");

    private static Dictionary<string, JsonElement> Json(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private const string Chests = """{ "mowe_x_00_01": [{ "type": "Bronze", "rewards": ["shards_x:2", "gold:100"] }] }""";

    private static DatamineMowEventNode BattleNode(int index, int depth, string battleId, string? type = "normal", params int[] unlockedBy) =>
        new(depth, "incursionBattle", battleId, index, type, battleId, null, null, unlockedBy.Select(parent => new DatamineMowEventNodeRef(parent)).ToArray(), null);

    private static DatamineMowEventBattle Battle(string id, params string[] enemies) =>
        new(id, 100, "C1_14", 3, new DatamineMowEventLoot(["gold:20-25"], "shards_x:2", "upgArmC005:2", "upgDmgC015:3"), enemies);

    private static DatamineMowEventTier Tier(
        IReadOnlyList<DatamineMowEventBattle>? battles = null,
        IReadOnlyList<DatamineMowEventNode>? nodes = null) =>
        new(
            0,
            "Common",
            battles ?? [Battle("1", "npcA:4", "npcB:3", "npcA:4"), Battle("2", "npcA:5")],
            nodes ??
            [
                BattleNode(0, 0, "1"),
                new(1, "incursionEnhancement", "E", 1, null, null, "Heal_01", null, [new(0)], [2]),
                BattleNode(2, 1, "2", "challenge", 0) with { ExclusiveWith = [1] },
                new(2, "chest", "C", 3, null, null, null, "mowe_x_00_01", [new(1), new(2)], null),
            ]);

    private static DatamineMowEvent Event(params DatamineMowEventTier[] tiers) => Event(1733126400000, tiers);

    private static DatamineMowEvent Event(long availableFrom, params DatamineMowEventTier[] tiers) =>
        new(
            1, 2, 3, 72001, 1, 3,
            [new DatamineMowEventMow("x", availableFrom, false, true, ["Ultramarines"], tiers.Length > 0 ? tiers : [Tier()])],
            new GameCatalogOhSoCloseOffer(new(200, 50), new(200, 50)),
            new Dictionary<string, DatamineMowEventEnhancement>
            {
                ["Heal_01"] = new("Heal", 3, null, false),
                ["Unused_01"] = new("CritBoost", 1, "EnhancementCritBoost", false),
            });

    [Fact]
    public void TheShippedIncursionsAreTheGames()
    {
        Assert.Equal(12, Snapshot.IncursionViews.Count);
        Assert.Equal(
            ["astraOrdnanceBattery", "blackForgefiend", "tyranBiovore"],
            Snapshot.IncursionViews.Take(3).Select(incursion => incursion.MowId));
        Assert.Equal("custoDreadnought", Snapshot.IncursionViews[^1].MowId);
        Assert.Equal(("2024-12-02T08:00:00Z", false, false), (Broadside.AvailableFromUtc, Broadside.New, Broadside.Updated));
        Assert.Equal(new GameCatalogIncursionTokens(2, 3, 72001, 1, 1), Broadside.Tokens);
        Assert.Equal(3, Broadside.MinUnitsOfGrandAllianceNeeded);
        Assert.Equal(10, Broadside.AllowedFactions.Count);
    }

    [Fact]
    public void TheShippedTiersRunCommonToMythic()
    {
        Assert.All(Snapshot.IncursionViews, incursion => Assert.Equal(
            ["Common", "Common", "Uncommon", "Uncommon", "Rare", "Rare", "Epic", "Epic", "Epic", "Legendary", "Legendary", "Legendary", "Mythic", "Mythic"],
            incursion.Tiers.Select(tier => tier.Rarity)));
        Assert.Equal((12, 26), (Broadside.Tiers[0].Battles.Count, Broadside.Tiers[0].Nodes.Count));
        Assert.Equal((15, 33), (Broadside.Tiers[^1].Battles.Count, Broadside.Tiers[^1].Nodes.Count));
    }

    [Fact]
    public void TheShippedFirstBattleMatchesTheGame()
    {
        var battle = Broadside.Tiers[0].Battles[0];

        Assert.Equal(("1", "EC3_11", false, 100, (int?)3), (battle.Id, battle.BoardId, battle.Challenge, battle.DifficultyMultiplier, battle.LightningVictoryTurns));
        Assert.Equal(["gold:20-25"], battle.BaseRewards);
        Assert.Equal(["shards_tauBroadside:2", "upgArmC005:2", "upgDmgC015:3"], battle.StarRewards);
        Assert.Equal(
            [new GameCatalogIncursionEnemy("tauNpc1FireWarrior", 4, 2), new("tauNpc3DroneShield", 3, 5), new("tauNpc6Carnivore", 3, 2)],
            battle.Enemies);
    }

    [Fact]
    public void TheShippedRunMapsInlineChestsAndEnhancements()
    {
        var nodes = Broadside.Tiers[0].Nodes;

        Assert.Equal(("battle", "1", 0), (nodes[0].Kind, nodes[0].BattleId, nodes[0].UnlockedBy.Count));
        Assert.Equal(("enhancement", "Heal_01"), (nodes[2].Kind, nodes[2].EnhancementId));
        Assert.Equal([1], nodes[2].ExclusiveWith);
        Assert.Equal(("chest", "mowe_tauBroadside_00_01"), (nodes[7].Kind, nodes[7].Chest!.Id));
        Assert.Equal(new GameCatalogIncursionEnhancement("Heal", null, 3, false), Broadside.Enhancements["Heal_01"]);
        Assert.Equal(
            Broadside.Tiers.SelectMany(tier => tier.Nodes).Select(node => node.EnhancementId).OfType<string>().Distinct().Order(StringComparer.Ordinal),
            Broadside.Enhancements.Keys);
        Assert.Contains(Snapshot.IncursionViews.SelectMany(incursion => incursion.Tiers).SelectMany(tier => tier.Battles), battle => battle.Challenge);
        Assert.Contains("mythicShards_tauBroadside:4", Broadside.Tiers[^1].Nodes.Where(node => node.Chest is not null).SelectMany(node => node.Chest!.Rewards));
    }

    [Fact]
    public void ASmallEventReadsEveryPart()
    {
        var incursion = Assert.Single(GameCatalogDenormalizer.ReadDatamineIncursions([Event()], Json(Chests)));
        var tier = Assert.Single(incursion.Tiers);

        Assert.Equal(["Heal_01"], incursion.Enhancements.Keys);
        Assert.Equal([false, true], tier.Battles.Select(battle => battle.Challenge));
        Assert.Equal([new GameCatalogIncursionEnemy("npcA", 4, 2), new("npcB", 3, 1)], tier.Battles[0].Enemies);
        Assert.Equal(["battle", "enhancement", "battle", "chest"], tier.Nodes.Select(node => node.Kind));
        Assert.Equal([1, 2], tier.Nodes[3].UnlockedBy);
        Assert.Equal("Bronze", tier.Nodes[3].Chest!.Type);
        Assert.Equal(["shards_x:2", "gold:100"], tier.Nodes[3].Chest!.Rewards);
    }

    public static TheoryData<string, DatamineMowEvent, string> InvalidSources() => new()
    {
        { "unknown battle '9'", Event(Tier(nodes: [BattleNode(0, 0, "9")])), Chests },
        { "unknown enhancement 'Nope'", Event(Tier(nodes: [BattleNode(0, 0, "1"), new(1, "incursionEnhancement", "E", 1, null, null, "Nope", null, [new(0)], null)])), Chests },
        { "chest 'mowe_x_00_01' is missing", Event(), "{}" },
        { "unknown type 'portal'", Event(Tier(nodes: [BattleNode(0, 0, "1"), new(1, "portal", "P", 1, null, null, null, null, [new(0)], null)])), Chests },
        { "invalid unlockedBy", Event(Tier(nodes: [BattleNode(0, 0, "1"), BattleNode(1, 1, "2", "normal", 5)])), Chests },
        { "is not npcId:level", Event(Tier(battles: [Battle("1", "npcA"), Battle("2", "npcA:1")])), Chests },
        { "positive Unix time", Event(0), Chests },
        { "three star rewards", Event(Tier(battles: [Battle("1", "npcA:1") with { Loot = null }, Battle("2", "npcA:1")])), Chests },
    };

    [Theory]
    [MemberData(nameof(InvalidSources))]
    public void AnUnmappableSourceFailsTheLoad(string expected, DatamineMowEvent mowEvent, string chests)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GameCatalogDenormalizer.ReadDatamineIncursions([mowEvent], Json(chests)));

        Assert.Contains("'incursions'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShippedIncursionsResolveTheirReferences()
    {
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateIncursions(Snapshot.IncursionViews, Snapshot.MowList, Snapshot.NpcList, Snapshot.FactionViews, errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void UnknownReferencesFailValidation()
    {
        var tier = Broadside.Tiers[0];
        var incursion = Broadside with
        {
            MowId = "nobody",
            AllowedFactions = ["Squats"],
            Tiers = [tier with { Rarity = "Shiny", Battles = [tier.Battles[0] with { Enemies = [new("ghost", 1, 1)] }] }],
        };
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateIncursions([incursion], Snapshot.MowList, Snapshot.NpcList, Snapshot.FactionViews, errors);

        Assert.All(errors, error => Assert.Equal("InvalidIncursion", error.Code));
        Assert.Contains(errors, error => error.Message.Contains("not for a served MoW", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Message.Contains("faction 'Squats'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Message.Contains("rarity 'Shiny'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Message.Contains("enemy 'ghost'", StringComparison.Ordinal));
    }
}
