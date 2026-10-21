using System.Text.Json;
using TacticusPlanner.Api.Features.V1Import;

namespace TacticusPlanner.Api.Tests;

public sealed class V1ImportParsingTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ParsesCurrentV1OnslaughtPreferencesShapeAndNormalizesSectorNames()
    {
        var data = JsonSerializer.Deserialize<V1UserData>("""
            {
              "onslaughtPreferences": {
                "Imperial": { "sector": "gold", "tier": 2 },
                "Xenos": { "sector": "diamond", "tier": 4 },
                "Chaos": { "sector": "silver", "tier": 3 }
              }
            }
            """, WebJsonOptions);

        var result = TacticusV1Client.ReadOnslaughtProgress(data);

        Assert.True(result.IsPresent);
        Assert.NotNull(result.Progress);
        Assert.Equal(new("Gold", 2), result.Progress.Imperial);
        Assert.Equal(new("Diamond", 4), result.Progress.Xenos);
        Assert.Equal(new("Silver", 3), result.Progress.Chaos);
    }

    [Fact]
    public void ParsesV1ShardSourceFieldsPreviouslyDroppedEntirely()
    {
        // A captured V1 profile payload's goals array: an Ascension goal (combined onslaught + energy
        // farming, plus mythic campaign usage) and an Unlock goal (campaign usage only — V1 never sends
        // shardFarmType/mythicCampaignsUsage for Unlock).
        var data = JsonSerializer.Deserialize<V1UserData>("""
            {
              "goals": [
                {
                  "id": "ascend-1",
                  "character": "Bellator",
                  "type": 2,
                  "priority": 1,
                  "dailyRaids": true,
                  "targetRarity": 3,
                  "targetStars": 8,
                  "shardFarmType": "both",
                  "campaignsUsage": 1,
                  "mythicCampaignsUsage": 2
                },
                {
                  "id": "unlock-1",
                  "character": "Bellator",
                  "type": 3,
                  "priority": 2,
                  "dailyRaids": false,
                  "campaignsUsage": 1
                }
              ]
            }
            """, WebJsonOptions);

        Assert.NotNull(data);
        Assert.NotNull(data.Goals);
        var goals = data.Goals;
        var ascend = Assert.Single(goals, goal => goal.Id == "ascend-1");
        Assert.Equal("both", ascend.ShardFarmType);
        Assert.Equal(1, ascend.CampaignsUsage);
        Assert.Equal(2, ascend.MythicCampaignsUsage);

        var unlock = Assert.Single(goals, goal => goal.Id == "unlock-1");
        Assert.Null(unlock.ShardFarmType);
        Assert.Equal(1, unlock.CampaignsUsage);
        Assert.Null(unlock.MythicCampaignsUsage);
    }

    [Fact]
    public void ParsesOnlyRegularCampaignEventProgressFromCurrentV1Shape()
    {
        var data = JsonSerializer.Deserialize<V1UserData>("""
            {
              "campaignsProgress": {
                "Adeptus Mechanicus Standard": 12,
                "Adeptus Mechanicus Standard Challenge": 2,
                "Adeptus Mechanicus Extremis": 7,
                "Indomitus": 75
              }
            }
            """, WebJsonOptions);

        var result = TacticusV1Client.ReadCampaignEventProgress(data);

        Assert.True(result.IsPresent);
        Assert.Equal(2, result.Progress!.Count);
        Assert.Contains(new V1CampaignEventProgress("eventCampaign1", "Standard", 12), result.Progress);
        Assert.Contains(new V1CampaignEventProgress("eventCampaign1", "Extremis", 7), result.Progress);
    }

}
