using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.Api.Features.TacticusIntegration;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.TacticusApi.Models.Guild;
using TacticusPlanner.TacticusApi.Models.GuildRaid;
using TacticusPlanner.TacticusApi.Models.Shared;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>Regression gate for optimize-api-handlers: whole-request PostgreSQL command counts per
/// endpoint, asserted as maxima so a handler cannot silently grow more round trips.</summary>
public sealed class HandlerCommandCeilingTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const int ListGoals = 3;
    private const int CreateGoal = 10;
    private const int CombinedOneSpec = 10;
    private const int CombinedThreeSpecs = 10;
    private const int CompleteGoal = 12;
    private const int DeleteGoal = 6;
    private const int ReorderGoals = 4;
    // Three more small reads than the single whole-season load they replace, in exchange for O(1) rows.
    private const int RaidStatus = 7;
    private const int PlayerSync = 6;

    private static readonly CreateGoalRequest Rank = new(
        "character",
        "blackTerminator",
        "rank",
        new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 3, false, 0)),
        null);

    private static CreateCombinedGoalsRequest Combined(params CombinedGoalSpec[] goals) =>
        new("character", "blackTerminator", null, [.. goals]);

    private static readonly CombinedGoalSpec Unlock = new("unlock", new CreateGoalConfigRequest(), []);
    private static readonly CombinedGoalSpec RankAfterUnlock = new(
        "rank", new CreateGoalConfigRequest(Rank: new RankTargetRequest(0, false, 0, 15, false, 0)), [0]);
    private static readonly CombinedGoalSpec Ability = new(
        "ability", new CreateGoalConfigRequest(Ability: new AbilityTargetRequest(0, 3, 0, 3)), []);

    [Fact]
    public async Task GetGoalsStaysWithinCeiling()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        await CreateAsync(client);
        AssertWithin(ListGoals, await client.GetAsync("/api/v1/me/goals", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateGoalStaysWithinCeiling()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        await CreateAsync(client);
        AssertWithin(CreateGoal, await client.PostAsJsonAsync("/api/v1/me/goals", Rank with
        {
            Config = new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 4, false, 0)),
        }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CombinedOneSpecStaysWithinCeiling()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        AssertWithin(CombinedOneSpec, await client.PostAsJsonAsync(
            "/api/v1/me/goals/combined", Combined(Unlock), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CombinedThreeSpecsStayWithinCeiling()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        AssertWithin(CombinedThreeSpecs, await client.PostAsJsonAsync(
            "/api/v1/me/goals/combined", Combined(Unlock, RankAfterUnlock, Ability), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompleteGoalStaysWithinCeiling()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var first = await CreateAsync(client);
        await CreateSecondAsync(client);

        // Completing the first goal leaves a gap that must be renumbered: the heaviest status path.
        AssertWithin(CompleteGoal, await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{first.GoalId}/status", new UpdateGoalStatusRequest("completed"), ct));
    }

    [Fact]
    public async Task DeleteGoalStaysWithinCeiling()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        await CreateAsync(client);
        var second = await CreateSecondAsync(client);

        AssertWithin(DeleteGoal, await client.DeleteAsync(
            $"/api/v1/me/goals/{second.GoalId}", TestContext.Current.CancellationToken));
    }

    private static async Task<GoalDetailResponse> CreateSecondAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/me/goals", Rank with
        {
            Config = new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 4, false, 0)),
        }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task ReorderGoalsStaysWithinCeiling()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var first = await CreateAsync(client);
        var second = await CreateSecondAsync(client);
        var list = (await client.GetFromJsonAsync<ListGoalsResponse>("/api/v1/me/goals", ct))!;

        AssertWithin(ReorderGoals, await client.PutAsJsonAsync(
            "/api/v1/me/goals/order", new UpdateGoalOrderRequest([second.GoalId, first.GoalId], list.OrderRevision), ct));
    }

    [Fact]
    public async Task RaidStatusStaysWithinCeiling()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, tacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var token = $"ceiling-token-{Guid.NewGuid()}";
        factory.TacticusApi.ConfigureGuildResponse(
            token,
            FakeTacticusApi.BuildGuildResponse(
                Guid.NewGuid(), $"C{Guid.NewGuid():N}"[..10], "Ceiling Guild", 10,
                (tacticusUserId, GuildRole.LEADER, 50, null)));
        (await client.PostAsJsonAsync("/api/v1/guilds/register", new RegisterGuildRequest(token), ct)).EnsureSuccessStatusCode();
        factory.TacticusApi.ConfigureGuildRaidResponse(token, ActiveRaid());
        (await client.PostAsync("/api/v1/guilds/me/raid-status/refresh", null, ct)).EnsureSuccessStatusCode();

        AssertWithin(RaidStatus, await client.GetAsync("/api/v1/guilds/me/raid-status", ct));
    }

    [Fact]
    public async Task PlayerSyncStaysWithinCeiling()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        (await client.PutAsJsonAsync(
            "/api/v1/me/tacticus-integration", new UpdateTacticusIntegrationRequest(FakeTacticusApi.ValidKey, null), ct))
            .EnsureSuccessStatusCode();

        // The second sync is the steady-state path: the snapshot row already exists.
        (await client.PostAsync("/api/v1/tacticus-integration/player-sync", null, ct)).EnsureSuccessStatusCode();
        AssertWithin(PlayerSync, await client.PostAsync("/api/v1/tacticus-integration/player-sync", null, ct));
    }

    private static async Task<GoalDetailResponse> CreateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/me/goals", Rank, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken))!;
    }

    private void AssertWithin(int ceiling, HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var actual = factory.CommandsFor(response);
        Assert.True(actual <= ceiling, $"{response.RequestMessage!.Method} {response.RequestMessage.RequestUri!.AbsolutePath}: {actual} commands, ceiling {ceiling}");
    }

    private GuildRaidResponse ActiveRaid()
    {
        var catalog = factory.Services.GetRequiredService<IGameCatalogProvider>().Current;
        var config = catalog.RaidBossesView.Seasons.OrderBy(pair => pair.Key, StringComparer.Ordinal).First();
        var tier = config.Value.Tiers[0];
        var set = tier.Sets[0];
        var boss = set.Encounters.First(encounter => encounter.EncounterType == "Boss");
        var maximumHp = catalog.RaidBossesView.Bosses
            .Single(unit => unit.UnitSetId == boss.UnitSetId)
            .StatProgression[boss.ProgressionIndex - 1].Health;
        return new GuildRaidResponse
        {
            Season = 77,
            SeasonConfigId = config.Key,
            Entries =
            [
                new GuildRaidEntry
                {
                    UserId = Guid.NewGuid(),
                    Tier = tier.Tier,
                    Set = set.Set,
                    EncounterIndex = boss.EncounterIndex,
                    RemainingHp = maximumHp / 2,
                    MaxHp = maximumHp,
                    EncounterType = EncounterType.Boss,
                    UnitId = $"{boss.UnitSetId}:{boss.ProgressionIndex}",
                    Rarity = Rarity.Common,
                    DamageDealt = maximumHp / 2,
                    DamageType = DamageType.Battle,
                    CompletedOn = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    GlobalConfigHash = "test-config",
                },
            ],
        };
    }
}
