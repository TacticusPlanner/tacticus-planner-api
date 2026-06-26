using System.Text.Json;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class GameCatalogValidationTests
{
    [Fact]
    public void EmbeddedSnapshotLoadsAllDatasets()
    {
        var snapshot = LoadSnapshot();

        Assert.NotEmpty(snapshot.SourceHash);
        Assert.NotEmpty(snapshot.Characters);
        Assert.NotEmpty(snapshot.Mows);
        Assert.NotEmpty(snapshot.MowUpgradeCosts);
        Assert.NotEmpty(snapshot.Npcs);
        Assert.NotEmpty(snapshot.Upgrades);
        Assert.NotEmpty(snapshot.Equipment);
        Assert.NotEmpty(snapshot.DropChances);
        Assert.NotEmpty(snapshot.CampaignBattles);
        Assert.NotEmpty(snapshot.Lres);

        Assert.All(GameCatalogDatasets.UnitFactions, key =>
        {
            Assert.True(snapshot.UnitsByFaction.TryGetValue(key, out var faction), $"Missing unit faction {key}.");
            Assert.NotEmpty(faction!.Characters);
        });
        Assert.All(GameCatalogDatasets.NpcFactions, key =>
        {
            Assert.True(snapshot.NpcsByFaction.TryGetValue(key, out var faction), $"Missing npc faction {key}.");
            Assert.NotEmpty(faction!.Npcs);
        });
        Assert.All(GameCatalogDatasets.CampaignBattleGroups, key =>
        {
            Assert.True(snapshot.CampaignGroups.TryGetValue(key, out var group), $"Missing campaign group {key}.");
            Assert.NotEmpty(group!.Faction);
            Assert.NotEmpty(group.CoreCharacters);
            Assert.NotEmpty(group.Difficulties);
            Assert.NotEmpty(group.Battles);
        });
        Assert.All(GameCatalogDatasets.EquipmentTypes, key =>
        {
            Assert.True(snapshot.EquipmentByType.TryGetValue(key, out var items), $"Missing equipment type {key}.");
            Assert.NotEmpty(items!);
        });
        Assert.All(GameCatalogDatasets.UpgradeRarities, key =>
        {
            Assert.True(snapshot.UpgradesByRarity.TryGetValue(key, out var items), $"Missing upgrade rarity {key}.");
            Assert.NotEmpty(items!);
        });
        Assert.All(GameCatalogDatasets.LreEvents, key =>
            Assert.True(snapshot.LresByEvent.ContainsKey(key), $"Missing LRE event {key}."));

        // The manifest now exposes the denormalized served datasets (hashes computed over each projection).
        foreach (var servedDataset in GameCatalogDatasets.Served)
        {
            Assert.True(snapshot.DatasetHashes.ContainsKey(servedDataset), $"Missing hash for {servedDataset}.");
        }

        Assert.Equal("1.40", snapshot.GameVersion);
        Assert.NotEmpty(snapshot.CharacterViews);
        Assert.NotEmpty(snapshot.NpcList);
        Assert.NotEmpty(snapshot.MowList);
        Assert.NotEmpty(snapshot.MowUpgradeCosts);
        Assert.NotEmpty(snapshot.UpgradeViews);
        Assert.NotEmpty(snapshot.EquipmentViews);
        // Equipment carries its matched per-rarity upgrade-cost ladder inlined (no shared extras table).
        Assert.Contains(snapshot.EquipmentViews, item => item.UpgradeLevels.Count > 0);
        Assert.NotEmpty(snapshot.CampaignBattleViews);
        Assert.Contains(snapshot.CampaignBattleViews, battle => !string.IsNullOrEmpty(battle.CampaignGroupId));
        Assert.NotEmpty(snapshot.CampaignDefinitionViews);
        Assert.Contains(snapshot.CampaignDefinitionViews, definition => definition.BattleIds.Count > 0);
        Assert.NotEmpty(snapshot.LreViews);
        // LRE id is the unit snowprint id (a non-numeric string), not the old numeric event id.
        Assert.All(snapshot.LreViews, lre => Assert.False(string.IsNullOrWhiteSpace(lre.Id)));
        Assert.Contains(snapshot.LreViews, lre => lre.Id == "emperLucius");
    }

    [Fact]
    public void DenormalizedViewsResolveCrossReferences()
    {
        var snapshot = LoadSnapshot();

        // A craftable upgrade exposes its expanded recipe split into base vs crafted totals.
        var crafted = snapshot.UpgradeViews.First(upgrade => upgrade.Craftable && upgrade.Recipe.Count > 0);
        Assert.NotNull(crafted.Expanded);
        Assert.True(crafted.Expanded!.TotalBaseCount > 0);
        Assert.Equal(crafted.Expanded.TotalBaseCount, crafted.Expanded.BaseUpgrades.Values.Sum());
        Assert.Equal(crafted.Expanded.TotalCraftedCount, crafted.Expanded.CraftedUpgrades.Values.Sum());

        // At least one upgrade is farmable, with inlined drop-chance numbers on potential locations.
        var farmable = snapshot.UpgradeViews.First(upgrade => upgrade.FarmLocations.Count > 0);
        Assert.All(farmable.FarmLocations, location =>
            Assert.True(location.Guaranteed || location.EffectiveRate is > 0));

        // Every LRE track resolves a non-empty available-units roster from its allowed-units filter,
        // and carries the imported static battle/enemy data.
        Assert.All(snapshot.LreViews, lre =>
        {
            Assert.NotEmpty(lre.Alpha.AvailableUnitIds);
            Assert.NotEmpty(lre.Beta.AvailableUnitIds);
            Assert.NotEmpty(lre.Gamma.AvailableUnitIds);

            foreach (var track in new[] { lre.Alpha, lre.Beta, lre.Gamma })
            {
                Assert.Equal(18, track.Battles.Count);
                Assert.NotEmpty(track.DefeatAll);
                Assert.All(track.Battles, battle =>
                {
                    Assert.NotEmpty(battle.Waves);
                    Assert.All(battle.Waves, wave => Assert.NotEmpty(wave.Enemies));
                });
            }
        });

        // Characters carry faction/alliance and at least some have shard locations + eligible equipment.
        Assert.All(snapshot.CharacterViews, character =>
        {
            Assert.NotEmpty(character.Faction);
            Assert.NotEmpty(character.Alliance);
        });
        Assert.Contains(snapshot.CharacterViews, character => character.ShardLocations.Count > 0);
        Assert.Contains(snapshot.CharacterViews, character =>
            character.EligibleEquipment.Any(slot => slot.EquipmentIds.Count > 0));
    }

    [Fact]
    public void GameCatalogValidationPassesForEmbeddedSnapshot()
    {
        var snapshot = LoadSnapshot();

        var errors = GameCatalogValidator.Validate(snapshot);

        Assert.Empty(errors);
    }

    [Fact]
    public void CanonicalJsonHashIgnoresFormattingAndChangesWithContent()
    {
        using var original = JsonDocument.Parse("""{"b":2,"a":["x","y"]}""");
        using var reformatted = JsonDocument.Parse("""
            {
              "a": [
                "x",
                "y"
              ],
              "b": 2
            }
            """);
        using var changed = JsonDocument.Parse("""{"b":3,"a":["x","y"]}""");

        var originalHash = GameCatalogHashing.ComputeCanonicalJsonHash(original.RootElement);

        Assert.Equal(originalHash, GameCatalogHashing.ComputeCanonicalJsonHash(reformatted.RootElement));
        Assert.NotEqual(originalHash, GameCatalogHashing.ComputeCanonicalJsonHash(changed.RootElement));
    }

    [Fact]
    public void DerivedViewsAreAvailableAndDeterministic()
    {
        var snapshot = LoadSnapshot();

        Assert.All(snapshot.CampaignGroups.Values, group =>
        {
            Assert.NotEmpty(group.Difficulties);
            Assert.NotEmpty(group.CoreCharacters);
        });
        Assert.Contains(snapshot.CampaignGroups.Values, group => string.Equals(group.ReleaseType, "event", StringComparison.Ordinal));
        Assert.Contains(snapshot.CampaignGroups.Values, group => string.Equals(group.ReleaseType, "standard", StringComparison.Ordinal));

        var farmableReward = snapshot.UpgradeFarmLocations.First(pair => pair.Value.Count > 0);
        Assert.NotEmpty(farmableReward.Key);
        Assert.All(farmableReward.Value, battleId => Assert.True(snapshot.CampaignBattlesById.ContainsKey(battleId)));

        var craftedUpgrade = snapshot.Upgrades.First(upgrade => upgrade.Craftable && upgrade.Recipe.Count > 0);
        Assert.True(snapshot.ExpandedUpgradeRecipes.TryGetValue(craftedUpgrade.Id, out var expandedRecipe));
        Assert.NotNull(expandedRecipe);
        Assert.NotEmpty(expandedRecipe);
    }

    private static GameCatalogSnapshot LoadSnapshot()
    {
        var serviceCollection = GameCatalogServiceCollectionExtensions.AddGameCatalog(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        );
        using var services =
            Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(serviceCollection);

        return Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<IGameCatalogProvider>(services)
            .Current;
    }
}
