using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.Api.Features.TacticusIntegration;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.TacticusApi.Models.Guild;
using TacticusPlanner.TacticusApi.Models.GuildRaid;
using TacticusPlanner.TacticusApi.Models.Shared;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>Regression gate for optimize-api-handlers and rebuild-foundation-pre-launch: whole-request PostgreSQL
/// command counts per endpoint (including the caller's profile lookup), asserted as maxima so a handler cannot
/// silently grow more round trips.</summary>
public sealed class HandlerCommandCeilingTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const int ListGoals = 3;
    private const int CreateGoal = 6;
    private const int CombinedOneSpec = 6;
    private const int CombinedThreeSpecs = 6;
    private const int CompleteGoal = 5;
    private const int DeleteGoal = 6;
    private const int ReorderGoals = 4;
    private const int EditGoal = 6;
    private const int LegendaryEventPlanMutation = 3;
    // Three more small reads than the single whole-season load they replace, in exchange for O(1) rows.
    private const int RaidStatus = 7;
    private const int PlayerSync = 6;

    private static readonly CreateGoalRequest Rank = new(
        GoalEntityType.Character,
        "blackTerminator",
        GoalType.Rank,
        new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 3, false, 0)),
        null);

    private static CreateCombinedGoalsRequest Combined(params CombinedGoalSpec[] goals) =>
        new(GoalEntityType.Character, "blackTerminator", null, [.. goals]);

    private static readonly CombinedGoalSpec Unlock = new(GoalType.Unlock, new CreateGoalConfigRequest(), []);
    private static readonly CombinedGoalSpec RankAfterUnlock = new(
        GoalType.Rank, new CreateGoalConfigRequest(Rank: new RankTargetRequest(0, false, 0, 15, false, 0)), [0]);
    private static readonly CombinedGoalSpec Ability = new(
        GoalType.Ability, new CreateGoalConfigRequest(Ability: new AbilityTargetRequest(0, 3, 0, 3)), []);

    [Fact]
    public async Task GetGoalsStaysWithinCeiling()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        await CreateAsync(client);
        AssertWithin(ListGoals, await client.GetAsync("/api/v1/goals", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateGoalStaysWithinCeiling()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        await CreateAsync(client);
        AssertWithin(CreateGoal, await client.PostGoalAsync(Rank with
        {
            Config = new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 4, false, 0)),
        }));
    }

    [Fact]
    public async Task CombinedOneSpecStaysWithinCeiling()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        AssertWithin(CombinedOneSpec, await client.PostAsJsonAsync(
            "/api/v1/goals", Combined(Unlock), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CombinedThreeSpecsStayWithinCeiling()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        AssertWithin(CombinedThreeSpecs, await client.PostAsJsonAsync(
            "/api/v1/goals", Combined(Unlock, RankAfterUnlock, Ability), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompleteGoalStaysWithinCeiling()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var first = await CreateAsync(client);
        await CreateSecondAsync(client);

        // Completing the first goal leaves a gap that must be renumbered: the heaviest status path.
        var revision = (await client.ListGoalsAsync()).OrderRevision;
        AssertWithin(CompleteGoal, await client.PostAsJsonAsync(
            "/api/v1/goals/status", new UpdateGoalsStatusRequest([first.GoalId], GoalStatus.Completed, revision), ct));
    }

    [Fact]
    public async Task DeleteGoalStaysWithinCeiling()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        await CreateAsync(client);
        var second = await CreateSecondAsync(client);

        AssertWithin(DeleteGoal, await client.DeleteAsync(
            $"/api/v1/goals/{second.GoalId}", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EditGoalStaysWithinCeiling()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var first = await CreateAsync(client);
        await CreateSecondAsync(client);
        var revision = (await client.ListGoalsAsync()).OrderRevision;

        // Every section at once: the heaviest edit.
        AssertWithin(EditGoal, await client.EditGoalAsync(first.GoalId, new EditGoalRequest(
            Target: new UpdateGoalTargetRequest(first.Revision, new GoalTargetEditRequest(Rank: new RankEndTargetRequest(5, false, 0))),
            Details: new UpdateGoalRequest("notes", null),
            ProjectIds: first.ProjectIds,
            Priority: new EditGoalPriorityRequest(2, revision))));
    }

    [Fact]
    public async Task LegendaryEventPlanMutationStaysWithinCeiling()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        (await client.PutAsJsonAsync(
            "/api/v1/legendary-event-plans/votanUthar",
            new TacticusPlanner.Api.Features.LegendaryEventPlans.UpdateLegendaryEventPlanRequest(0, "first", false), ct))
            .EnsureSuccessStatusCode();

        AssertWithin(LegendaryEventPlanMutation, await client.PutAsJsonAsync(
            "/api/v1/legendary-event-plans/votanUthar",
            new TacticusPlanner.Api.Features.LegendaryEventPlans.UpdateLegendaryEventPlanRequest(1, "second", true), ct));
    }

    private static async Task<GoalDetailResponse> CreateSecondAsync(HttpClient client)
    {
        var response = await client.PostGoalAsync(Rank with
        {
            Config = new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 4, false, 0)),
        });
        response.EnsureSuccessStatusCode();
        return (await response.ReadCreatedGoalAsync())!;
    }

    [Fact]
    public async Task ReorderGoalsStaysWithinCeiling()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var first = await CreateAsync(client);
        var second = await CreateSecondAsync(client);
        var list = (await client.GetFromJsonAsync<ListGoalsResponse>("/api/v1/goals", ct))!;

        AssertWithin(ReorderGoals, await client.PutAsJsonAsync(
            "/api/v1/goals/order", new UpdateGoalOrderRequest([second.GoalId, first.GoalId], list.OrderRevision), ct));
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
        (await client.PostAsJsonAsync("/api/v1/guild/register", new RegisterGuildRequest(token), ct)).EnsureSuccessStatusCode();
        factory.TacticusApi.ConfigureGuildRaidResponse(token, ActiveRaid());
        (await client.PostAsync("/api/v1/guild/raid-status/refresh", null, ct)).EnsureSuccessStatusCode();

        AssertWithin(RaidStatus, await client.GetAsync("/api/v1/guild/raid-status", ct));
    }

    [Fact]
    public async Task PlayerSyncStaysWithinCeiling()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        (await client.PutAsJsonAsync(
            "/api/v1/account/tacticus-integration", new UpdateTacticusIntegrationRequest(FakeTacticusApi.ValidKey, null), ct))
            .EnsureSuccessStatusCode();

        // The second sync is the steady-state path: the snapshot row already exists.
        (await client.PostAsync("/api/v1/player-data/sync", null, ct)).EnsureSuccessStatusCode();
        AssertWithin(PlayerSync, await client.PostAsync("/api/v1/player-data/sync", null, ct));
    }

    private static async Task<GoalDetailResponse> CreateAsync(HttpClient client)
    {
        var response = await client.PostGoalAsync(Rank);
        response.EnsureSuccessStatusCode();
        return (await response.ReadCreatedGoalAsync())!;
    }

    private void AssertWithin(int ceiling, HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var actual = factory.CommandsFor(response);
        Assert.True(
            actual <= ceiling,
            $"{response.RequestMessage!.Method} {response.RequestMessage.RequestUri!.AbsolutePath}: {actual} commands, ceiling {ceiling}"
                + string.Concat(factory.CommandTextsFor(response).Select(text =>
                    Environment.NewLine + "  " + string.Concat(text.ReplaceLineEndings(" ").Take(140)))));
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
