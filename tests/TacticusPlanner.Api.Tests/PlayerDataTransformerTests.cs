using TacticusPlanner.Api.Features.PlayerData;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.TacticusApi.Models.Player;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// Exercises <see cref="PlayerDataTransformer"/> directly against a real (embedded) catalog snapshot,
/// without any HTTP host or database — the transformer's only dependency is <see cref="IGameCatalogProvider"/>.
/// </summary>
public sealed class PlayerDataTransformerTests
{
    private sealed class TestCatalogProvider : IGameCatalogProvider
    {
        public GameCatalogSnapshot Current { get; } = GameCatalogLoader.Load();
    }

    private static PlayerDataTransformer CreateTransformer() => new(new TestCatalogProvider());

    private static PlayerResponse BuildResponse(string configHash = "hash-1") => new()
    {
        Player = new Player
        {
            Details = new PlayerDetails { Name = "Tester", PowerLevel = 100 },
            Units =
            [
                new Unit
                {
                    Id = FakeTacticusApi.CharacterUnitId, // real catalog character id
                    Name = "Tigurius",
                    ProgressionIndex = 11,
                    Xp = 200000,
                    XpLevel = 35,
                    Rank = 12,
                    Shards = 59,
                    MythicShards = 0,
                    Abilities = [new Ability { Id = "StormOfWrath", Level = 35 }],
                    Upgrades = [0, 2, 4],
                    Items = [],
                },
            ],
            Inventory = new Inventory
            {
                Upgrades = [new Upgrade { Id = "upgHpC004", Name = "Health Common", Amount = 3 }],
                Shards = [],
                MythicShards = [],
                XpBooks = [],
                AbilityBadges = new AbilityBadges { Imperial = [], Xenos = [], Chaos = [] },
                Components = [],
                ForgeBadges = [],
                Orbs = new Orbs { Imperial = [], Xenos = [], Chaos = [] },
                Items = [new InventoryEquipment { Id = "I_Booster_Crit_E001", Name = "Crit Booster", Level = 1, Amount = 2 }],
                RequisitionOrders = new RequisitionOrders { Regular = 69, Blessed = 11 },
                ResetStones = 2,
            },
            Progress = new Progress
            {
                Campaigns =
                [
                    new CampaignProgress
                    {
                        Id = FakeTacticusApi.CampaignId, // realigned catalog id -> should resolve
                        Name = "Indomitus",
                        Type = "Standard",
                        Battles =
                        [
                            new CampaignLevel { BattleIndex = 0, AttemptsLeft = 3, AttemptsUsed = 0 },
                            new CampaignLevel { BattleIndex = 2, AttemptsLeft = 3, AttemptsUsed = 0 },
                        ],
                    },
                    new CampaignProgress
                    {
                        Id = FakeTacticusApi.UnmatchedCampaignId, // no catalog cross-reference yet
                        Name = string.Empty,
                        Type = "Standard",
                        Battles = [new CampaignLevel { BattleIndex = 0, AttemptsLeft = 10, AttemptsUsed = 0 }],
                    },
                ],
                LegendaryEvents = [],
            },
        },
        Metadata = new Metadata { ConfigHash = configHash, LastUpdatedOn = 1_780_000_000, Scopes = ["Player"] },
    };

    [Fact]
    public void MapsKnownCatalogUnitIntoCharactersNotMows()
    {
        var result = CreateTransformer().Transform(BuildResponse());

        var character = Assert.Single(result.Characters);
        Assert.Equal(FakeTacticusApi.CharacterUnitId, character.UnitId);
        Assert.Equal(12, character.Rank);
        Assert.Single(character.Abilities);
        Assert.Equal("StormOfWrath", character.Abilities[0].AbilityId);
        Assert.Equal([0, 2, 4], character.AppliedUpgradeSlots);
        Assert.Empty(result.Mows);
    }

    [Fact]
    public void SplitsInventoryUpgradesAndItemsIntoTheirOwnChunksAndKeepsRequisitionOrdersInInventory()
    {
        var result = CreateTransformer().Transform(BuildResponse());

        var upgrade = Assert.Single(result.InventoryUpgrades);
        Assert.Equal("upgHpC004", upgrade.UpgradeId);

        var item = Assert.Single(result.InventoryItems);
        Assert.Equal("I_Booster_Crit_E001", item.ItemId);

        Assert.Equal(69, result.Inventory.RequisitionOrdersRegular);
        Assert.Equal(11, result.Inventory.RequisitionOrdersBlessed);
        Assert.Equal(2, result.Inventory.ResetStones);
    }

    [Fact]
    public void ResolvesCatalogCampaignGroupIdWhenTheCatalogHasAMatchingGroupAndLeavesItNullOtherwise()
    {
        var result = CreateTransformer().Transform(BuildResponse());

        var matched = result.CampaignProgress.Single(c => c.TacticusCampaignId == FakeTacticusApi.CampaignId);
        Assert.Equal(FakeTacticusApi.CampaignId, matched.CatalogCampaignGroupId);
        Assert.Equal(2, matched.HighestObservedBattleIndex);

        var unmatched = result.CampaignEventsProgress.Single(c => c.TacticusCampaignId == FakeTacticusApi.UnmatchedCampaignId);
        Assert.Null(unmatched.CatalogCampaignGroupId);
    }

    [Fact]
    public void HashingIsDeterministicForIdenticalInputAndChangesWithContent()
    {
        var transformer = CreateTransformer();

        var first = transformer.Transform(BuildResponse());
        var second = transformer.Transform(BuildResponse());
        Assert.Equal(first.SourceHash, second.SourceHash);
        Assert.Equal(first.ChunkHashes[PlayerDataChunkKeys.Characters], second.ChunkHashes[PlayerDataChunkKeys.Characters]);

        var differentConfigHash = transformer.Transform(BuildResponse("hash-2"));
        Assert.NotEqual(first.SourceHash, differentConfigHash.SourceHash);
        // Content-only chunk hashes are unaffected by the configHash/version input to the aggregate hash.
        Assert.Equal(first.ChunkHashes[PlayerDataChunkKeys.Characters], differentConfigHash.ChunkHashes[PlayerDataChunkKeys.Characters]);
    }

    [Fact]
    public void ExtractsConfigHashAndLastUpdatedOnFromMetadata()
    {
        var result = CreateTransformer().Transform(BuildResponse("a-config-hash"));

        Assert.Equal("a-config-hash", result.ConfigHash);
        Assert.Equal(1_780_000_000, result.TacticusLastUpdatedOn);
    }
}
