using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.GameDomain;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Tests;

/// <summary>Coverage for <c>goal-target-editing</c> (openspec change edit-goal-targets-in-place):
/// <c>PUT /me/goals/{id}/target</c> replaces an in-flight goal's end target in place, revision-checked,
/// with creation-equivalent validation and a <c>TargetChanged</c> history event. InMemory API tests exercise
/// the pre-checks and mapping; the JSON event persistence is covered by the Postgres integration tests.</summary>
public sealed class GoalTargetEditEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string CharacterId = "blackTerminator";
    private const string RelevantUpgradeId = "upgHpC014"; // on blackTerminator's rank-up ladder
    private const string UnrelatedUpgradeId = "upgArmC001";

    private static CreateGoalRequest Goal(string goalType, CreateGoalConfigRequest config) =>
        new("character", CharacterId, goalType, config, null);

    private static readonly CreateGoalRequest RankGoal =
        Goal("rank", new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 5, false, 0)));

    private static UpdateGoalTargetRequest RankEdit(long revision, int end, bool pointFive = false, int applied = 0) =>
        new(revision, new GoalTargetEditRequest(Rank: new RankEndTargetRequest(end, pointFive, applied)));

    [Fact]
    public async Task RankTargetAdvancesInPlaceKeepingIdentityMembershipsOrderAndSnapshot()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var projectA = await GetDefaultProjectAsync(client);
        var projectB = await CreateProjectAsync(client, "Second plan");
        var created = await CreateGoalAsync(
            client,
            RankGoal with
            {
                Projects = [new ProjectPriorityRequest(projectA.ProjectId), new ProjectPriorityRequest(projectB.ProjectId)],
                Snapshot = new CreateGoalSnapshotRequest(InitialRank: "Stone1"),
            });
        var other = await CreateGoalAsync(
            client,
            Goal("level", new CreateGoalConfigRequest(Level: new LevelTargetRequest(1, 10))) with
            {
                Projects = [new ProjectPriorityRequest(projectA.ProjectId)],
            });
        var orderBefore = await PrioritiesAsync(client, projectA.ProjectId);
        var pause = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}/status", new UpdateGoalStatusRequest("paused"),
            TestContext.Current.CancellationToken);
        pause.EnsureSuccessStatusCode();
        var before = await GetGoalAsync(client, created.GoalId);

        var response = await PutTargetAsync(client, created.GoalId, RankEdit(before.Revision, 7));

        response.EnsureSuccessStatusCode();
        var updated = await ReadAsync<GoalDetailResponse>(response);
        var fresh = await GetGoalAsync(client, created.GoalId);
        Assert.Equal(updated.Revision, fresh.Revision);
        Assert.True(fresh.Revision > before.Revision);
        Assert.Equal(7, fresh.Config.Rank!.End);
        Assert.Equal(before.Config.Rank!.Start, fresh.Config.Rank.Start); // baseline preserved
        Assert.Equal(created.GoalId, fresh.GoalId);
        Assert.Equal("Paused", fresh.Status); // lifecycle status untouched
        Assert.Equal(before.ProjectIds.Order(), fresh.ProjectIds.Order());
        Assert.Equal(before.Snapshot!.InitialRank, fresh.Snapshot!.InitialRank);
        Assert.Equal(orderBefore, await PrioritiesAsync(client, projectA.ProjectId));

        var changed = Assert.Single(fresh.Events, e => e.Type == GoalEventType.TargetChanged);
        Assert.Equal(5, changed.PreviousTarget!.RankEnd);
        Assert.Equal(7, changed.NewTarget!.RankEnd);
        Assert.Equal(before.Events.Count + 1, fresh.Events.Count);
        Assert.NotEqual(other.GoalId, fresh.GoalId);
    }

    [Fact]
    public async Task NonTargetFieldsAreUnchangedByATargetEdit()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(
            client,
            RankGoal with { Config = RankGoal.Config with { FarmingStrategy = "EveryStep" } });
        var notes = await client.PutAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}",
            new UpdateGoalRequest("keep me", null, "EveryStep"),
            TestContext.Current.CancellationToken);
        notes.EnsureSuccessStatusCode();
        var before = await GetGoalAsync(client, created.GoalId);

        (await PutTargetAsync(client, created.GoalId, RankEdit(before.Revision, 6, applied: 2)))
            .EnsureSuccessStatusCode();

        var after = await GetGoalAsync(client, created.GoalId);
        Assert.Equal("keep me", after.Notes);
        Assert.Equal("EveryStep", after.Config.FarmingStrategy);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.CreatedAt, after.CreatedAt);
        Assert.Equal(6, after.Config.Rank!.End);
        Assert.Equal(2, after.Config.Rank.EndAppliedUpgrades);
    }

    [Fact]
    public async Task AscensionTargetCanBeEdited()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(
            client,
            Goal("ascension", new CreateGoalConfigRequest(
                Progression: new ProgressionTargetRequest("Common:None", "Common:OneStar"))));

        var response = await PutTargetAsync(
            client,
            created.GoalId,
            new UpdateGoalTargetRequest(
                created.Revision, new GoalTargetEditRequest(Progression: new ProgressionEndTargetRequest("Common:TwoStars"))));

        response.EnsureSuccessStatusCode();
        var updated = await GetGoalAsync(client, created.GoalId);
        Assert.Equal("Common:TwoStars", updated.Config.Progression!.End);
        Assert.Equal("Common:None", updated.Config.Progression.Start);
    }

    [Fact]
    public async Task LevelTargetCanBeEdited()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(
            client, Goal("level", new CreateGoalConfigRequest(Level: new LevelTargetRequest(1, 10))));

        var response = await PutTargetAsync(
            client,
            created.GoalId,
            new UpdateGoalTargetRequest(created.Revision, new GoalTargetEditRequest(Level: new LevelEndTargetRequest(20))));

        response.EnsureSuccessStatusCode();
        var updated = await GetGoalAsync(client, created.GoalId);
        Assert.Equal(20, updated.Config.Level!.End);
        Assert.Equal(1, updated.Config.Level.Start);
    }

    [Fact]
    public async Task AbilityTargetCanBeEdited()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(
            client, Goal("ability", new CreateGoalConfigRequest(Ability: new AbilityTargetRequest(0, 3, 0, 3))));

        var response = await PutTargetAsync(
            client,
            created.GoalId,
            new UpdateGoalTargetRequest(created.Revision, new GoalTargetEditRequest(Ability: new AbilityEndTargetRequest(5, 4))));

        response.EnsureSuccessStatusCode();
        var updated = await GetGoalAsync(client, created.GoalId);
        Assert.Equal(5, updated.Config.Ability!.ActiveEnd);
        Assert.Equal(4, updated.Config.Ability.PassiveEnd);
        Assert.Equal(0, updated.Config.Ability.ActiveStart);
    }

    [Fact]
    public async Task UpgradeTargetCanBeEditedAndIsRecordedInHistory()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(
            client,
            Goal("upgrade", new CreateGoalConfigRequest(
                Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest(RelevantUpgradeId, 2)]))));

        var response = await PutTargetAsync(
            client,
            created.GoalId,
            new UpdateGoalTargetRequest(
                created.Revision,
                new GoalTargetEditRequest(
                    Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest(RelevantUpgradeId, 9)]))));

        response.EnsureSuccessStatusCode();
        var updated = await GetGoalAsync(client, created.GoalId);
        Assert.Equal(9, Assert.Single(updated.Config.Upgrade!.Targets).Quantity);
        var changed = Assert.Single(updated.Events, e => e.Type == GoalEventType.TargetChanged);
        Assert.Equal(2, Assert.Single(changed.PreviousTarget!.UpgradeTargets!).Quantity);
        Assert.Equal(9, Assert.Single(changed.NewTarget!.UpgradeTargets!).Quantity);
    }

    [Fact]
    public async Task UnlockGoalHasNoAdjustableTarget()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client, Goal("unlock", new CreateGoalConfigRequest()));

        var response = await PutTargetAsync(client, created.GoalId, RankEdit(created.Revision, 7));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(created.Revision, (await GetGoalAsync(client, created.GoalId)).Revision);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("archived")]
    public async Task HistoricalGoalTargetCannotBeEdited(string status)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client, RankGoal);
        (await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}/status", new UpdateGoalStatusRequest(status),
            TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var before = await GetGoalAsync(client, created.GoalId);

        var response = await PutTargetAsync(client, created.GoalId, RankEdit(before.Revision, 7));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(5, (await GetGoalAsync(client, created.GoalId)).Config.Rank!.End);
    }

    [Fact]
    public async Task MalformedOrMismatchedTargetGroupsAreRejectedWithoutChange()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client, RankGoal);

        UpdateGoalTargetRequest[] bad =
        [
            new(created.Revision, new GoalTargetEditRequest()), // no group at all
            new(created.Revision, new GoalTargetEditRequest(Level: new LevelEndTargetRequest(20))), // wrong kind
            new(created.Revision, new GoalTargetEditRequest( // mixed groups
                Rank: new RankEndTargetRequest(7, false, 0), Level: new LevelEndTargetRequest(20))),
        ];
        foreach (var request in bad)
        {
            var response = await PutTargetAsync(client, created.GoalId, request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var after = await GetGoalAsync(client, created.GoalId);
        Assert.Equal(created.Revision, after.Revision);
        Assert.Equal(5, after.Config.Rank!.End);
    }

    [Theory]
    [InlineData(1)] // not above the stored baseline start
    [InlineData(0)]
    [InlineData(999)] // beyond the rank ladder
    public async Task InvalidRankTargetsAreRejectedWithCreationRules(int end)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client, RankGoal);

        var response = await PutTargetAsync(client, created.GoalId, RankEdit(created.Revision, end));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(created.Revision, (await GetGoalAsync(client, created.GoalId)).Revision);
    }

    [Fact]
    public async Task InvalidUpgradeAndLevelTargetsAreRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var upgrade = await CreateGoalAsync(
            client,
            Goal("upgrade", new CreateGoalConfigRequest(
                Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest(RelevantUpgradeId, 2)]))));
        var level = await CreateGoalAsync(
            client, Goal("level", new CreateGoalConfigRequest(Level: new LevelTargetRequest(1, 10))));

        var irrelevant = await PutTargetAsync(
            client,
            upgrade.GoalId,
            new UpdateGoalTargetRequest(
                upgrade.Revision,
                new GoalTargetEditRequest(
                    Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest(UnrelatedUpgradeId, 1)]))));
        var tooHigh = await PutTargetAsync(
            client,
            level.GoalId,
            new UpdateGoalTargetRequest(level.Revision, new GoalTargetEditRequest(Level: new LevelEndTargetRequest(61))));

        Assert.Equal(HttpStatusCode.BadRequest, irrelevant.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooHigh.StatusCode);
    }

    [Fact]
    public async Task AlreadyReachedTargetIsSavedWithoutChangingStatus()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory, subject: "target-edit-reached");
        var created = await CreateGoalAsync(
            client, Goal("rank", new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 12, false, 0))));
        await SeedProgressAsync(
            "target-edit-reached",
            new PlayerCharacterRecord { UnitId = UnitId.From(CharacterId), Rank = (UnitRank)8, XpLevel = 60 });

        // The unit is now at rank 8, past the new target of 5 — a valid target relative to the baseline.
        var response = await PutTargetAsync(client, created.GoalId, RankEdit(created.Revision, 5));

        response.EnsureSuccessStatusCode();
        var after = await GetGoalAsync(client, created.GoalId);
        Assert.Equal(5, after.Config.Rank!.End);
        Assert.Equal("Active", after.Status);
    }

    [Fact]
    public async Task RankTargetHeldInAnotherOfTheGoalsProjectsIsAnAtomicConflict()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var projectA = await GetDefaultProjectAsync(client);
        var projectB = await CreateProjectAsync(client, "Second plan");
        var shared = await CreateGoalAsync(
            client,
            RankGoal with
            {
                Projects = [new ProjectPriorityRequest(projectA.ProjectId), new ProjectPriorityRequest(projectB.ProjectId)],
            });
        var occupant = await CreateGoalAsync(
            client,
            RankGoal with
            {
                Config = new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 7, false, 0)),
                Projects = [new ProjectPriorityRequest(projectB.ProjectId)],
            });

        var response = await PutTargetAsync(client, shared.GoalId, RankEdit(shared.Revision, 7));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var conflict = await ReadAsync<ProjectGoalSlotConflictResponse>(response);
        Assert.Equal(projectB.ProjectId, conflict.ProjectId);
        Assert.Equal(occupant.GoalId, conflict.ExistingGoalId);
        Assert.Equal("7:0", conflict.NormalizedTarget);
        var unchanged = await GetGoalAsync(client, shared.GoalId);
        Assert.Equal(5, unchanged.Config.Rank!.End);
        Assert.Equal(shared.Revision, unchanged.Revision);
        Assert.DoesNotContain(unchanged.Events, e => e.Type == GoalEventType.TargetChanged);
    }

    [Fact]
    public async Task RankCollisionNamesEveryConflictingProject()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var projectA = await GetDefaultProjectAsync(client);
        var projectB = await CreateProjectAsync(client, "Second plan");
        var shared = await CreateGoalAsync(
            client,
            RankGoal with
            {
                Projects = [new ProjectPriorityRequest(projectA.ProjectId), new ProjectPriorityRequest(projectB.ProjectId)],
            });
        var seven = new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 7, false, 0));
        var holderA = await CreateGoalAsync(
            client, RankGoal with { Config = seven, Projects = [new ProjectPriorityRequest(projectA.ProjectId)] });
        var holderB = await CreateGoalAsync(
            client, RankGoal with { Config = seven, Projects = [new ProjectPriorityRequest(projectB.ProjectId)] });

        var response = await PutTargetAsync(client, shared.GoalId, RankEdit(shared.Revision, 7));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var conflict = await ReadAsync<ProjectGoalSlotConflictResponse>(response);
        var entries = Assert.IsAssignableFrom<IReadOnlyList<ProjectGoalSlotConflictEntry>>(conflict.Conflicts);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.ProjectId == projectA.ProjectId && e.ExistingGoalId == holderA.GoalId);
        Assert.Contains(entries, e => e.ProjectId == projectB.ProjectId && e.ExistingGoalId == holderB.GoalId);
    }

    [Fact]
    public async Task MissingUpgradeListOrBlankIdsAreABadRequestNotAServerError()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var upgrade = await CreateGoalAsync(
            client,
            Goal("upgrade", new CreateGoalConfigRequest(
                Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest(RelevantUpgradeId, 2)]))));
        var ascension = await CreateGoalAsync(
            client,
            Goal("ascension", new CreateGoalConfigRequest(
                Progression: new ProgressionTargetRequest("Common:None", "Common:OneStar"))));

        var noList = await client.PutAsJsonAsync(
            $"/api/v1/me/goals/{upgrade.GoalId}/target",
            new { expectedRevision = upgrade.Revision, target = new { upgrade = new { targets = (object?)null } } },
            TestContext.Current.CancellationToken);
        var blankId = await PutTargetAsync(
            client,
            upgrade.GoalId,
            new UpdateGoalTargetRequest(
                upgrade.Revision,
                new GoalTargetEditRequest(
                    Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest("  ", 1)]))));
        var noStep = await client.PutAsJsonAsync(
            $"/api/v1/me/goals/{ascension.GoalId}/target",
            new { expectedRevision = ascension.Revision, target = new { progression = new { end = (string?)null } } },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, noList.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blankId.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noStep.StatusCode);
    }

    [Fact]
    public async Task EditedRankTargetFreesTheOldSlotAndOccupiesTheNewOne()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client, RankGoal);
        (await PutTargetAsync(client, created.GoalId, RankEdit(created.Revision, 7))).EnsureSuccessStatusCode();

        // The old target (5) is free again; the new target (7) is now held.
        await CreateGoalAsync(client, RankGoal);
        var heldNow = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal with { Config = new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 7, false, 0)) },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, heldNow.StatusCode);
    }

    [Fact]
    public async Task StaleRevisionIsRejectedWithTheCurrentGoalAndNoOverwrite()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client, RankGoal);
        var loadedRevision = created.Revision;

        (await PutTargetAsync(client, created.GoalId, RankEdit(loadedRevision, 7))).EnsureSuccessStatusCode();
        var current = await GetGoalAsync(client, created.GoalId);

        var stale = await PutTargetAsync(client, created.GoalId, RankEdit(loadedRevision, 6));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var conflict = await ReadAsync<GoalRevisionConflictResponse>(stale);
        Assert.Equal("goalRevisionStale", conflict.IssueCode);
        Assert.Equal(current.Revision, conflict.Goal.Revision);
        Assert.Equal(7, conflict.Goal.Config.Rank!.End);
        var after = await GetGoalAsync(client, created.GoalId);
        Assert.Equal(7, after.Config.Rank!.End);
        Assert.Equal(current.Revision, after.Revision);
    }

    [Fact]
    public async Task RetryingAnIdenticalTargetLeavesRevisionAndHistoryUnchanged()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client, RankGoal);
        var request = RankEdit(created.Revision, 7);

        (await PutTargetAsync(client, created.GoalId, request)).EnsureSuccessStatusCode();
        var afterFirst = await GetGoalAsync(client, created.GoalId);
        var retry = await PutTargetAsync(client, created.GoalId, request); // same (now stale) revision

        retry.EnsureSuccessStatusCode();
        var afterRetry = await GetGoalAsync(client, created.GoalId);
        Assert.Equal(afterFirst.Revision, afterRetry.Revision);
        Assert.Equal(afterFirst.Events.Count, afterRetry.Events.Count);
        Assert.Single(afterRetry.Events, e => e.Type == GoalEventType.TargetChanged);
    }

    [Fact]
    public async Task GoalDetailExposesAMonotonicRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client, RankGoal);
        Assert.True(created.Revision > 0);

        var paused = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}/status", new UpdateGoalStatusRequest("paused"),
            TestContext.Current.CancellationToken);
        paused.EnsureSuccessStatusCode();

        Assert.True((await GetGoalAsync(client, created.GoalId)).Revision > created.Revision);
    }

    [Fact]
    public async Task OtherUsersGoalIsNotFound()
    {
        var owner = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(owner, RankGoal);
        var stranger = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await PutTargetAsync(stranger, created.GoalId, RankEdit(created.Revision, 7));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task SeedProgressAsync(string subject, PlayerCharacterRecord character)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().Include(entity => entity.Profile)
            .FirstAsync(entity => entity.Subject == subject, TestContext.Current.CancellationToken);
        db.PlayerDataSnapshots.Add(new PlayerDataSnapshot { Id = account.Profile!.Id, Characters = [character] });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> PutTargetAsync(HttpClient client, Guid goalId, UpdateGoalTargetRequest request) =>
        client.PutAsJsonAsync($"/api/v1/me/goals/{goalId}/target", request, TestContext.Current.CancellationToken);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        return body;
    }

    private static async Task<GoalDetailResponse> CreateGoalAsync(HttpClient client, CreateGoalRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/me/goals", request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadAsync<GoalDetailResponse>(response);
    }

    private static async Task<GoalDetailResponse> GetGoalAsync(HttpClient client, Guid goalId)
    {
        var goal = await client.GetFromJsonAsync<GoalDetailResponse>(
            $"/api/v1/me/goals/{goalId}", TestContext.Current.CancellationToken);
        Assert.NotNull(goal);
        return goal;
    }

    private static async Task<List<(Guid GoalId, int Priority)>> PrioritiesAsync(HttpClient client, Guid projectId)
    {
        var response = await client.GetFromJsonAsync<ListProjectGoalsResponse>(
            $"/api/v1/me/projects/{projectId}/goals", TestContext.Current.CancellationToken);
        Assert.NotNull(response);
        return response.Goals.Select(entry => (entry.Goal.GoalId, entry.Priority)).ToList();
    }

    private static async Task<ProjectSummaryResponse> CreateProjectAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/me/projects", new CreateProjectRequest(name, null, null), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadAsync<ProjectSummaryResponse>(response);
    }

    private static async Task<ProjectSummaryResponse> GetDefaultProjectAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<ListProjectsResponse>(
            "/api/v1/me/projects", TestContext.Current.CancellationToken);
        Assert.NotNull(response);
        return response.Projects.Single(project => project.IsDefault);
    }
}
