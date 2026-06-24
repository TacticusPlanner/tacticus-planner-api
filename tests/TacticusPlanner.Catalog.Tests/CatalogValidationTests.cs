using System.Text.Json;
using Xunit;

namespace TacticusPlanner.Catalog.Tests;

public sealed class CatalogValidationTests
{
    [Fact]
    public void EmbeddedSnapshotLoadsAllDatasets()
    {
        var snapshot = LoadSnapshot();

        Assert.NotEmpty(snapshot.SourceHash);
        Assert.NotEmpty(snapshot.Units);
        Assert.NotEmpty(snapshot.Mows);
        Assert.NotEmpty(snapshot.Upgrades);
        Assert.NotEmpty(snapshot.Equipment);
        Assert.NotEmpty(snapshot.Campaigns);
        Assert.NotEmpty(snapshot.CampaignEvents);
        Assert.NotEmpty(snapshot.CampaignBattles);
        Assert.NotEmpty(snapshot.Lres);

        foreach (var requiredDataset in CatalogDatasets.Required)
        {
            Assert.True(snapshot.DatasetHashes.ContainsKey(requiredDataset), $"Missing hash for {requiredDataset}.");
        }
    }

    [Fact]
    public void CatalogValidationPassesForEmbeddedSnapshot()
    {
        var snapshot = LoadSnapshot();

        var errors = CatalogValidator.Validate(snapshot);

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

        var originalHash = CatalogHashing.ComputeCanonicalJsonHash(original.RootElement);

        Assert.Equal(originalHash, CatalogHashing.ComputeCanonicalJsonHash(reformatted.RootElement));
        Assert.NotEqual(originalHash, CatalogHashing.ComputeCanonicalJsonHash(changed.RootElement));
    }

    [Fact]
    public void DerivedViewsAreAvailableAndDeterministic()
    {
        var snapshot = LoadSnapshot();

        Assert.NotEmpty(snapshot.CampaignEvents);
        Assert.All(snapshot.Campaigns, campaign => Assert.NotEqual("event", campaign.ReleaseType));
        Assert.All(snapshot.CampaignEvents, campaign => Assert.Equal("event", campaign.ReleaseType));

        var farmableReward = snapshot.UpgradeFarmLocations.First(pair => pair.Value.Count > 0);
        Assert.NotEmpty(farmableReward.Key);
        Assert.All(farmableReward.Value, battleId => Assert.True(snapshot.CampaignBattlesById.ContainsKey(battleId)));

        var craftedUpgrade = snapshot.Upgrades.First(upgrade => upgrade.Craftable && upgrade.Recipe.Count > 0);
        Assert.True(snapshot.ExpandedUpgradeRecipes.TryGetValue(craftedUpgrade.Id, out var expandedRecipe));
        Assert.NotNull(expandedRecipe);
        Assert.NotEmpty(expandedRecipe);
    }

    private static CatalogSnapshot LoadSnapshot()
    {
        var serviceCollection = CatalogServiceCollectionExtensions.AddCatalog(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        );
        using var services =
            Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(serviceCollection);

        return Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<ICatalogProvider>(services)
            .Current;
    }
}
