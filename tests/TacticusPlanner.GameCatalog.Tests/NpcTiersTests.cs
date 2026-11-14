using System.Text.Json;
using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Utils;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class NpcTiersTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> NpcIds =
        new(["darkaNpc2Hellblaster", "eldarNpc1Guardian", "eldarNpc4Wraithguard", "darkaNpcAzrael"], StringComparer.Ordinal);

    private static readonly HashSet<string> BossIds = new(["darkaNpcAzrael"], StringComparer.Ordinal);

    private static readonly GameCatalogHordeModeTier[] DefaultTiers =
    [
        new("S", 2.0),
        new("A", 1.7),
        new("B", 1.45),
        new("C", 1.2),
        new("D", 1.0),
    ];

    private static GameCatalogNpcTiers Record(params GameCatalogNpcTier[] npcs) =>
        new(GameCatalogDatasets.NpcTiers, DefaultTiers, npcs);

    private static List<GameCatalogValidationError> Validate(params GameCatalogNpcTiers[] records)
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateNpcTiers(records, NpcIds, BossIds, errors);
        return errors;
    }

    private static void AssertSingleError(List<GameCatalogValidationError> errors, params string[] expectedFragments)
    {
        var error = Assert.Single(errors);
        Assert.Equal(GameCatalogDatasets.NpcTiers, error.Dataset);
        Assert.Equal("InvalidNpcTiers", error.Code);
        Assert.Contains("npc-tiers", error.Message, StringComparison.Ordinal);
        foreach (var fragment in expectedFragments)
        {
            Assert.Contains(fragment, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void InitialDatasetListsTheRotationLegendaryEventNpcsWithTheFittedTiers()
    {
        var snapshot = GameCatalogLoader.Load();

        var record = Assert.Single(snapshot.NpcTiersViews);
        Assert.Equal("npc-tiers", record.Id);
        Assert.Equal(["S", "A", "B", "C", "D"], record.Tiers.Select(tier => tier.Tier));
        Assert.Equal([2.2, 1.5, 1.35, 1.15, 1.0], record.Tiers.Select(tier => tier.Multiplier));
        Assert.Equal(44, record.Npcs.Count);
        var bossIds = snapshot.Npcs.Where(npc => npc.IsBoss).Select(npc => npc.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("darkaNpcAzrael", bossIds);
        Assert.Contains("orksBossWarboss", bossIds);
        Assert.DoesNotContain("darkaNpc2Hellblaster", bossIds);
        Assert.DoesNotContain(record.Npcs, npc => bossIds.Contains(npc.NpcId));
        Assert.All(record.Npcs, npc => Assert.Null(npc.Multiplier));

        // Bile NPCs fitted to clear results (Nov 2026); every other NPC stays at D (power is right).
        var tiered = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["darkaNpc2Hellblaster"] = "S",
            ["eldarNpc2Warlock"] = "A",
            ["eldarNpc4Wraithguard"] = "B",
            ["eldarNpc1Guardian"] = "B",
            ["darkaNpc3Terminator"] = "B",
            ["thousNpc1PinkHorror"] = "C",
            ["deathNpc3Nurglings"] = "C",
            // Provisional Lysander, Farsight and Uthar tiers (judged from stats, abilities and traits, not yet fitted).
            ["templNpc4Aggressor"] = "B",
            ["blackNpc2Terminator"] = "B",
            ["admecNpc1Vanguard"] = "B",
            ["astraNpc4Mortar"] = "B",
            ["tauNpc2DroneSniper"] = "B",
            ["tauNpc5StealthSuit"] = "B",
            ["orksNpc2OrkBoy"] = "C",
            ["orksNpc3GrotTank"] = "C",
            ["templNpc2Neophyte"] = "C",
            ["templNpc3InitiatePyreblaster"] = "C",
            ["blackNpc4Havoc"] = "C",
            ["admecNpc3Electropriest"] = "C",
            ["admecNpc2Techpriest"] = "C",
            ["tauNpc1FireWarrior"] = "C",
            ["tauNpc6Carnivore"] = "C",
            ["necroNpc4Destroyer"] = "B",
            ["deathNpc2PlagueMarine"] = "B",
            ["necroNpc3Deathmark"] = "C",
            ["necroNpc5Swarm"] = "C",
            ["deathNpc1Poxwalker"] = "C",
        };
        Assert.All(record.Npcs, npc => Assert.Equal(tiered.GetValueOrDefault(npc.NpcId, "D"), npc.Tier));
        Assert.All(tiered.Keys, npcId => Assert.Contains(record.Npcs, npc => npc.NpcId == npcId));
    }

    [Fact]
    public void DatasetIsListedInTheManifestWithAHash()
    {
        var snapshot = GameCatalogLoader.Load();

        var entry = Assert.Single(snapshot.Manifest.Datasets, dataset => dataset.Key == GameCatalogDatasets.NpcTiers);
        Assert.False(string.IsNullOrWhiteSpace(entry.Hash));
        Assert.Contains(GameCatalogDatasets.NpcTiers, GameCatalogDatasets.Served);
    }

    [Fact]
    public void NpcsAreSortedByIdAndTheHashIgnoresAuthoringOrder()
    {
        var served = GameCatalogDenormalizer.BuildNpcTiers([Record(new("eldarNpc4Wraithguard", "A"), new("darkaNpc2Hellblaster", "B"))]);
        var servedReversed = GameCatalogDenormalizer.BuildNpcTiers([Record(new("darkaNpc2Hellblaster", "B"), new("eldarNpc4Wraithguard", "A"))]);

        Assert.Equal(["darkaNpc2Hellblaster", "eldarNpc4Wraithguard"], Assert.Single(served).Npcs.Select(npc => npc.NpcId));
        Assert.Equal(
            GameCatalogHashing.ComputeCanonicalJsonHash(served, JsonOptions),
            GameCatalogHashing.ComputeCanonicalJsonHash(servedReversed, JsonOptions));
    }

    [Fact]
    public void MissingListsAreServedEmpty()
    {
        var served = Assert.Single(GameCatalogDenormalizer.BuildNpcTiers([Record() with { Tiers = null!, Npcs = null! }]));

        Assert.Empty(served.Tiers);
        Assert.Empty(served.Npcs);
    }

    [Fact]
    public void ValidRecordPasses() =>
        Assert.Empty(Validate(Record(new("darkaNpc2Hellblaster", "A", 1.8), new("eldarNpc1Guardian", "D"))));

    [Fact]
    public void WrongRecordIdFails() =>
        AssertSingleError(Validate(Record() with { Id = "not-the-id" }), "not-the-id");

    [Fact]
    public void MoreThanOneRecordFails() =>
        AssertSingleError(Validate(Record(), Record()), "exactly one record");

    [Fact]
    public void UnknownNpcFails() =>
        AssertSingleError(Validate(Record(new GameCatalogNpcTier("notAnNpc", "A"))), "notAnNpc");

    [Fact]
    public void BossNpcFails() =>
        AssertSingleError(Validate(Record(new GameCatalogNpcTier("darkaNpcAzrael", "D", null))), "darkaNpcAzrael", "boss");

    [Fact]
    public void DuplicateNpcFails() =>
        AssertSingleError(
            Validate(Record(new("eldarNpc1Guardian", "A"), new("eldarNpc1Guardian", "B"))),
            "eldarNpc1Guardian",
            "more than once");

    [Fact]
    public void UnknownTierFails() =>
        AssertSingleError(Validate(Record(new GameCatalogNpcTier("eldarNpc1Guardian", "X"))), "tier 'X'");

    [Fact]
    public void TiersOutOfOrderFail() =>
        AssertSingleError(
            Validate(Record() with { Tiers = [DefaultTiers[1], DefaultTiers[0], .. DefaultTiers[2..]] }),
            "A, S, B, C, D");

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5.01)]
    public void NpcMultiplierOutOfRangeFails(double multiplier) =>
        AssertSingleError(Validate(Record(new GameCatalogNpcTier("eldarNpc1Guardian", "A", multiplier))), "eldarNpc1Guardian", "multiplier");

    [Fact]
    public void TierMultiplierOutOfRangeFails() =>
        AssertSingleError(Validate(Record() with { Tiers = [.. DefaultTiers[..4], new("D", 0)] }), "tier 'D'", "multiplier");

    [Fact]
    public void NpcMultiplierIsSerializedOnlyWhenSet()
    {
        var json = JsonSerializer.Serialize(
            Record(new("eldarNpc1Guardian", "D"), new("darkaNpc2Hellblaster", "A", 1.8)),
            JsonOptions);

        Assert.Contains("\"npcId\":\"darkaNpc2Hellblaster\",\"tier\":\"A\",\"multiplier\":1.8", json, StringComparison.Ordinal);
        Assert.Contains("\"npcId\":\"eldarNpc1Guardian\",\"tier\":\"D\"}", json, StringComparison.Ordinal);
    }
}
