using System.Text.Json;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

/// <summary>
/// Every campaign in the pinned datamine's <c>battles/campaigns.json</c> is curated in
/// <c>Data/curated/campaign-curation.json</c>. The loader reads only curated campaigns, so a datamine bump that
/// ships a new campaign (such as a new Campaign Event) would otherwise leave it silently unserved.
/// </summary>
public sealed class CampaignCurationCoverageTests
{
    [Fact]
    public void EveryDatamineCampaignIsCurated()
    {
        var datamineDir = DatamineSourceTests.ShippedDatamineDir();
        var curationPath = Path.Join(Path.GetDirectoryName(datamineDir), "curated", "campaign-curation.json");

        using var datamine = JsonDocument.Parse(File.ReadAllText(Path.Join(datamineDir, "raw", "gameconfig", "battles", "campaigns.json")));
        var datamineCampaigns = datamine.RootElement.EnumerateObject()
            .SelectMany(kind => kind.Value.EnumerateArray())
            .Select(campaign => campaign.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        using var curation = JsonDocument.Parse(File.ReadAllText(curationPath));
        var curatedCampaigns = curation.RootElement.EnumerateObject()
            .SelectMany(group => group.Value.GetProperty("campaigns").EnumerateArray())
            .Select(source => source.GetProperty("campaign").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        var missing = datamineCampaigns.Except(curatedCampaigns).Order(StringComparer.Ordinal).ToArray();
        Assert.True(
            missing.Length == 0,
            $"The datamine has campaigns campaign-curation.json does not curate: {string.Join(", ", missing)}. "
            + "Add a campaign-curation.json entry and a GameCatalogDatasets.CampaignBattleGroups key for each "
            + "(and the apps repo's campaign name, icon and id mapping).");
    }
}
