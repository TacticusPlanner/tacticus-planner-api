using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.V1Import;
using TacticusPlanner.Domain.Teams;

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


    private static V1TeamImportData ReadTeams(string json) =>
        TacticusV1Client.ReadTeams(JsonSerializer.Deserialize<V1UserData>(json, WebJsonOptions)?.Teams2);

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"teams2": null}""")]
    [InlineData("""{"teams2": []}""")]
    public void AbsentOrEmptyTeams2IsMissing(string json)
    {
        var result = ReadTeams(json);

        Assert.False(result.IsPresent);
        Assert.Null(result.Teams);
    }

    [Theory]
    [InlineData("""{"teams2": {}}""")]
    [InlineData("""{"teams2": "teams"}""")]
    public void ANonArrayTeams2IsInvalid(string json)
    {
        var result = ReadTeams(json);

        Assert.True(result.IsPresent);
        Assert.Null(result.Teams);
    }

    [Fact]
    public void Teams2EntriesAreReadLeniently()
    {
        var result = ReadTeams("""
            {
              "teams2": [
                {
                  "name": "GW Ultras", "priority": 2, "chars": ["ultraCalgar", 7, "ultraTitus"], "flexIndex": 2,
                  "mows": ["ultraDreadnought"], "warOffense": true, "raid": true, "ta": false,
                  "campaignStoryline": "tau", "incursionMows": ["orksRukkatrukk"], "notes": "Keep Titus flex"
                },
                { "name": "No chars", "chars": "ultraCalgar" },
                { "chars": [] },
                "not an object"
              ]
            }
            """);

        Assert.True(result.IsPresent);
        var teams = Assert.IsAssignableFrom<IReadOnlyList<V1TeamSource>>(result.Teams);
        Assert.Equal(4, teams.Count);
        var first = teams[0];
        Assert.Equal(("GW Ultras", 2d, 2), (first.Name, first.Priority, first.FlexIndex));
        Assert.Equal(["ultraCalgar", "7", "ultraTitus"], first.Chars);
        Assert.Equal(["ultraDreadnought"], first.Mows);
        Assert.Equal([TeamMode.WarOffense, TeamMode.GuildRaid], first.Modes);
        Assert.Equal(("tau", "Keep Titus flex"), (first.CampaignStoryline, first.Notes));
        Assert.Equal(["orksRukkatrukk"], first.IncursionMows);
        Assert.Equal(("No chars", null), (teams[1].Name, teams[1].Chars));
        Assert.Null(teams[2].Name);
        Assert.Equal((null, null), (teams[3].Name, teams[3].Chars));
    }

    [Fact]
    public async Task TheRealClientReadsTeamsFromTheProfileResponse()
    {
        const string body = """
            {
              "tacticusApiKey": null,
              "data": {
                "teams2": [
                  { "name": "GW ultras", "chars": ["ultraCalgar", "ultraTitus", "ultraApothecary"], "guildWar": true },
                  { "name": "Horde", "chars": ["blackAbaddon", "blackPoxwalker", "blackHaarken"], "horde": true }
                ]
              }
            }
            """;
        var services = new ServiceCollection().AddTacticusV1Client("https://v1.example.test", "functions-key");
        services.AddHttpClient(TacticusV1Client.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new ProfileHandler(body));
        using var provider = services.BuildServiceProvider();

        var profile = await new TacticusV1Client(provider.GetRequiredService<IHttpClientFactory>())
            .GetProfileAsync("token", TestContext.Current.CancellationToken);

        Assert.NotNull(profile?.Teams);
        Assert.True(profile.Teams.IsPresent);
        Assert.Equal(["GW ultras", "Horde"], profile.Teams.Teams!.Select(team => team.Name));
        Assert.Equal(["ultraCalgar", "ultraTitus", "ultraApothecary"], profile.Teams.Teams![0].Chars);
    }

    private sealed class ProfileHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
    }
}
