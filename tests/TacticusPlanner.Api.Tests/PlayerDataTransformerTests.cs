using TacticusPlanner.Api.Features.PlayerData;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.Persistence.Users.PlayerData;
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

    /// <summary>Real catalog MoW id (Ultramarines, see units/units-ultramarines.json).</summary>
    private const string MowUnitId = "ultraDreadnought";

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
                    Items = [new Equipment { SlotId = "Slot3", Id = "I_Booster_Crit_E001", Name = "Crit Booster", Rarity = "Epic", Level = 1 }],
                },
                new Unit
                {
                    Id = MowUnitId, // real catalog MoW id — no rank/equipment on the mapped record
                    Name = "Dreadnought",
                    ProgressionIndex = 5,
                    Xp = 1000,
                    XpLevel = 10,
                    Rank = 0,
                    Shards = 3,
                    MythicShards = 0,
                    Abilities = [],
                    Upgrades = [],
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
                Components =
                [
                    new MoWComponent { Name = "Plasma Core", GrandAlliance = "Imperial", Amount = 4 },
                    new MoWComponent { Name = "Void Shield", GrandAlliance = "Imperial", Amount = 6 },
                    new MoWComponent { Name = "Warp Fragment", GrandAlliance = "Chaos", Amount = 2 },
                ],
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
                        Id = FakeTacticusApi.CampaignId, // real catalog groupId (see GameCatalogDatasets)
                        Name = "Indomitus",
                        Type = "Standard",
                        Battles =
                        [
                            // BattleIndex 0 has never been attempted (AttemptsUsed 0) — excluded from
                            // LiveProgress.BattleAttempts, but still counts toward the high-water mark.
                            new CampaignLevel { BattleIndex = 0, AttemptsLeft = 3, AttemptsUsed = 0 },
                            new CampaignLevel { BattleIndex = 2, AttemptsLeft = 2, AttemptsUsed = 1 },
                        ],
                    },
                    new CampaignProgress
                    {
                        Id = FakeTacticusApi.EventCampaignId,
                        Name = string.Empty,
                        Type = "Standard",
                        Battles = [new CampaignLevel { BattleIndex = 0, AttemptsLeft = 9, AttemptsUsed = 1 }],
                    },
                ],
                LegendaryEvents =
                [
                    new LegendaryEvent
                    {
                        Id = "emperLucius",
                        CurrentPoints = 100,
                        CurrentCurrency = 5,
                        CurrentShards = 2,
                        CurrentClaimedChestIndex = 1,
                        Lanes =
                        [
                            new LegendaryEventLane
                            {
                                Id = 1,
                                Name = "Alpha",
                                Progress =
                                [
                                    new LegendaryEventProgress { ObjectivesCleared = [0, 1], HighScore = 25, EncounterPoints = 25 },
                                ],
                            },
                            new LegendaryEventLane
                            {
                                Id = 3,
                                Name = "Gamma",
                                Progress = [],
                            },
                        ],
                    },
                ],
            },
        },
        Metadata = new Metadata { ConfigHash = configHash, LastUpdatedOn = 1_780_000_000, Scopes = ["Player"] },
    };

    [Fact]
    public void SplitsUnitsIntoCharactersAndMowsByRealCatalogId()
    {
        var result = CreateTransformer().Transform(BuildResponse());

        var character = Assert.Single(result.Characters);
        Assert.Equal(FakeTacticusApi.CharacterUnitId, character.UnitId);
        Assert.Equal(PlayerRank.Gold1, character.Rank);
        Assert.Equal(PlayerProgression.EpicRedThreeStars, character.ProgressionIndex);
        Assert.Single(character.Abilities);
        Assert.Equal("StormOfWrath", character.Abilities[0].AbilityId);
        Assert.Equal([0, 2, 4], character.AppliedUpgradeSlots);
        var equipped = Assert.Single(character.EquippedItems);
        Assert.Equal("I_Booster_Crit_E001", equipped.EquipmentId);

        var mow = Assert.Single(result.Mows);
        Assert.Equal(MowUnitId, mow.UnitId);
        // PlayerMowRecord has no Rank/EquippedItems properties at all — enforced at compile time, not
        // just by a runtime assertion (MoWs don't have ranks or equipment slots).
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
    public void SumsMowComponentsPerGrandAllianceInsteadOfKeepingPerItemRows()
    {
        var result = CreateTransformer().Transform(BuildResponse());

        Assert.Equal(10, result.Inventory.Components.Imperial.Amount); // 4 + 6
        Assert.Equal(0, result.Inventory.Components.Xenos.Amount);
        Assert.Equal(2, result.Inventory.Components.Chaos.Amount);
    }

    [Fact]
    public void SplitsCampaignProgressByEventIdAndSimplifiesToIdentityPlusHighWaterMark()
    {
        var result = CreateTransformer().Transform(BuildResponse());

        var standard = Assert.Single(result.CampaignProgress);
        Assert.Equal(FakeTacticusApi.CampaignId, standard.TacticusCampaignId);
        Assert.Equal("Standard", standard.Type);
        Assert.Equal(2, standard.HighestCompletedBattleIndex);

        var evt = Assert.Single(result.CampaignEventsProgress);
        Assert.Equal(FakeTacticusApi.EventCampaignId, evt.TacticusCampaignId);
    }

    [Fact]
    public void PopulatesLiveProgressWithBattleAttemptsAndTheActiveCampaignEventId()
    {
        var result = CreateTransformer().Transform(BuildResponse());

        // Only battles with AttemptsUsed > 0 are kept: 1 standard + 1 event battle (BattleIndex 0's
        // untouched standard battle is excluded).
        Assert.Equal(2, result.LiveProgress.BattleAttempts.Count);
        Assert.Contains(result.LiveProgress.BattleAttempts, b =>
            b.TacticusCampaignId == FakeTacticusApi.CampaignId && b.BattleIndex == 2 && b.AttemptsLeft == 2);
        Assert.DoesNotContain(result.LiveProgress.BattleAttempts, b =>
            b.TacticusCampaignId == FakeTacticusApi.CampaignId && b.BattleIndex == 0);

        Assert.Equal(FakeTacticusApi.EventCampaignId, result.LiveProgress.ActiveCampaignEventId);
    }

    [Fact]
    public void MapsLreLanesOntoNamedAlphaBetaGammaTracksMatchingTheCatalogsLreModel()
    {
        var result = CreateTransformer().Transform(BuildResponse());

        var lre = Assert.Single(result.LreProgress);
        Assert.Equal("emperLucius", lre.Id);
        Assert.NotNull(lre.Alpha);
        Assert.Single(lre.Alpha!.Encounters);
        Assert.Equal(25, lre.Alpha.Encounters[0].HighScore);
        Assert.Null(lre.Beta);
        Assert.NotNull(lre.Gamma);
        Assert.Empty(lre.Gamma!.Encounters);
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
